namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// A single search result.
/// </summary>
public sealed record SearchResult
{
    /// <summary>
    /// The diagnostic message or telemetry event
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// BM25 relevance score. Unbounded, higher is better, comparable only within one
    /// result set.
    /// </summary>
    public required double Score { get; init; }

    /// <summary>
    /// When this diagnostic event occurred
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Source of the diagnostic (e.g., "CPU", "Memory", "Thread")
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Log severity, as a string (e.g. "Warning"). Source-generated JSON serializes
    /// enums as numbers by default, and a numeric severity in the API is unreadable.
    /// </summary>
    public required string Level { get; init; }

    /// <summary>
    /// Event id, if the caller supplied one (0 otherwise).
    /// </summary>
    public required int EventId { get; init; }

    /// <summary>
    /// Exception type name, only set when the log entry carries an exception.
    /// </summary>
    public string? ExceptionType { get; init; }

    /// <summary>
    /// Exception message, only set when the log entry carries an exception.
    /// </summary>
    public string? ExceptionMessage { get; init; }

    /// <summary>
    /// Message template with {Placeholders} intact, before argument
    /// substitution. Null when the source supplied none.
    /// </summary>
    public string? Template { get; init; }

    /// <summary>
    /// Structured arguments as a JSON object string, with {OriginalFormat}
    /// removed. Null when there are no arguments.
    /// </summary>
    /// <remarks>Carried as a string, not raw JSON, so whatever came off the
    /// wire is preserved verbatim rather than dropped, so this can hold text
    /// that is not valid JSON; it must stay an escaped string property or one
    /// bad payload corrupts the whole response.</remarks>
    public string? ArgumentsJson { get; init; }

    /// <summary>
    /// W3C trace id as 32 lowercase hex chars. Null when absent.
    /// </summary>
    public string? TraceId { get; init; }

    /// <summary>
    /// W3C span id as 16 lowercase hex chars. Null when absent.
    /// </summary>
    public string? SpanId { get; init; }
}