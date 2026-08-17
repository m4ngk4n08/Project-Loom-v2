namespace Loom.Telemetry.Query;

/// <summary>Closed set of AST node kinds — the executor's switch over this enum is
/// exhaustive and compiler-checked, unlike a reflection-based visitor pattern.</summary>
public enum AggregateFunction { None, Avg, Count, Max, Min, P99 }

public sealed record SelectColumn(string Name, AggregateFunction Aggregate);
public sealed record WhereCondition(string Column, string Operator, string Value);

public sealed record QueryAst(
    IReadOnlyList<SelectColumn> Columns,
    IReadOnlyList<WhereCondition> Conditions,
    string? GroupByColumn,
    string? OrderByColumn,
    bool OrderDescending,
    int? Limit);
