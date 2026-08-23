using Loom.Storage;
using Loom.Telemetry.Alerting;
using Loom.Telemetry.Query;

namespace Loom.Dashboard.Extensions
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddDashboardServices(this IServiceCollection services, int targetPid)
        {
            // Centralized metric storage
            services.AddLoomStorage();
            services.AddSingleton<IQueryExecutor, QueryExecutor>();

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
