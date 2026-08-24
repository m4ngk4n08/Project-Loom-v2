using Loom.Web.Api.Interfaces;
using Loom.Web.Api.Services;
using Loom.Storage;
using Loom.Telemetry;
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
            services.AddHttpClient();
            services.Configure<WebhookAlertOptions>(options =>
                options.Url = Environment.GetEnvironmentVariable("LOOM_ALERT_WEBHOOK_URL"));
            services.AddAlertTarget<WebhookAlertTarget>();

            // Rules registered here (before Program.cs builds the app) so
            // AlertEvaluationHostedService sees a non-empty registry when it starts -
            // see BACKLOG.md § 6.7. These metric names are what a self-monitoring
            // caller would push via POST /api/metrics/ingest; Web.Api records nothing
            // about itself automatically.
            new LoomTelemetryOptions()
                .AddAlert("HighIngestErrorRate", alert => alert
                    .When("http.requests.errors", agg => agg.Count > 10)
                    .InWindow(TimeSpan.FromMinutes(5))
                    .Notify<ConsoleAlertTarget>()
                    .Notify<WebhookAlertTarget>())
                .AddAlert("HighIngestLatency", alert => alert
                    .When("http.request.duration.ms", agg => agg.P99 > 500)
                    .InWindow(TimeSpan.FromMinutes(5))
                    .Notify<ConsoleAlertTarget>()
                    .Notify<WebhookAlertTarget>());

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
