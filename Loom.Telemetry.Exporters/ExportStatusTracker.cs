using System.Collections.Concurrent;

namespace Loom.Telemetry.Exporters;

/// <summary>
/// Thread-safe tracker for exporter health status.
/// Records success/failure for each exporter to enable monitoring.
/// </summary>
public sealed class ExportStatusTracker
{
    private readonly ConcurrentDictionary<string, ExporterStatus> _statuses = new();

    /// <summary>
    /// Record a successful export for the named exporter.
    /// </summary>
    public void RecordSuccess(string exporterName)
    {
        _statuses.AddOrUpdate(
            exporterName,
            _ => new ExporterStatus
            {
                Name = exporterName,
                IsHealthy = true,
                LastSuccessUtc = DateTime.UtcNow,
                TotalExports = 1,
                TotalFailures = 0
            },
            (_, existing) => existing with
            {
                IsHealthy = true,
                LastSuccessUtc = DateTime.UtcNow,
                TotalExports = existing.TotalExports + 1
            });
    }

    /// <summary>
    /// Record a failed export for the named exporter.
    /// </summary>
    public void RecordFailure(string exporterName, string error)
    {
        _statuses.AddOrUpdate(
            exporterName,
            _ => new ExporterStatus
            {
                Name = exporterName,
                IsHealthy = false,
                LastFailureUtc = DateTime.UtcNow,
                LastError = error,
                TotalExports = 0,
                TotalFailures = 1
            },
            (_, existing) => existing with
            {
                IsHealthy = false,
                LastFailureUtc = DateTime.UtcNow,
                LastError = error,
                TotalFailures = existing.TotalFailures + 1
            });
    }

    /// <summary>
    /// Get all exporter statuses.
    /// </summary>
    public IReadOnlyDictionary<string, ExporterStatus> GetStatuses() => _statuses;
}

/// <summary>
/// Health status snapshot for a single exporter.
/// </summary>
public sealed record ExporterStatus
{
    public required string Name { get; init; }
    public required bool IsHealthy { get; init; }
    public DateTime? LastSuccessUtc { get; init; }
    public DateTime? LastFailureUtc { get; init; }
    public string? LastError { get; init; }
    public long TotalExports { get; init; }
    public long TotalFailures { get; init; }
}
