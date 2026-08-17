using Loom.Web.Api.Interfaces;
using Loom.Web.Api.Services;
using Loom.Telemetry.Query;
using Loom.Telemetry.Alerting;

namespace Loom.Web.Api.Extensions
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddServices(this IServiceCollection services)
        {
            services.AddSingleton<IMetricsService, MetricsService>();
            services.AddSingleton<IQueryExecutor, QueryExecutor>();

            // Alerting services
            services.AddLoomAlerting();
            services.AddAlertTarget<ConsoleAlertTarget>();

            return services;
        }
    }
}
