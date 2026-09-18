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
///
/// Second follow-up (§ 6.29, static-asset gap): UseLoomDashboardSecurityHeaders's path-prefix
/// scoping has no entry for the Angular bundle's own paths (content-hashed filenames, index.html)
/// because Loom.Dashboard/Program.cs mounts that provider with UseStaticFiles at the app root,
/// outside every scoped prefix, and static files are served before routing runs - so the
/// middleware never sees a matching path and MapSpaFallback's own header-setting never fires
/// either (the static-file middleware already short-circuited the request). The fix is
/// UseLoomDashboardStaticAssets(provider), which ties header-setting directly to that specific
/// UseStaticFiles registration instead of trying to recognize its paths.
///
/// Third follow-up (§ 6.29, host-route leak): the previous fix set the headers unconditionally
/// before calling next(), with no check on whether this fileProvider would actually serve the
/// request. A host's own route, registered downstream via UseRouting/Map* (which must come after
/// this call, per the ordering rules above), still got the headers forced onto it. The fix gates
/// header-setting on fileProvider.GetFileInfo(relativePath).Exists - the same check UseStaticFiles
/// itself makes - so only requests this specific provider actually serves get the headers,
/// regardless of what happens to unmatched requests downstream.
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

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();

        // Applied ahead of routing, matching the required pipeline position.
        app.UseLoomDashboardSecurityHeaders();

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

    private sealed class StaticAssetApi : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required HttpClient Client { get; init; }
        public required string LoomAssetsDir { get; init; }
        public required string HostAssetsDir { get; init; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            foreach (var dir in new[] { LoomAssetsDir, HostAssetsDir })
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch
                {
                    // best-effort cleanup; not the point of the test
                }
            }
        }
    }

    // Mounts Loom's static assets exactly the way Loom.Dashboard/Program.cs:173 does - via
    // UseLoomDashboardStaticAssets, at the app root, no RequestPath - alongside a second,
    // unrelated static-file mount registered the plain way (bare UseStaticFiles, not through the
    // wrapper) under its own RequestPath, standing in for a host's own static content.
    private static async Task<StaticAssetApi> StartWithStaticAssetsAsync()
    {
        var loomAssetsDir = Path.Combine(Path.GetTempPath(), "loom-security-headers-loom-assets-" + Guid.NewGuid());
        Directory.CreateDirectory(loomAssetsDir);
        File.WriteAllText(Path.Combine(loomAssetsDir, "index.html"), "<html>loom static</html>");
        var loomProvider = new PhysicalFileProvider(loomAssetsDir);

        var hostAssetsDir = Path.Combine(Path.GetTempPath(), "loom-security-headers-host-assets-" + Guid.NewGuid());
        Directory.CreateDirectory(hostAssetsDir);
        File.WriteAllText(Path.Combine(hostAssetsDir, "page.html"), "<html>host static</html>");
        var hostProvider = new PhysicalFileProvider(hostAssetsDir);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();

        // A host's own, unrelated static-file mount, registered the plain way (not through the
        // wrapper) and placed ahead of Loom's in the pipeline - exactly how a host embedding this
        // library would register its own, pre-existing static content. It is never touched by
        // the wrapper's header-setting middleware because it short-circuits the request before
        // reaching that middleware at all.
        app.UseStaticFiles(new StaticFileOptions { FileProvider = hostProvider, RequestPath = "/host-content" });

        // Root-mounted, via the wrapper - reproduces Program.cs:173 exactly.
        app.UseLoomDashboardStaticAssets(loomProvider);

        // A host's own route, mapped downstream via UseRouting/Map* - exactly the order the
        // wrapper's own doc comment tells a consumer to use. Its header-setting middleware must
        // NOT stamp Loom's headers onto this response just because the request passed through it
        // on the way to routing.
        app.UseRouting();
        app.MapGet("/custom", () => "host route");

        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return new StaticAssetApi
        {
            App = app,
            Client = new HttpClient { BaseAddress = new Uri(address) },
            LoomAssetsDir = loomAssetsDir,
            HostAssetsDir = hostAssetsDir
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

    // The regression § 6.29's second follow-up reproduced: mounting the embedded Angular
    // provider at the app root via a bare UseStaticFiles (exactly Program.cs:173, before this
    // fix) short-circuits the request before UseLoomDashboardSecurityHeaders's prefix check or
    // MapSpaFallback's own header-setting ever run, so the response carries no headers at all.
    // Ported here as a real test against UseLoomDashboardStaticAssets: fails against 60e02b4's
    // code (no wrapper existed, so a root-mounted UseStaticFiles is bare) and passes once the
    // static-file registration goes through the wrapper.
    [Fact]
    public async Task StaticAsset_MountedAtRootViaWrapper_CarriesAllFourSecurityHeaders()
    {
        await using var api = await StartWithStaticAssetsAsync();

        var response = await api.Client.GetAsync("/index.html");

        AssertAllHeadersPresent(response);
    }

    // Proves the fix didn't regress back to "everything gets headers" - the failure mode the
    // very first round's review caught. A host's own static-file mount, registered the plain way
    // and never routed through UseLoomDashboardStaticAssets, must carry none of Loom's headers,
    // even though Loom's own root-mounted static assets (served through the wrapper, in the same
    // pipeline) do.
    [Fact]
    public async Task HostOwnStaticFileMount_NotThroughWrapper_NeverGetsLoomSecurityHeaders()
    {
        await using var api = await StartWithStaticAssetsAsync();

        var loomResponse = await api.Client.GetAsync("/index.html");
        var hostResponse = await api.Client.GetAsync("/host-content/page.html");

        AssertAllHeadersPresent(loomResponse);
        AssertNoLoomHeadersPresent(hostResponse);
    }

    // Third follow-up's repro, ported permanently: a host's own route mapped downstream of
    // UseLoomDashboardStaticAssets via UseRouting/Map* - exactly the order the wrapper's doc
    // comment tells a consumer to use - must never get Loom's headers just because the request
    // passed through the wrapper's header-setting middleware on its way to routing. Fails against
    // a96727f (unconditional ApplyLoomSecurityHeaders before next()) and passes once
    // header-setting is gated on fileProvider.GetFileInfo(relativePath).Exists.
    [Fact]
    public async Task HostRoute_MappedDownstreamOfStaticAssetsWrapper_NeverGetsLoomSecurityHeaders()
    {
        await using var api = await StartWithStaticAssetsAsync();

        var loomResponse = await api.Client.GetAsync("/index.html");
        var hostResponse = await api.Client.GetAsync("/custom");

        AssertAllHeadersPresent(loomResponse);
        AssertNoLoomHeadersPresent(hostResponse);
    }

    // A path that resolves neither to a file in the wrapper's fileProvider nor to any mapped
    // route - a genuine 404. Confirms the gate is "does this specific provider have this file,"
    // not some broader heuristic that happens to also exclude mapped host routes.
    [Fact]
    public async Task PathMatchingNeitherFileNorRoute_NeverGetsLoomSecurityHeaders()
    {
        await using var api = await StartWithStaticAssetsAsync();

        var response = await api.Client.GetAsync("/does-not-exist-anywhere.html");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        AssertNoLoomHeadersPresent(response);
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
