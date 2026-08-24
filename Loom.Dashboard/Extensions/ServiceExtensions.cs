using Loom.Storage;
using Loom.Storage.Logging;
using Loom.Telemetry;
using Loom.Telemetry.Alerting;
using Loom.Telemetry.Exporters;
using Loom.Telemetry.Exporters.Console;
using Loom.Telemetry.Query;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
            services.AddHttpClient();
            services.Configure<WebhookAlertOptions>(options =>
                options.Url = Environment.GetEnvironmentVariable("LOOM_ALERT_WEBHOOK_URL"));
            services.AddAlertTarget<WebhookAlertTarget>();

            // Rules registered here (before AddDashboardServices' caller builds the app)
            // so AlertEvaluationHostedService sees a non-empty registry when it starts -
            // see BACKLOG.md § 6.7.
            new LoomTelemetryOptions()
                .AddAlert("HighCpuUsage", alert => alert
                    .When("cpu-usage", agg => agg.Average > 0.8)
                    .InWindow(TimeSpan.FromMinutes(1))
                    .Notify<ConsoleAlertTarget>()
                    .Notify<WebhookAlertTarget>())
                .AddAlert("HighMemoryUsage", alert => alert
                    .When("working-set", agg => agg.Average > 500)
                    .InWindow(TimeSpan.FromMinutes(5))
                    .Notify<ConsoleAlertTarget>()
                    .Notify<WebhookAlertTarget>());

            // EventPipe bridge — pulls metrics from target process into the store
            services.AddSingleton(sp =>
                new EventPipeBridge(targetPid, sp.GetRequiredService<IMetricStore>(),
                    sp.GetRequiredService<ILogger<EventPipeBridge>>()));
            services.AddHostedService(sp => sp.GetRequiredService<EventPipeBridge>());

            // Export pipeline is opt-in: both ExportCollectionHostedService and ConsoleExporter
            // log at Information on every tick, which would flood the terminal of a live
            // diagnostic session. Off by default; the status endpoint reports the truth either way.
            if (string.Equals(Environment.GetEnvironmentVariable("LOOM_DASHBOARD_EXPORT"),
                    "console", StringComparison.OrdinalIgnoreCase))
            {
                services.AddLoomExporting(opts =>
                {
                    opts.CollectionInterval = TimeSpan.FromSeconds(30);
                    opts.ChannelCapacity = 64;
                });
                services.AddLoomExporter<ConsoleExporter>();
            }

            // Registered unconditionally so /api/exporters/status can always resolve it.
            // TryAdd: AddLoomExporting already registers this when the pipeline is on.
            services.TryAddSingleton<ExportStatusTracker>();

            return services;
        }
    }
}
