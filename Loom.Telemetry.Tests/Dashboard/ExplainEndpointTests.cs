using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Loom.Dashboard.Extensions;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using Loom.Web.Contracts.Explain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Loom.Telemetry.Tests.Dashboard;

/// <summary>
/// PROMPT-assist-inversion.md: the library no longer knows about Anthropic (or any
/// provider) - it maps POST /api/logs/explain only when the host has registered an
/// IExplainClient, which MapLogEndpoints checks directly against the built service
/// provider at map time. These tests cover that gate: absent, the route does not exist
/// (404 from the router, not a 501 from a handler); present, the route exists and returns
/// whatever the registered client produces.
///
/// Same bare-WebApplication pattern as AlertEndpointTests: going through MapLoomDashboard
/// would pull in AddLoomSecurity, which throws without real LOOM_JWT_KEY_FILE /
/// LOOM_AUTH_USERS_FILE key material CI does not have.
/// </summary>
public sealed class ExplainEndpointTests
{
    private sealed class FakeExplainClient : IExplainClient
    {
        public Task<ExplainResult> ExplainAsync(ExplainPayload payload, CancellationToken ct) =>
            Task.FromResult(new ExplainResult(
                Explanation: "This looks like a routine checkout event.",
                ModelUsed: "fake-model-v1",
                SentText: "fake sent text",
                InputTokens: 12,
                OutputTokens: 34));
    }

    private sealed class ExplainApi : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required HttpClient Client { get; init; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    // Counts constructions, so a test can prove mapping the route never builds a client.
    private sealed class CountingExplainClient : IExplainClient
    {
        public static int Constructed;

        public CountingExplainClient() => Interlocked.Increment(ref Constructed);

        public Task<ExplainResult> ExplainAsync(ExplainPayload payload, CancellationToken ct) =>
            Task.FromResult(new ExplainResult("counted", "counting-model", "sent", 1, 1));
    }

    private static Task<ExplainApi> StartAsync(IExplainClient? explainClient) =>
        StartAsync(explainClient is null ? null : services => services.AddSingleton(explainClient));

    private static async Task<ExplainApi> StartAsync(Action<IServiceCollection>? register, string? environmentName = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environmentName });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        register?.Invoke(builder.Services);

        var app = builder.Build();
        app.MapGroup("/api").MapLogEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return new ExplainApi
        {
            App = app,
            Client = new HttpClient { BaseAddress = new Uri(address) }
        };
    }

    private static HttpContent ExplainRequestBody() => JsonContent(
        new ExplainRequest { Template = "processing {OrderId}" },
        LoomJsonSerializerContext.Default.ExplainRequest);

    private static System.Net.Http.Json.JsonContent JsonContent<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
        System.Net.Http.Json.JsonContent.Create(value, typeInfo);

    [Fact]
    public async Task NoExplainClientRegistered_RouteIsNotMapped_Returns404()
    {
        await using var api = await StartAsync(explainClient: null);

        var response = await api.Client.PostAsync("/api/logs/explain", ExplainRequestBody());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FakeExplainClientRegistered_RouteExists_ReturnsTheClientsResult()
    {
        await using var api = await StartAsync(new FakeExplainClient());

        var response = await api.Client.PostAsync("/api/logs/explain", ExplainRequestBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonSerializer.Deserialize(
            await response.Content.ReadAsStringAsync(),
            LoomJsonSerializerContext.Default.ExplainResponse);

        Assert.NotNull(body);
        Assert.Equal("This looks like a routine checkout event.", body.Explanation);
        Assert.Equal("fake-model-v1", body.ModelUsed);
        Assert.Equal(12, body.InputTokens);
        Assert.Equal(34, body.OutputTokens);
    }

    // Development turns on scope validation, so resolving a scoped service from the root
    // provider at map time throws. Checking registration must not resolve anything.
    [Fact]
    public async Task ScopedExplainClientInDevelopment_MapsWithoutResolving_AndServesRequests()
    {
        CountingExplainClient.Constructed = 0;

        await using var api = await StartAsync(
            services => services.AddScoped<IExplainClient, CountingExplainClient>(),
            environmentName: "Development");

        Assert.Equal(0, CountingExplainClient.Constructed);

        var response = await api.Client.PostAsync("/api/logs/explain", ExplainRequestBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, CountingExplainClient.Constructed);
    }
}
