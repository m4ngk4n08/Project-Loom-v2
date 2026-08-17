using System.Diagnostics;
using Loom.Telemetry;
using Loom.Web.Contracts.Dtos;

namespace Loom.Telemetry.Query;

public interface IQueryExecutor
{
    ValueTask<QueryResponse> ExecuteAsync(string queryText, CancellationToken ct);
}

public sealed class QueryExecutor : IQueryExecutor
{
    public ValueTask<QueryResponse> ExecuteAsync(string queryText, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var ast = QueryParser.Parse(queryText);
        var plan = QueryPlanner.Plan(ast);

        var rows = Execute(plan);
        var elapsed = Stopwatch.GetElapsedTime(started);

        var response = new QueryResponse
        {
            Columns = ast.Columns.Select(c => c.Aggregate == AggregateFunction.None ? c.Name : $"{c.Aggregate}({c.Name})".ToUpperInvariant()).ToList(),
            Rows = rows,
            ExecutionTimeMs = elapsed.TotalMilliseconds
        };
        return ValueTask.FromResult(response);
    }

    private static List<QueryResultRow> Execute(QueryPlan plan)
    {
        // Executor stage: switch on closed AggregateFunction enum, not a visitor.Visit(dynamic).
        var buffers = LoomRuntime.GetBuffersSnapshot();
        var rows = new List<QueryResultRow>();

        foreach (var metricName in plan.ReferencedMetricNames.DefaultIfEmpty(buffers.Keys.FirstOrDefault() ?? ""))
        {
            if (!buffers.TryGetValue(metricName, out var buffer)) continue;
            if (!MatchesConditions(metricName, plan.Ast.Conditions)) continue;

            var entries = buffer.Snapshot();
            if (entries.Length == 0) continue;

            var values = plan.Ast.Columns.Select(col => col.Aggregate switch
            {
                AggregateFunction.Avg => new QueryValue { Number = entries.Average(e => e.Value) },
                AggregateFunction.Count => new QueryValue { Number = entries.Length },
                AggregateFunction.Max => new QueryValue { Number = entries.Max(e => e.Value) },
                AggregateFunction.Min => new QueryValue { Number = entries.Min(e => e.Value) },
                AggregateFunction.P99 => new QueryValue { Number = Percentile(entries.Select(e => e.Value), 0.99) },
                _ => new QueryValue { Text = metricName }
            }).ToList();

            rows.Add(new QueryResultRow { Values = values });
        }

        if (plan.Ast.OrderByColumn is not null)
        {
            rows = plan.Ast.OrderDescending
                ? rows.OrderByDescending(RowSortKey).ToList()
                : rows.OrderBy(RowSortKey).ToList();
        }

        return plan.Ast.Limit is { } limit ? rows.Take(limit).ToList() : rows;

        static double RowSortKey(QueryResultRow row) => row.Values[^1].Number ?? 0;
    }

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return 0;
        var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static bool MatchesConditions(string metricName, IReadOnlyList<WhereCondition> conditions) =>
        conditions.Count == 0 || conditions.All(c => c.Column != "method" || metricName.Contains(c.Value, StringComparison.OrdinalIgnoreCase));
}
