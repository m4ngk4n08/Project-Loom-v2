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

        app.UseStaticFiles(new StaticFileOptions { FileProvider = provider });

        app.UseRouting();
        app.MapGet("/api/ping", () => "pong");

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

        var response = await api.Client.GetAsync("/index.html");

        AssertAllHeadersPresent(response);
    }
}
