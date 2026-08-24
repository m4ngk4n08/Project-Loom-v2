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

    public static IServiceCollection AddLoomLogStorage(this IServiceCollection services, int bufferCapacity = 8192)
    {
        services.AddSingleton<ILogStore>(new InMemoryLogStore(bufferCapacity));
        return services;
    }
}
