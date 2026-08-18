using Microsoft.Extensions.DependencyInjection;

namespace Loom.Storage;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLoomStorage(this IServiceCollection services, int bufferCapacity = 8192)
    {
        services.AddSingleton<IMetricStore>(new InMemoryMetricStore(bufferCapacity));
        return services;
    }
}
