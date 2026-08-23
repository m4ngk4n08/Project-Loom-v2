using System.Threading.Channels;
using Loom.Storage;
using Loom.Telemetry.Alerting.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Loom.Telemetry;

namespace Loom.Telemetry.Alerting;

public sealed record AlertNotification(AlertRule Rule, MetricAggregate Observed, DateTime FiredAt);

public sealed class AlertEvaluationHostedService(
    Channel<AlertNotification> notificationChannel,
    ISilenceStore silenceStore,
    IMetricStore metricStore,
    ILogger<AlertEvaluationHostedService>? logger = null) : BackgroundService
{
    private readonly Dictionary<string, DateTime> _lastFired = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rules = LoomTelemetryOptionsAlertingExtensions.Rules;
        if (rules.Count == 0)
        {
            logger?.LogInformation("Alert evaluation started: no rules registered.");
            return;
        }

        logger?.LogInformation("Alert evaluation started: {RuleCount} rule(s) registered: {RuleNames}",
            rules.Count, string.Join(", ", rules.Select(r => r.Name)));

        var tickInterval = rules.Select(r => r.Window).DefaultIfEmpty(TimeSpan.FromMinutes(5)).Min() / 10;
        using var timer = new PeriodicTimer(tickInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTime.UtcNow;
            foreach (var rule in rules)
            {
                var aggregate = ComputeWindowAggregate(rule, now);
                if (aggregate is null)
                {
                    logger?.LogDebug("Alert rule {Rule} evaluated: no data in window.", rule.Name);
                    continue;
                }

                var fired = rule.Condition(aggregate.Value) && ShouldFire(rule, now);
                logger?.LogDebug(
                    "Alert rule {Rule} evaluated: metric={Metric} count={Count} avg={Avg:F2} max={Max:F2} p99={P99:F2} fired={Fired}",
                    rule.Name, aggregate.Value.MetricName, aggregate.Value.Count,
                    aggregate.Value.Average, aggregate.Value.Max, aggregate.Value.P99, fired);

                if (fired)
                {
                    notificationChannel.Writer.TryWrite(new AlertNotification(rule, aggregate.Value, now));
                    _lastFired[rule.Name] = now;
                    logger?.LogInformation(
                        "Alert FIRED: rule={Rule} metric={Metric} count={Count} avg={Avg:F2} max={Max:F2} p99={P99:F2} window={Window}",
                        rule.Name, aggregate.Value.MetricName, aggregate.Value.Count,
                        aggregate.Value.Average, aggregate.Value.Max, aggregate.Value.P99, rule.Window);
                }
            }
        }
    }

    private MetricAggregate? ComputeWindowAggregate(AlertRule rule, DateTime now)
    {
        var buffers = metricStore.GetBuffers();
        if (!buffers.TryGetValue(rule.MetricName, out var buffer) || buffer is null) return null;

        var cutoff = now - rule.Window;
        var windowValues = buffer.Snapshot()
            .Where(e => e.Timestamp >= cutoff)
            .Select(e => e.Value)
            .ToArray();

        if (windowValues.Length == 0) return new MetricAggregate(rule.MetricName, 0, 0, 0, 0);

        var sorted = windowValues.OrderBy(v => v).ToArray();
        var p99Index = Math.Clamp((int)Math.Ceiling(0.99 * sorted.Length) - 1, 0, sorted.Length - 1);

        return new MetricAggregate(
            rule.MetricName, windowValues.Length, windowValues.Average(), windowValues.Max(), sorted[p99Index]);
    }

    private bool ShouldFire(AlertRule rule, DateTime now)
    {
        if (silenceStore.IsSilenced(rule.Name))
            return false;

        return !_lastFired.TryGetValue(rule.Name, out var last) || now - last >= rule.Window;
    }
}
