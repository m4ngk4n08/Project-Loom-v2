using Loom.Web.Api.Interfaces;
using Loom.Web.Api.Services;
using Loom.Storage;
using Loom.Telemetry.Query;
using Loom.Telemetry.Alerting;
using Loom.Telemetry.Exporters;
using Loom.Telemetry.Exporters.Console;

namespace Loom.Web.Api.Extensions
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddServices(this IServiceCollection services)
        {
            // Centralized metric storage
            services.AddLoomStorage();

            services.AddSingleton<IMetricsService, MetricsService>();
            services.AddSingleton<IQueryExecutor, QueryExecutor>();

            // Alerting services
            services.AddLoomAlerting();
            services.AddAlertTarget<ConsoleAlertTarget>();

            // Exporter services
            services.AddLoomExporting(opts =>
            {
                opts.CollectionInterval = TimeSpan.FromSeconds(10);
                opts.ChannelCapacity = 64;
            });
            services.AddLoomExporter<ConsoleExporter>();

            return services;
        }
    }
}
