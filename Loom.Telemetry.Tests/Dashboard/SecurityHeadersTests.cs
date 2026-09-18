using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Loom.Dashboard.Extensions;
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
/// BACKLOG.md § 6.29: Loom.Dashboard.AspNetCore set none of the security headers
/// Loom.Dashboard/Program.cs applied inline (CSP, X-Frame-Options, X-Content-Type-Options,
/// Referrer-Policy) - a host embedding the library got an unprotected dashboard with nothing
/// telling it to add these itself. UseLoomDashboardSecurityHeaders() must be callable BEFORE
/// UseStaticFiles/UseRouting and must still apply to a short-circuited static-file response.
///
/// Follow-up (§ 6.29 code review): the first cut applied these headers to EVERY response the
/// host serves, not just Loom's own routes. UseLoomDashboardSecurityHeaders() now scopes to
/// Loom's known path prefixes (/api, /ws/metrics, /ws/logs, /prometheus); a host's own routes
/// (e.g. "/custom" below) must never see these headers.
/// </summary>
public sealed class SecurityHeadersTests
{
    private sealed class HeaderApi : IAsyncDisposable
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

    private static async Task<HeaderApi> StartAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "loom-security-headers-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "index.html"), "<html>static</html>");
        var provider = new PhysicalFileProvider(tempDir);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();

        // Applied ahead of static files/routing, matching the required pipeline position.
        app.UseLoomDashboardSecurityHeaders();

        // Mounted under /api - one of Loom's own prefixes - so this still exercises "a
        // short-circuited static-file response carries the headers" now that the middleware is
        // scoped by path. (Loom's real static assets are served at the process root by the
        // loom-dashboard tool itself, outside this library's scoped prefixes entirely; see the
        // handback note on that gap.)
        app.UseStaticFiles(new StaticFileOptions { FileProvider = provider, RequestPath = "/api" });

        app.UseRouting();
        app.MapGet("/api/ping", () => "pong");
        app.MapGet("/custom", () => "host page");

        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return new HeaderApi
        {
            App = app,
            Client = new HttpClient { BaseAddress = new Uri(address) },
            TempDir = tempDir
        };
    }

    private static void AssertAllHeadersPresent(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var xcto));
        Assert.Equal("nosniff", xcto!.Single());

        Assert.True(response.Headers.TryGetValues("X-Frame-Options", out var xfo));
        Assert.Equal("DENY", xfo!.Single());

        Assert.True(response.Headers.TryGetValues("Referrer-Policy", out var rp));
        Assert.Equal("no-referrer", rp!.Single());

        Assert.True(response.Headers.TryGetValues("Content-Security-Policy", out var csp));
        Assert.Contains("default-src 'self'", csp!.Single());
    }

    private static void AssertNoLoomHeadersPresent(HttpResponseMessage response)
    {
        Assert.False(response.Headers.Contains("X-Content-Type-Options"));
        Assert.False(response.Headers.Contains("X-Frame-Options"));
        Assert.False(response.Headers.Contains("Referrer-Policy"));
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task ApiResponse_CarriesAllFourSecurityHeaders()
    {
        await using var api = await StartAsync();

        var response = await api.Client.GetAsync("/api/ping");

        AssertAllHeadersPresent(response);
    }

    [Fact]
    public async Task StaticFileResponse_ShortCircuitedBeforeRouting_StillCarriesAllFourSecurityHeaders()
    {
        await using var api = await StartAsync();

        var response = await api.Client.GetAsync("/api/index.html");

        AssertAllHeadersPresent(response);
    }

    // The bug § 6.29's code review found: the first cut stamped Loom's CSP/frame/sniff headers
    // onto every response the host serves, including its own pages. This is the test that
    // actually catches that - it fails against the unscoped middleware (every response gets the
    // headers) and only passes once UseLoomDashboardSecurityHeaders is scoped to Loom's own path
    // prefixes.
    [Fact]
    public async Task HostRoute_OutsideLoomPrefixes_NeverGetsLoomSecurityHeaders()
    {
        await using var api = await StartAsync();

        var loomResponse = await api.Client.GetAsync("/api/ping");
        var hostResponse = await api.Client.GetAsync("/custom");

        AssertAllHeadersPresent(loomResponse);
        AssertNoLoomHeadersPresent(hostResponse);
    }

    [Fact]
    public async Task SpaFallback_UnmatchedRoute_CarriesAllFourSecurityHeaders()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        app.UseLoomDashboardSecurityHeaders();
        app.UseRouting();
        app.MapSpaFallback(embeddedProvider: null);

        await app.StartAsync();
        await using var _ = app;

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        using var client = new HttpClient { BaseAddress = new Uri(address) };

        // No embeddedProvider, so this unmatched, non-/api route falls through to the SPA
        // fallback's "assets not found" 404 - MapSpaFallback's own handler sets the headers
        // directly, since its path ("/", here) isn't one of Loom's known prefixes.
        var response = await client.GetAsync("/");

        AssertAllHeadersPresent(response);
    }
}
