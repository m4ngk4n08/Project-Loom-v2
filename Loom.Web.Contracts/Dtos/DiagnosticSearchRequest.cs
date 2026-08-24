namespace Loom.Web.Contracts.Dtos;


/// <summary>
/// Request for BM25 lexical search over captured logs.
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
    /// Minimum BM25 relevance score. Scores are unbounded and corpus-relative, not a
    /// 0-1 similarity - leave at 0.0 unless tuning against a known corpus.
    /// </summary>
    public double MinScore { get; init; } = 0.0;
}
