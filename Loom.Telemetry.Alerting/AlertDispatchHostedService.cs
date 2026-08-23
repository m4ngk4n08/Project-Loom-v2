using System.Threading.Channels;
using Loom.Telemetry.Alerting.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Loom.Telemetry.Alerting;

/// <summary>Consumes the Channel<AlertNotification> that AlertEvaluationHostedService
/// writes to — this decoupling is ADR-8's "fire-and-forget via Channel<T> to avoid
/// blocking evaluation" and the Risk Register's mitigation for "Alert evaluation
/// blocking hot path."</summary>
public sealed class AlertDispatchHostedService(
    Channel<AlertNotification> channel,
    IEnumerable<IAlertTarget> allTargets,
    ILogger<AlertDispatchHostedService>? logger = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var notification in channel.Reader.ReadAllAsync(stoppingToken))
        {
            var targets = allTargets.Where(t => notification.Rule.TargetTypes.Contains(t.GetType()));
            foreach (var target in targets)
            {
                try
                {
                    await target.NotifyAsync(notification, stoppingToken);
                    logger?.LogInformation(
                        "Alert notification delivered: rule={Rule} metric={Metric} firedAt={FiredAt:O} target={Target}",
                        notification.Rule.Name, notification.Rule.MetricName, notification.FiredAt, target.GetType().Name);
                }
                catch (Exception ex) when (stoppingToken.IsCancellationRequested is false)
                {
                    // A failed notification must not crash the dispatcher or block other targets/alerts.
                    logger?.LogError(ex,
                        "Alert notification FAILED: rule={Rule} metric={Metric} target={Target}",
                        notification.Rule.Name, notification.Rule.MetricName, target.GetType().Name);
                }
            }
        }
    }
}
