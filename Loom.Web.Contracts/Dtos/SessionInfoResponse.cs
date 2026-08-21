namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Describes the process being observed by the dashboard.
/// The dashboard is a viewer for ONE target process — this tells the user
/// exactly which process the metrics belong to.
/// </summary>
public sealed record SessionInfoResponse
{
    /// <summary>
    /// OS process ID of the observed application.
    /// </summary>
    public required int TargetProcessId { get; init; }

    /// <summary>
    /// Process name (e.g. "SampleMonitoredApp") of the observed application.
    /// </summary>
    public required string TargetProcessName { get; init; }

    /// <summary>
    /// When the dashboard session started (UTC).
    /// </summary>
    public required DateTime StartedAtUtc { get; init; }

    /// <summary>
    /// Seconds since the dashboard session started.
    /// </summary>
    public required long UptimeSeconds { get; init; }

    /// <summary>
    /// Number of distinct metric names currently collected from the target.
    /// </summary>
    public required int MetricCount { get; init; }
}