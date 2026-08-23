using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Loom.Telemetry.Exporters.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Loom.Telemetry.Exporters;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the metric export system with the DI container.
    /// Sets up the collection timer, dispatch pipeline, and bounded channel with backpressure.
    /// </summary>
    public static IServiceCollection AddLoomExporting(
        this IServiceCollection services,
        Action<ExportOptions>? configure = null)
    {
        var options = new ExportOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        // Bounded channel with drop-oldest backpressure.
        // Prevents unbounded memory growth if exporters fall behind.
        var channel = Channel.CreateBounded<MetricBatch>(new BoundedChannelOptions(options.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
        services.AddSingleton(channel);

        // Status tracker for exporter health monitoring
        services.AddSingleton<ExportStatusTracker>();

        // Collection timer (producer)
        services.AddHostedService<ExportCollectionHostedService>();

        // Dispatch consumer
        services.AddHostedService<ExportDispatchHostedService>();

        return services;
    }

    /// <summary>
    /// Registers a metric exporter implementation.
    /// The exporter will receive all batches from the dispatch pipeline.
    /// </summary>
    public static IServiceCollection AddLoomExporter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        this IServiceCollection services)
        where T : class, IMetricExporter
    {
        services.AddSingleton<IMetricExporter, T>();
        return services;
    }
}
