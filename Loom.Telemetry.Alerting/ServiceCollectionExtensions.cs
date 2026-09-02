using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Loom.Telemetry.Alerting.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Loom.Telemetry.Alerting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLoomAlerting(
        this IServiceCollection services,
        Action<IAlertRuleRegistry>? configure = null)
    {
        // Bounded, drop-oldest: a burst of simultaneous alerts shouldn't grow memory
        // unboundedly if the dispatcher briefly falls behind (Risk Register: "Exporter
        // backpressure causing memory growth" — same channel-backpressure pattern, applied
        // here to alert notifications instead of exported metric batches).
        var channel = Channel.CreateBounded<AlertNotification>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
        services.AddSingleton(channel);
        services.AddSingleton<ISilenceStore, InMemorySilenceStore>();

        var registry = new AlertRuleRegistry();
        configure?.Invoke(registry);
        services.AddSingleton<IAlertRuleRegistry>(registry);

        services.AddHostedService<AlertEvaluationHostedService>();
        services.AddHostedService<AlertDispatchHostedService>();
        return services;
    }

    public static IServiceCollection AddAlertTarget<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this IServiceCollection services)
        where T : class, IAlertTarget
    {
        services.AddSingleton<IAlertTarget, T>();
        return services;
    }
}
