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