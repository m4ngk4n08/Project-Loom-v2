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
