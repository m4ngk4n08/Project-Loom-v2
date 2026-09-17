using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Loom.Telemetry.Tests.Dashboard;

/// <summary>
/// BACKLOG.md § 6.13: MapSpaFallback (EndpointExtensions.cs:602) served index.html for every
/// unmatched request, including /api/* paths that do not correspond to a real endpoint. An
/// unconfigured Explain route (no IExplainClient registered) or any typo'd API path answered
/// 200 text/html instead of 404, defeating the frontend's 404 => "not configured" contract.
///
/// Same bare-WebApplication pattern as ExplainEndpointTests: going through MapLoomDashboard
/// would pull in AddLoomSecurity, which throws without real key material CI does not have.
/// </summary>
public sealed class SpaFallbackTests
{
    private sealed class FakeExplainClient : IExplainClient
    {
        public Task<ExplainResult> ExplainAsync(ExplainPayload payload, CancellationToken ct) =>
            Task.FromResult(new ExplainResult(
                Explanation: "fake",
                ModelUsed: "fake-model",
                SentText: "fake sent text",
                InputTokens: 1,
                OutputTokens: 1));
    }

    private sealed class SpaApi : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required HttpClient Client { get; init; }
        public required string TempDir { get; init; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            try
            {
                Directory.Delete(TempDir, recursive: true);
            }
            catch
            {
                // best-effort cleanup; not the point of the test
            }
        }
    }

    private static async Task<SpaApi> StartAsync(bool mapLogEndpoints, IExplainClient? explainClient)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "loom-spa-fallback-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "index.html"), "<html><body>loom-spa</body></html>");
        var provider = new PhysicalFileProvider(tempDir);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        if (explainClient is not null)
        {
            builder.Services.AddSingleton(explainClient);
        }

        var app = builder.Build();

        if (mapLogEndpoints)
        {
            app.MapGroup("/api").MapLogEndpoints();
        }

        app.MapSpaFallback(provider);

        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return new SpaApi
        {
            App = app,
            Client = new HttpClient { BaseAddress = new Uri(address) },
            TempDir = tempDir
        };
    }

    private static HttpContent ExplainRequestBody() => System.Net.Http.Json.JsonContent.Create(
        new ExplainRequest { Template = "processing {OrderId}" },
        LoomJsonSerializerContext.Default.ExplainRequest);

    [Fact]
    public async Task PostApiDoesNotExist_Returns404_NotHtml()
    {
        await using var api = await StartAsync(mapLogEndpoints: false, explainClient: null);

        var response = await api.Client.PostAsync("/api/does-not-exist", new StringContent(""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetApiDoesNotExist_Returns404_NotHtml()
    {
        await using var api = await StartAsync(mapLogEndpoints: false, explainClient: null);

        var response = await api.Client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetApiDoesNotExist_MixedCase_Returns404_NotHtml()
    {
        await using var api = await StartAsync(mapLogEndpoints: false, explainClient: null);

        var response = await api.Client.GetAsync("/API/Does-Not-Exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetApiBare_Returns404_NotHtml()
    {
        await using var api = await StartAsync(mapLogEndpoints: false, explainClient: null);

        var response = await api.Client.GetAsync("/api");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetLogsDeepLink_Returns200_HtmlWithIndexContent()
    {
        await using var api = await StartAsync(mapLogEndpoints: false, explainClient: null);

        var response = await api.Client.GetAsync("/logs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("loom-spa", body);
    }

    [Fact]
    public async Task GetApiFoo_IsNotAnApiPrefixMatch_Returns200_Html()
    {
        await using var api = await StartAsync(mapLogEndpoints: false, explainClient: null);

        var response = await api.Client.GetAsync("/apifoo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PostExplain_NoExplainClientRegistered_Returns404_NotHtml()
    {
        await using var api = await StartAsync(mapLogEndpoints: true, explainClient: null);

        var response = await api.Client.PostAsync("/api/logs/explain", ExplainRequestBody());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PostExplain_WithExplainClientRegistered_RealRouteStillWinsOverFallback()
    {
        await using var api = await StartAsync(mapLogEndpoints: true, explainClient: new FakeExplainClient());

        var response = await api.Client.PostAsync("/api/logs/explain", ExplainRequestBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
