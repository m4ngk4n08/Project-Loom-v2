using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using System.Threading.Tasks;
using Loom.Dashboard.Extensions;
using Loom.Telemetry.Alerting;
using Loom.Telemetry.Alerting.Interfaces;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Loom.Telemetry.Tests.Dashboard;

/// <summary>
/// HTTP-level coverage for /api/alerts. These four handlers had none: they were rewritten
/// from a process-global rule list to an injected IAlertRuleRegistry with nothing gating
/// the result.
///
/// The app under test maps only the alert group, onto a WebApplication carrying just the
/// three services those handlers resolve. Going through MapLoomDashboard would pull in the
/// whole dashboard service graph plus AddLoomSecurity, which throws unless
/// LOOM_JWT_KEY_FILE/LOOM_AUTH_USERS_FILE point at real key material - not available in CI
/// (see LoomDashboardApiTests for the same constraint). No auth middleware is installed
/// here, so the handlers are reachable directly; that is the point, and it is why these
/// tests assert nothing about authentication.
/// </summary>
public class AlertEndpointTests
{
    /// <summary>A started app bound to an ephemeral loopback port, plus a client aimed at it.</summary>
    private sealed class AlertApi : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required HttpClient Client { get; init; }
        public required AlertRuleRegistry Registry { get; init; }
        public required Channel<AlertNotification> Channel { get; init; }
        public required ISilenceStore SilenceStore { get; init; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    private static async Task<AlertApi> StartAsync(Action<AlertRuleRegistry>? seed = null)
    {
        var registry = new AlertRuleRegistry();
        seed?.Invoke(registry);

        // Unbounded rather than the bounded channel AddLoomAlerting builds: nothing drains it
        // here, and a drop-oldest bound would make "did the handler write?" untestable.
        var channel = System.Threading.Channels.Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Registered by hand instead of via AddLoomAlerting, which also adds two hosted
        // services - and AlertEvaluationHostedService needs an IMetricStore. These are
        // endpoint tests, not an evaluation-loop test.
        builder.Services.AddSingleton<IAlertRuleRegistry>(registry);
        builder.Services.AddSingleton(channel);
        // Registered as ISilenceStore, not as InMemorySilenceStore: the handler takes the
        // interface, and the request-delegate generator treats an unregistered interface
        // parameter as a JSON body rather than a service - which turns into a silent 400 on
        // a PUT with no body, not a DI error.
        builder.Services.AddSingleton<ISilenceStore>(silenceStore);

        var app = builder.Build();
        app.MapGroup("/api").MapAlertEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return new AlertApi
        {
            App = app,
            Client = new HttpClient { BaseAddress = new Uri(address) },
            Registry = registry,
            Channel = channel,
            SilenceStore = silenceStore
        };
    }

    private static AlertRule Rule(string name, string metricName, int windowMinutes = 1) =>
        new(name, metricName, TimeSpan.FromMinutes(windowMinutes)) { Condition = _ => false };

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo)
        where T : class =>
        JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), typeInfo);

    [Fact]
    public async Task GetAlerts_ReturnsEveryRegisteredRule()
    {
        await using var api = await StartAsync(r =>
        {
            r.Add(Rule("HighCpu", "cpu-usage"));
            r.Add(Rule("HighMemory", "working-set", windowMinutes: 5));
        });

        var response = await api.Client.GetAsync("/api/alerts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rules = await ReadAsync(response, LoomJsonSerializerContext.Default.ListAlertConfigDto);

        Assert.NotNull(rules);
        Assert.Equal(2, rules.Count);
        Assert.Equal("HighCpu", rules[0].Name);
        Assert.Equal("cpu-usage", rules[0].MetricName);
        Assert.Equal(TimeSpan.FromMinutes(1), rules[0].Window);
        Assert.Equal("HighMemory", rules[1].Name);
        Assert.Equal(TimeSpan.FromMinutes(5), rules[1].Window);
    }

    [Fact]
    public async Task GetAlerts_EmptyRegistry_ReturnsAnEmptyArrayNotAnError()
    {
        await using var api = await StartAsync();

        var response = await api.Client.GetAsync("/api/alerts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rules = await ReadAsync(response, LoomJsonSerializerContext.Default.ListAlertConfigDto);
        Assert.NotNull(rules);
        Assert.Empty(rules);
    }

    [Fact]
    public async Task GetAlerts_RuleAddedAfterStartup_IsVisible()
    {
        // The whole point of the refactor: the endpoint reads the live singleton, so a rule
        // added by an alert-management call is served without restarting the host.
        await using var api = await StartAsync();

        api.Registry.Add(Rule("AddedLate", "late-metric"));
        var response = await api.Client.GetAsync("/api/alerts");

        var rules = await ReadAsync(response, LoomJsonSerializerContext.Default.ListAlertConfigDto);
        Assert.NotNull(rules);
        var rule = Assert.Single(rules);
        Assert.Equal("AddedLate", rule.Name);
    }

    [Fact]
    public async Task GetAlert_KnownName_ReturnsThatRule()
    {
        await using var api = await StartAsync(r => r.Add(Rule("HighCpu", "cpu-usage")));

        var response = await api.Client.GetAsync("/api/alerts/HighCpu");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rule = await ReadAsync(response, LoomJsonSerializerContext.Default.AlertConfigDto);
        Assert.NotNull(rule);
        Assert.Equal("HighCpu", rule.Name);
        Assert.Equal("cpu-usage", rule.MetricName);
    }

    [Fact]
    public async Task GetAlert_UnknownName_Returns404()
    {
        await using var api = await StartAsync(r => r.Add(Rule("HighCpu", "cpu-usage")));

        var response = await api.Client.GetAsync("/api/alerts/NoSuchAlert");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TestAlert_KnownName_Returns202AndQueuesANotification()
    {
        await using var api = await StartAsync(r => r.Add(Rule("HighCpu", "cpu-usage")));

        var response = await api.Client.PostAsync("/api/alerts/HighCpu/test", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        Assert.True(api.Channel.Reader.TryRead(out var notification));
        Assert.Equal("HighCpu", notification!.Rule.Name);
        Assert.Equal("cpu-usage", notification.Observed.MetricName);
    }

    [Fact]
    public async Task TestAlert_UnknownName_Returns404AndQueuesNothing()
    {
        await using var api = await StartAsync(r => r.Add(Rule("HighCpu", "cpu-usage")));

        var response = await api.Client.PostAsync("/api/alerts/NoSuchAlert/test", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(api.Channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task SilenceAlert_KnownName_Returns204AndSilencesIt()
    {
        await using var api = await StartAsync(r => r.Add(Rule("HighCpu", "cpu-usage")));

        Assert.False(api.SilenceStore.IsSilenced("HighCpu"));

        var response = await api.Client.PutAsync("/api/alerts/HighCpu/silence?duration=00:05:00", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(api.SilenceStore.IsSilenced("HighCpu"));
    }

    [Fact]
    public async Task SilenceAlert_UnknownName_Returns404AndSilencesNothing()
    {
        // The 404 has to come before the write: silencing a name with no rule behind it
        // would leave an entry nothing can ever clear.
        await using var api = await StartAsync(r => r.Add(Rule("HighCpu", "cpu-usage")));

        var response = await api.Client.PutAsync("/api/alerts/NoSuchAlert/silence?duration=00:05:00", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(api.SilenceStore.IsSilenced("NoSuchAlert"));
    }
}
