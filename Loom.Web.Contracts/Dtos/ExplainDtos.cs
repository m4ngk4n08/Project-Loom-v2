namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Request to explain a single log entry.
/// </summary>
public sealed record ExplainRequest
{
    /// <summary>
    /// Message template with {Placeholders} intact.
    /// </summary>
    public required string Template { get; init; }

    /// <summary>
    /// Structured arguments as a JSON object string.
    /// </summary>
    /// <remarks>The server extracts argument NAMES from this and never reads the
    /// values, so even a client that puts secrets in the values cannot cause them to
    /// be transmitted. The outgoing payload comes from ExplainPayloadBuilder, not from
    /// trusting the caller.</remarks>
    public string? ArgumentsJson { get; init; }

    /// <summary>
    /// Logger category (e.g. "MyApp.Services.OrderService").
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Log severity, as a string (e.g. "Warning").
    /// </summary>
    public string? Level { get; init; }

    /// <summary>
    /// Exception type name, only set when the log entry carries an exception.
    /// </summary>
    public string? ExceptionType { get; init; }
}

/// <summary>
/// Response for an explain request.
/// </summary>
public sealed record ExplainResponse
{
    /// <summary>
    /// The model's explanation of the log event.
    /// </summary>
    public required string Explanation { get; init; }

    /// <summary>
    /// Which model produced the explanation.
    /// </summary>
    public required string ModelUsed { get; init; }

    /// <summary>
    /// The exact text transmitted to the model.
    /// </summary>
    /// <remarks>Returned so the UI can show it - a redaction boundary you cannot see
    /// is one you cannot audit.</remarks>
    public required string SentText { get; init; }

    /// <summary>
    /// Input tokens consumed by the request.
    /// </summary>
    public required int InputTokens { get; init; }

    /// <summary>
    /// Output tokens consumed by the response.
    /// </summary>
    public required int OutputTokens { get; init; }
}
