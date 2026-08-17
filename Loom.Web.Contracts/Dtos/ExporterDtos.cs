namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Health status of a metric exporter.
/// </summary>
public sealed record ExporterStatusDto
{
    public required string Name { get; init; }
    public required bool IsHealthy { get; init; }
    public DateTime? LastSuccessUtc { get; init; }
    public DateTime? LastFailureUtc { get; init; }
    public string? LastError { get; init; }
    public long TotalExports { get; init; }
    public long TotalFailures { get; init; }
}

/// <summary>
/// Grafana Cloud metric push payload.
/// </summary>
public sealed record GrafanaMetricPayload
{
    public required GrafanaMetricSeries[] Series { get; init; }
}

/// <summary>
/// A single metric series in Grafana format.
/// </summary>
public sealed record GrafanaMetricSeries
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required GrafanaDataPoint[] DataPoints { get; init; }
    public Dictionary<string, string>? Labels { get; init; }
}

/// <summary>
/// A single data point in Grafana format.
/// </summary>
public sealed record GrafanaDataPoint
{
    public required double Value { get; init; }
    public required long TimestampMs { get; init; }
}
