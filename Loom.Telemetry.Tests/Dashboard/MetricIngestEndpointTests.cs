using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Loom.Dashboard.Extensions;
using Loom.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Loom.Telemetry.Tests.Dashboard;

/// <summary>
/// Covers PROMPT-dashboard-review-fixes.md item 8: /api/metrics/ingest must validate the
/// whole batch before writing anything (no partial commit on a later invalid entry), and
/// a JSON `null` metric type - which passes `required` validation on the DTO - must return
/// 400, not crash the process with a NullReferenceException from ToLowerInvariant().
///
/// Same bare-WebApplication pattern as AlertEndpointTests/ExplainEndpointTests: only the
/// ingest group and an IMetricStore are registered, no security.
/// </summary>
public sealed class MetricIngestEndpointTests
{
    private sealed class IngestApi : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required HttpClient Client { get; init; }
        public required IMetricStore Store { get; init; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    private static async Task<IngestApi> StartAsync()
    {
        var store = new InMemoryMetricStore();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IMetricStore>(store);

        var app = builder.Build();
        app.MapGroup("/api").MapMetricIngestEndpoint();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return new IngestApi
        {
            App = app,
            Client = new HttpClient { BaseAddress = new Uri(address) },
            Store = store
        };
    }

    private static HttpContent RawJson(string json) =>
        new StringContent(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task ValidThenInvalidType_Returns400_AndWritesNothing()
    {
        await using var api = await StartAsync();

        var body = RawJson("""
            {
              "metrics": [
                { "name": "requests", "type": "Counter", "value": 1 },
                { "name": "bad", "type": "NotAType", "value": 1 }
              ]
            }
            """);

        var response = await api.Client.PostAsync("/api/metrics/ingest", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(api.Store.GetMetricNames());
    }

    [Fact]
    public async Task NullType_Returns400_NotServerError()
    {
        await using var api = await StartAsync();

        var body = RawJson("""
            {
              "metrics": [
                { "name": "requests", "type": null, "value": 1 }
              ]
            }
            """);

        var response = await api.Client.PostAsync("/api/metrics/ingest", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(api.Store.GetMetricNames());
    }

    [Fact]
    public async Task NullMetricsArray_Returns400()
    {
        await using var api = await StartAsync();

        var body = RawJson("""{ "metrics": null }""");

        var response = await api.Client.PostAsync("/api/metrics/ingest", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AllValid_Returns202_AndWritesEverything()
    {
        await using var api = await StartAsync();

        var body = RawJson("""
            {
              "metrics": [
                { "name": "requests", "type": "Counter", "value": 1 },
                { "name": "latency", "type": "Gauge", "value": 12.5 }
              ]
            }
            """);

        var response = await api.Client.PostAsync("/api/metrics/ingest", body);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(2, api.Store.GetMetricNames().Count);
    }
}
