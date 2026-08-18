namespace Loom.Telemetry.Query;

/// <summary>Resolves column references in the AST into metric names for the executor.
/// SELECT * produces an empty list — the executor iterates all store keys.</summary>
public static class QueryPlanner
{
    public static QueryPlan Plan(QueryAst ast)
    {
        var referencedNames = ast.Columns.Select(c => c.Name)
            .Concat(ast.Conditions.Select(c => c.Column))
            .Where(n => n != "*")
            .Distinct()
            .ToList();

        return new QueryPlan(ast, referencedNames);
    }
}

public sealed record QueryPlan(QueryAst Ast, IReadOnlyList<string> ReferencedMetricNames);
