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

    private static async Task<ExplainApi> StartAsync(IExplainClient? explainClient)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        if (explainClient is not null)
            builder.Services.AddSingleton(explainClient);

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
}
