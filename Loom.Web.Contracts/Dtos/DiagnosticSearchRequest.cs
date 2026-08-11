namespace Loom.Web.Contracts.Dtos;


/// <summary>
/// Request for vector search over diagnostic telemetry.
/// </summary>
public sealed record DiagnosticSearchRequest
{
    /// <summary>
    /// The search query text
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Maximum number of results to return (default: 10)
    /// </summary>
    public int MaxResults { get; init; } = 10;

    /// <summary>
    /// Minimum similarity threshold (0.0 - 1.0) for results to be included (default: 0.7)
    /// </summary>
    public double MinSimilarity { get; init; } = 0.7;
}
