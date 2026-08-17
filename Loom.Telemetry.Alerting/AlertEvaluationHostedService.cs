using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Loom.Telemetry;

namespace Loom.Telemetry.Alerting;

public sealed record AlertNotification(AlertRule Rule, MetricAggregate Observed, DateTime FiredAt);

public sealed class AlertEvaluationHostedService(
    Channel<AlertNotification> notificationChannel,
    ISilenceStore silenceStore) : BackgroundService
{
    private readonly Dictionary<string, DateTime> _lastFired = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rules = LoomTelemetryOptionsAlertingExtensions.Rules;
        if (rules.Count == 0) return;

        // Tick at (smallest window / 10) per ADR-8, so even the tightest window gets
        // several evaluation opportunities within its own duration.
        var tickInterval = rules.Select(r => r.Window).DefaultIfEmpty(TimeSpan.FromMinutes(5)).Min() / 10;
        using var timer = new PeriodicTimer(tickInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTime.UtcNow;
            foreach (var rule in rules)
            {
                var aggregate = ComputeWindowAggregate(rule, now);
                if (aggregate is null) continue;

                if (rule.Condition(aggregate.Value) && ShouldFire(rule, now))
                {
                    // Fire-and-forget: TryWrite never blocks the evaluation loop.
                    // On a full bounded channel, the notification is dropped rather than
                    // stalling evaluation of the remaining rules — see Step 11.5 for the
                    // channel's bounded/drop-oldest configuration.
                    notificationChannel.Writer.TryWrite(new AlertNotification(rule, aggregate.Value, now));
                    _lastFired[rule.Name] = now;
                }
            }
        }
    }

    private static MetricAggregate? ComputeWindowAggregate(AlertRule rule, DateTime now)
    {
        var buffers = LoomRuntime.GetBuffersSnapshot();
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
