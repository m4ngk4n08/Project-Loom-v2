namespace Loom.Telemetry;

/// <summary>
/// A cumulative counter total for one (metric name, tag set) series, maintained by
/// the store independently of its ring buffer - monotonic across buffer wraps.
/// </summary>
public readonly record struct CounterTotal(string MetricName, MetricTag[] Tags, double Total);
