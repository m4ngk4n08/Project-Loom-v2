using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Loom.Dashboard;
using Loom.Dashboard.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Loom.Telemetry.Tests.Dashboard;

/// <summary>
/// BACKLOG.md § 6.30: MapDashboardEndpoints unconditionally mapped a root-level MapFallback
/// (via MapSpaFallback). A host embedding the library that maps its own SPA/catch-all fallback
/// gets two equal-precedence MapFallback registrations, and ASP.NET Core's documented behavior
/// for that is AmbiguousMatchException -> every unmatched request 500s. Fix: MapDashboardEndpoints
/// (and MapLoomDashboard) now take a `mapFallback` parameter, default true (preserving today's
/// behavior for the only current consumer, loom-dashboard); a host with its own fallback passes
/// false.
///
/// Goes through the bare MapDashboardEndpoints, not MapLoomDashboard - same rationale as
/// SpaFallbackTests: MapLoomDashboard requires UseLoomDashboard/AddLoomSecurity, which needs key
/// material CI does not have.
/// </summary>
public sealed class MapLoomDashboardFallbackTests
{
    private sealed class FallbackApi : IAsyncDisposable
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

    private static async Task<FallbackApi> StartAsync(bool mapFallback, bool addHostFallback)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "loom-mapfallback-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "index.html"), "<html><body>loom-spa</body></html>");
        var provider = new PhysicalFileProvider(tempDir);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<MetricsResponseBuilder>();

        var app = builder.Build();
        var metricsBuilder = app.Services.GetRequiredService<MetricsResponseBuilder>();

        app.MapDashboardEndpoints(
            targetPid: 1,
            sessionStartedAtUtc: DateTime.UtcNow,
            embeddedProvider: provider,
            metricsBuilder: metricsBuilder,
            mapFallback: mapFallback);

        if (addHostFallback)
        {
            // Stand-in for a host's own app.MapFallbackToFile("index.html")-style route.
            app.MapFallback(async context =>
            {
                context.Response.StatusCode = 200;
                await context.Response.WriteAsync("host-fallback");
            });
        }

        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return new FallbackApi
        {
            App = app,
            Client = new HttpClient { BaseAddress = new Uri(address) },
            TempDir = tempDir
        };
    }

    [Fact]
    public async Task DefaultMapFallbackTrue_PlusHostOwnFallback_CollidesWithAmbiguousMatch()
    {
        // Reproduces § 6.30: the default (mapFallback: true, matching pre-fix behavior for the
        // single current consumer) still collides if a host maps its own fallback alongside it.
        await using var api = await StartAsync(mapFallback: true, addHostFallback: true);

        var response = await api.Client.GetAsync("/some/unmatched/route");

        // ASP.NET Core's AmbiguousMatchException surfaces to the client as a 500.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task MapFallbackFalse_PlusHostOwnFallback_HostFallbackWins_No500()
    {
        await using var api = await StartAsync(mapFallback: false, addHostFallback: true);

        var response = await api.Client.GetAsync("/some/unmatched/route");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("host-fallback", body);
    }

    [Fact]
    public async Task MapFallbackTrue_NoHostFallback_ApiPrefix_Returns404_NotHtml()
    {
        // Existing coverage for the default's own behavior (no host collision involved).
        await using var api = await StartAsync(mapFallback: true, addHostFallback: false);

        var response = await api.Client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task MapFallbackTrue_NoHostFallback_NonApiRoute_ServesIndexHtml()
    {
        await using var api = await StartAsync(mapFallback: true, addHostFallback: false);

        var response = await api.Client.GetAsync("/logs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("loom-spa", body);
    }
}
