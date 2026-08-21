namespace Loom.Telemetry.Query;

/// <summary>Resolves column references in the AST into metric names for the executor.
/// SELECT * and the pseudo-column `method` produce an empty list — the executor
/// iterates all store keys in those cases.</summary>
public static class QueryPlanner
{
    public static QueryPlan Plan(QueryAst ast)
    {
        var referencedNames = ast.Columns
            .Where(c => c.Aggregate == AggregateFunction.None)
            .Select(c => c.Name)
            .Where(n => n != "*" && !n.Equals("method", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();

        return new QueryPlan(ast, referencedNames);
    }
}

public sealed record QueryPlan(QueryAst Ast, IReadOnlyList<string> ReferencedMetricNames);
