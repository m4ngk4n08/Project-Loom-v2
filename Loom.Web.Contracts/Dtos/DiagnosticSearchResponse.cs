namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Response containing search results.
/// Like Google search results - a list of matching items with relevance scores.
/// </summary>
public sealed record DiagnosticSearchResponse
{
    /// <summary>
    /// The original search query
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Number of results found
    /// </summary>
    public required int TotalResults { get; init; }

    /// <summary>
    /// How long the search took (milliseconds)
    /// </summary>
    public required double SearchTimeMs { get; init; }

    /// <summary>
    /// The actual search results
    /// </summary>
    public required SearchResult[] Results { get; init; }
}

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
    /// Similarity score (0.0 - 1.0, higher = better match)
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
}