using System;
using Loom.Dashboard;
using Loom.Dashboard.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Loom.Telemetry.Tests.Dashboard;

/// <summary>
/// Regression coverage for the fail-closed guard: AddLoomDashboard/MapLoomDashboard must
/// never let a host serve the dashboard without the authentication middleware installed.
/// AddLoomDashboard also registers AddLoomSecurity, which throws unless
/// LOOM_JWT_KEY_FILE/LOOM_AUTH_USERS_FILE point at real key material - that is not
/// available in CI, so this file exercises MapLoomDashboard's guard directly against a
/// WebApplication with only MetricsResponseBuilder registered, rather than going through
/// AddLoomDashboard.
/// </summary>
public class LoomDashboardApiTests
{
    [Fact]
    public void MapLoomDashboard_WithoutUseLoomDashboard_ThrowsInvalidOperationException()
    {
        var app = WebApplication.CreateBuilder().Build();

        var ex = Assert.Throws<InvalidOperationException>(() => app.MapLoomDashboard(targetPid: 1234));
        Assert.Contains("UseLoomDashboard", ex.Message);
    }

    [Fact]
    public void MapLoomDashboard_AfterUseLoomDashboard_DoesNotThrow()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<MetricsResponseBuilder>();
        var app = builder.Build();
        app.UseRouting();
        app.UseLoomDashboard();

        var ex = Record.Exception(() => app.MapLoomDashboard(targetPid: 1234));

        Assert.Null(ex);
    }
}
