namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// A single captured log entry.
/// </summary>
public sealed record LogEntryDto
{
    /// <summary>
    /// Formatted log message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Logger category (e.g. "MyApp.Services.OrderService").
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Log severity, as a string (e.g. "Warning"). Source-generated JSON serializes
    /// enums as numbers by default, and a numeric severity in the API is unreadable.
    /// </summary>
    public required string Level { get; init; }

    /// <summary>
    /// When the entry was logged (UTC).
    /// </summary>
    public required DateTime TimestampUtc { get; init; }

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

/// <summary>
/// Response for the resumable log tail endpoint. NextSequence is the cursor to
/// pass back on the next poll; DroppedCount reports records that were overwritten
/// before the caller could read them.
/// </summary>
public sealed record LogTailResponse
{
    public required LogEntryDto[] Entries { get; init; }

    public required long NextSequence { get; init; }

    public required int DroppedCount { get; init; }
}
