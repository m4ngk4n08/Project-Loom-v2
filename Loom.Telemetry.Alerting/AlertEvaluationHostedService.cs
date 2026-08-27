using System.Threading.Channels;
using Loom.Storage;
using Loom.Telemetry.Alerting.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Loom.Telemetry;

namespace Loom.Telemetry.Alerting;

/// <summary>FiredAt keeps ONE meaning in both states: when the alert STARTED firing.
/// For a Resolved notification that is the original firing time, not the resolution
/// time — ResolvedAt carries that.</summary>
public sealed record AlertNotification(AlertRule Rule, MetricAggregate Observed, DateTime FiredAt)
{
    public AlertState State { get; init; } = AlertState.Firing;

    /// <summary>Set only when State == Resolved.</summary>
    public DateTime? ResolvedAt { get; init; }

    /// <summary>Set only when State == Resolved.</summary>
    public AlertResolutionReason? ResolutionReason { get; init; }
}

public sealed class AlertEvaluationHostedService(
    Channel<AlertNotification> notificationChannel,
    ISilenceStore silenceStore,
    IMetricStore metricStore,
    ILogger<AlertEvaluationHostedService>? logger = null) : BackgroundService
{
    // rule name -> DateTime the alert started firing
    private readonly Dictionary<string, DateTime> _activeAlerts = [];
    // rule name -> DateTime of the last Firing notification (the re-notify cooldown)
    private readonly Dictionary<string, DateTime> _lastNotified = [];
    // rule name -> when we last computed a real aggregate, and what it was. The
    // aggregate is kept so an expiry notification can report the last thing actually
    // observed rather than a fabricated zero reading.
    private readonly Dictionary<string, (DateTime SeenAt, MetricAggregate Aggregate)> _lastData = [];

    // With no rules the loop body is a no-op, so a short idle tick costs a timer
    // wakeup and nothing else. It bounds how long a rule registered after startup
    // waits for its first evaluation - which is the whole point of § 6.7.
    internal static readonly TimeSpan IdleTickInterval = TimeSpan.FromMilliseconds(250);

    internal static TimeSpan ComputeTickInterval(IReadOnlyList<AlertRule> rules) =>
        rules.Count == 0
            ? IdleTickInterval
            : rules.Select(r => r.Window).Min() / 10;

    // Three windows of silence. Scales with the rule instead of being one constant
    // that is too eager for a 5-minute rule and too slow for a 1-minute one.
    internal static TimeSpan ResolveNoDataGrace(AlertRule rule) =>
        rule.NoDataGrace ?? rule.Window * 3;

    internal static bool HasExpired(DateTime now, DateTime lastDataSeen, TimeSpan grace) =>
        now - lastDataSeen >= grace;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The registry is re-read every tick rather than snapshotted once: returning
        // early on an empty registry meant no rule registered later was ever
        // evaluated, for the life of the process (BACKLOG.md § 6.7).
        var tickInterval = ComputeTickInterval(LoomTelemetryOptionsAlertingExtensions.Rules);
        using var timer = new PeriodicTimer(tickInterval);

        logger?.LogInformation("Alert evaluation started with a {Tick} tick.", tickInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // Copy before iterating: Rules is a plain List and enumerating it while
            // something adds a rule throws.
            var rules = LoomTelemetryOptionsAlertingExtensions.Rules.ToList();

            var desiredTick = ComputeTickInterval(rules);
            if (desiredTick != tickInterval)
            {
                tickInterval = desiredTick;
                timer.Period = desiredTick;
                logger?.LogInformation("Alert evaluation tick adjusted to {Tick}.", desiredTick);
            }

            var now = DateTime.UtcNow;
            foreach (var rule in rules)
            {
                var aggregate = ComputeWindowAggregate(rule, now);
                if (aggregate is null)
                {
                    // An active alert whose metric has been silent past its grace period is
                    // resolved as NoData. A rule that has never seen data cannot expire: there
                    // is no alert open to close.
                    if (_activeAlerts.TryGetValue(rule.Name, out var noDataActiveSince) &&
                        _lastData.TryGetValue(rule.Name, out var last) &&
                        HasExpired(now, last.SeenAt, ResolveNoDataGrace(rule)))
                    {
                        _activeAlerts.Remove(rule.Name);
                        _lastNotified.Remove(rule.Name);
                        notificationChannel.Writer.TryWrite(
                            new AlertNotification(rule, last.Aggregate, noDataActiveSince)
                            {
                                State = AlertState.Resolved,
                                ResolvedAt = now,
                                ResolutionReason = AlertResolutionReason.NoData
                            });
                        logger?.LogWarning(
                            "Alert RESOLVED (no data): rule={Rule} metric={Metric} firedAt={FiredAt:O} lastData={LastData:O} grace={Grace}",
                            rule.Name, rule.MetricName, noDataActiveSince, last.SeenAt, ResolveNoDataGrace(rule));
                        continue;
                    }

                    logger?.LogDebug("Alert rule {Rule} evaluated: no data in window.", rule.Name);
                    continue;
                }

                _lastData[rule.Name] = (now, aggregate.Value);

                var conditionMet = rule.Condition(aggregate.Value);
                var isActive = _activeAlerts.TryGetValue(rule.Name, out var activeSince);
                logger?.LogDebug(
                    "Alert rule {Rule} evaluated: metric={Metric} count={Count} avg={Avg:F2} max={Max:F2} p99={P99:F2} conditionMet={ConditionMet} active={Active}",
                    rule.Name, aggregate.Value.MetricName, aggregate.Value.Count,
                    aggregate.Value.Average, aggregate.Value.Max, aggregate.Value.P99, conditionMet, isActive);

                if (conditionMet && !isActive)
                {
                    if (silenceStore.IsSilenced(rule.Name))
                        continue;

                    _activeAlerts[rule.Name] = now;
                    _lastNotified[rule.Name] = now;
                    notificationChannel.Writer.TryWrite(new AlertNotification(rule, aggregate.Value, now));
                    logger?.LogInformation(
                        "Alert FIRED: rule={Rule} metric={Metric} count={Count} avg={Avg:F2} max={Max:F2} p99={P99:F2} window={Window}",
                        rule.Name, aggregate.Value.MetricName, aggregate.Value.Count,
                        aggregate.Value.Average, aggregate.Value.Max, aggregate.Value.P99, rule.Window);
                }
                else if (conditionMet && isActive)
                {
                    // Re-notify only after the cooldown — this preserves today's behavior.
                    // FiredAt on the re-notification stays the ORIGINAL start time.
                    if (now - _lastNotified[rule.Name] >= rule.Window)
                    {
                        _lastNotified[rule.Name] = now;
                        notificationChannel.Writer.TryWrite(new AlertNotification(rule, aggregate.Value, activeSince));
                        logger?.LogInformation(
                            "Alert RE-FIRED: rule={Rule} metric={Metric} count={Count} avg={Avg:F2} max={Max:F2} p99={P99:F2} window={Window}",
                            rule.Name, aggregate.Value.MetricName, aggregate.Value.Count,
                            aggregate.Value.Average, aggregate.Value.Max, aggregate.Value.P99, rule.Window);
                    }
                }
                else if (!conditionMet && isActive)
                {
                    // Resolution ignores the cooldown and silence: an alert that already
                    // fired (the operator saw it open) always gets its "OK" the moment the
                    // condition clears. Only the initial fire is silence-gated.
                    _activeAlerts.Remove(rule.Name);
                    _lastNotified.Remove(rule.Name);
                    notificationChannel.Writer.TryWrite(new AlertNotification(rule, aggregate.Value, activeSince)
                    {
                        State = AlertState.Resolved,
                        ResolvedAt = now,
                        ResolutionReason = AlertResolutionReason.ConditionCleared
                    });
                    logger?.LogInformation(
                        "Alert RESOLVED: rule={Rule} metric={Metric} count={Count} avg={Avg:F2} max={Max:F2} p99={P99:F2} firedAt={FiredAt:O}",
                        rule.Name, aggregate.Value.MetricName, aggregate.Value.Count,
                        aggregate.Value.Average, aggregate.Value.Max, aggregate.Value.P99, activeSince);
                }
                // !conditionMet && !isActive -> nothing.
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

        // An empty window is "no data", not a zero reading — a condition like
        // `agg => agg.Average < 5` must not fire spuriously, and a `>` condition must
        // not auto-resolve an active alert merely because data stopped arriving.
        if (windowValues.Length == 0) return null;

        var sorted = windowValues.OrderBy(v => v).ToArray();
        var p99Index = Math.Clamp((int)Math.Ceiling(0.99 * sorted.Length) - 1, 0, sorted.Length - 1);

        return new MetricAggregate(
            rule.MetricName, windowValues.Length, windowValues.Average(), windowValues.Max(), sorted[p99Index]);
    }
}
