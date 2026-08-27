namespace Loom.Telemetry.Alerting;

/// <summary>What an aggregate-condition delegate receives — pre-computed count/avg/max/p99
/// over the alert's own sliding window, so the condition itself stays a cheap comparison.</summary>
public readonly record struct MetricAggregate(string MetricName, long Count, double Average, double Max, double P99);

public class AlertRule(string name, string metricName, TimeSpan window)
{
    public string Name { get; } = name;
    public string MetricName { get; } = metricName;
    public TimeSpan Window { get; } = window;

    /// <summary>A plain closure, not an expression tree — see the AOT-compatibility note above.</summary>
    public Func<MetricAggregate, bool> Condition { get; set; } = static _ => false;

    /// <summary>How long a metric may produce no samples before an active alert on
    /// it is resolved as NoData. Null means Window * 3.</summary>
    public TimeSpan? NoDataGrace { get; set; }

    public List<Type> TargetTypes { get; } = [];
}
