namespace Loom.Telemetry.Exporters;

/// <summary>
/// A batch of metrics collected at a specific time, grouped by metric name.
/// </summary>
public sealed class MetricBatch
{
    /// <summary>
    /// UTC timestamp when this batch was collected.
    /// </summary>
    public required DateTime CollectedAtUtc { get; init; }

    /// <summary>
    /// Metric entries grouped by metric name.
    /// </summary>
    public required MetricBatchEntry[] Entries { get; init; }
}

/// <summary>
/// A single metric's records within a batch.
/// </summary>
public sealed class MetricBatchEntry
{
    /// <summary>
    /// The metric name.
    /// </summary>
    public required string MetricName { get; init; }

    /// <summary>
    /// The type of metric (Counter, Gauge, Histogram, MethodExecution).
    /// </summary>
    public required MetricType Type { get; init; }

    /// <summary>
    /// All records for this metric since the last collection.
    /// </summary>
    public required MetricRecord[] Records { get; init; }
}
