namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Request to ingest new telemetry data.
/// This is how external systems send diagnostic events to Loom.
/// </summary>
public sealed record TelemetryIngestRequest
{
    /// <summary>
    /// Type of telemetry event
    /// Examples: "gc_start", "thread_blocked", "cpu_spike"
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// When the event occurred (UTC)
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Source application or service name
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Event severity: "Info", "Warning", "Error", "Critical"
    /// </summary>
    public required string Severity { get; init; }

    /// <summary>
    /// Event message or description
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Optional additional metadata (key-value pairs)
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}