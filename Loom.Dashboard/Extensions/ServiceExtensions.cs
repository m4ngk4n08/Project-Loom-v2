using Loom.Storage;
using Loom.Storage.Logging;
using Loom.Telemetry.Alerting;
using Loom.Telemetry.Query;
using Microsoft.Extensions.Logging;

namespace Loom.Dashboard.Extensions
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddDashboardServices(this IServiceCollection services, int targetPid)
        {
            // Centralized metric storage
            services.AddLoomStorage();
            services.AddSingleton<IQueryExecutor, QueryExecutor>();

            // Centralized log storage, captured via the standard ILoggerProvider path -
            // the LoggerFactory picks this up automatically. Program.cs sets the
            // minimum level to Information, so the store only ever sees Information
            // and above. No cycle: InMemoryLogStore takes no ILogger of its own, and
            // LoomLoggerProvider's "Loom." category prefix guard keeps the dashboard's
            // own logging (EventPipeBridge, query execution, this session summary, etc.)
            // out of the store, which is what stops a log-reading endpoint from
            // generating more logs about reading logs.
            services.AddLoomLogStorage();
            services.AddSingleton<ILoggerProvider>(sp =>
                new LoomLoggerProvider(sp.GetRequiredService<ILogStore>()));

            // Alerting services
            services.AddLoomAlerting();
            services.AddAlertTarget<ConsoleAlertTarget>();

            // EventPipe bridge — pulls metrics from target process into the store
            services.AddSingleton(sp =>
                new EventPipeBridge(targetPid, sp.GetRequiredService<IMetricStore>(),
                    sp.GetRequiredService<ILogger<EventPipeBridge>>()));
            services.AddHostedService(sp => sp.GetRequiredService<EventPipeBridge>());

            return services;
        }
    }
}
