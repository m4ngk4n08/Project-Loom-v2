using Microsoft.Extensions.DependencyInjection;
using System;

namespace Loom.Telemetry;

/// <summary>
/// Service collection extensions for Loom telemetry configuration.
/// </summary>
public static class LoomTelemetryServiceExtensions
{
    /// <summary>
    /// Registers Loom telemetry with no extra configuration. This is the whole call for a
    /// consumer that only uses <c>[LoomProfile]</c> and <c>[LoomTrack]</c>, which is most
    /// of them: <see cref="LoomTelemetryOptions"/> carries no settings of its own, so the
    /// callback overload would only ever be handed an empty lambda.
    /// </summary>
    public static IServiceCollection AddLoomTelemetry(this IServiceCollection services)
        => services.AddLoomTelemetry(static _ => { });

    /// <summary>
    /// Registers Loom telemetry and runs <paramref name="configure"/> against the options
    /// object. The options type is empty on its own; the other Loom packages hang their
    /// fluent surface off it as extension methods, so this overload is how alerting,
    /// collectors and exporters get configured.
    /// </summary>
    public static IServiceCollection AddLoomTelemetry(
        this IServiceCollection services,
        Action<LoomTelemetryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new LoomTelemetryOptions();
        configure(options);
        services.AddSingleton(options);
        return services;
    }
}
