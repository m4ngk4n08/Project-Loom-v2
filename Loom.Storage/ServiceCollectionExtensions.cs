using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Loom.Storage;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLoomStorage(this IServiceCollection services, int bufferCapacity = 8192)
    {
        services.AddSingleton<IMetricStore>(sp =>
            new InMemoryMetricStore(bufferCapacity, logger: sp.GetService<ILogger<InMemoryMetricStore>>()));
        return services;
    }

    /// <summary>
    /// Registers <see cref="IMetricsService"/>, which measures <b>the calling process
    /// itself</b>. Deliberately separate from <see cref="AddLoomStorage"/>: a host that
    /// observes another process (for example the dashboard attached to a target PID)
    /// would otherwise resolve it by accident and report its own CPU and memory as
    /// though they were the monitored process's.
    /// </summary>
    public static IServiceCollection AddLoomSelfMetrics(this IServiceCollection services)
    {
        services.AddSingleton<IMetricsService, MetricsService>();
        return services;
    }

    public static IServiceCollection AddLoomLogStorage(this IServiceCollection services, int bufferCapacity = 8192)
    {
        services.AddSingleton<ILogStore>(new InMemoryLogStore(bufferCapacity));
        return services;
    }
}
