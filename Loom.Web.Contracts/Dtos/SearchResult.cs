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
}