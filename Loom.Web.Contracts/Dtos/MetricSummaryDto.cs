namespace Loom.Web.Contracts.Dtos;

/// <summary>Aggregate statistics for a single metric, used by the Metrics Explorer
/// to render the left-hand list (badges, latest value, unit) without loading samples.</summary>
public sealed record MetricSummaryDto
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string Unit { get; init; }
    public required int SampleCount { get; init; }
    public required double LatestValue { get; init; }
    public required double Average { get; init; }
    public required double Min { get; init; }
    public required double Max { get; init; }
    public required double P95 { get; init; }
    public required DateTime FirstTimestampUtc { get; init; }
    public required DateTime LastTimestampUtc { get; init; }
}