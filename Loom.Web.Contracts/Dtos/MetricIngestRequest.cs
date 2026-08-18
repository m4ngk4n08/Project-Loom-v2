namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Batch request for ingesting raw metrics into the ring buffer.
/// Simpler than TelemetryIngestRequest - directly maps to LoomMetrics API.
/// </summary>
public sealed record MetricIngestRequest
{
    /// <summary>
    /// Array of metrics to ingest.
    /// </summary>
    public required MetricIngestDto[] Metrics { get; init; }
}

/// <summary>
/// A single metric to ingest.
/// </summary>
public sealed record MetricIngestDto
{
    /// <summary>
    /// Metric name (e.g., "http.request.duration", "cache.hits").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Metric type: "Counter", "Gauge", or "Histogram".
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Numeric value.
    /// </summary>
    public required double Value { get; init; }

    /// <summary>
    /// Timestamp when this metric was recorded (optional, defaults to now).
    /// </summary>
    public DateTime? Timestamp { get; init; }

    /// <summary>
    /// Optional tags/dimensions (key-value pairs).
    /// </summary>
    public Dictionary<string, string>? Tags { get; init; }
}
