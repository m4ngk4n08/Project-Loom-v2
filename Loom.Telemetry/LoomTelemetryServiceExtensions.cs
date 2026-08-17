using Microsoft.Extensions.DependencyInjection;
using System;

namespace Loom.Telemetry;

/// <summary>
/// Service collection extensions for Loom telemetry configuration.
/// </summary>
public static class LoomTelemetryServiceExtensions
{
    public static IServiceCollection AddLoomTelemetry(
        this IServiceCollection services,
        Action<LoomTelemetryOptions> configure)
    {
        var options = new LoomTelemetryOptions();
        configure(options);
        services.AddSingleton(options);
        return services;
    }
}
