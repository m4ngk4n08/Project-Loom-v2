namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Request for vector search over diagnostic telemetry.
/// Think of this like typing a search query into Google.
/// </summary>
public sealed record DiagnosticSearchRequest
{
    /// <summary>
    /// The search query text
    /// Example: "thread blocked on database"
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Maximum number of results to return (default: 10)
    /// </summary>
    public int MaxResults { get; init; } = 10;

    /// <summary>
    /// Minimum similarity threshold (0.0 - 1.0)
    /// Higher = more strict matching
    /// </summary>
    public double MinSimilarity { get; init; } = 0.7;
}