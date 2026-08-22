namespace Loom.Web.Contracts.Dtos;

public sealed record QueryRequest { public required string Query { get; init; } }

public sealed record QueryResponse
{
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<QueryResultRow> Rows { get; init; }
    public required double ExecutionTimeMs { get; init; }
}

public sealed record QueryResultRow { public required IReadOnlyList<QueryValue> Values { get; init; } }

/// <summary>Closed union over a query cell's value kinds — avoids `object`, which would
/// need reflection to serialize (same pattern as MetricUpdate in Phase 1).</summary>
public sealed record QueryValue
{
    public string? Text { get; init; }
    public double? Number { get; init; }
    public DateTime? Timestamp { get; init; }
}

public sealed record QueryColumn { public required string Name { get; init; } public required string Kind { get; init; } }

/// <summary>Minimal-API error body for query endpoints. Not `Microsoft.AspNetCore.Mvc.ProblemDetails` -
/// `Results.Problem(...)` serializes that type even from Minimal APIs, which would pull MVC's
/// JSON metadata into this AOT contracts assembly (see ADR-1 / JsonContext.cs).</summary>
public sealed record QueryErrorResponse { public required string Error { get; init; } }
