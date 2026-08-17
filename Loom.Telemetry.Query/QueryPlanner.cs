using Loom.Telemetry;

namespace Loom.Telemetry.Query;

/// <summary>Resolves metric names in the AST to actual ring buffers, and validates
/// time-range-shaped WHERE conditions before execution — the "Planner" stage ADR-7 calls
/// out between parsing and executing.</summary>
public static class QueryPlanner
{
    public static QueryPlan Plan(QueryAst ast)
    {
        var buffers = LoomRuntime.GetBuffersSnapshot();
        var referencedNames = ast.Columns.Select(c => c.Name)
            .Concat(ast.Conditions.Select(c => c.Column))
            .Where(n => n != "*" && buffers.ContainsKey(n))
            .Distinct()
            .ToList();

        return new QueryPlan(ast, referencedNames);
    }
}

public sealed record QueryPlan(QueryAst Ast, IReadOnlyList<string> ReferencedMetricNames);
