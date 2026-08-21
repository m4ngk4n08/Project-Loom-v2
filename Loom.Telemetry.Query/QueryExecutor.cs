using System.Diagnostics;
using Loom.Storage;
using Loom.Telemetry;
using Loom.Web.Contracts.Dtos;

namespace Loom.Telemetry.Query;

public interface IQueryExecutor
{
    ValueTask<QueryResponse> ExecuteAsync(string queryText, CancellationToken ct);
}

public sealed class QueryExecutor : IQueryExecutor
{
    private const int RawDefaultCap = 1000;

    private readonly IMetricStore _store;

    public QueryExecutor(IMetricStore store)
    {
        _store = store;
    }

    public ValueTask<QueryResponse> ExecuteAsync(string queryText, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var ast = QueryParser.Parse(queryText);
        var plan = QueryPlanner.Plan(ast);

        var rows = plan.Ast.Columns.Any(c => c.Aggregate != AggregateFunction.None)
            ? ExecuteAggregate(plan)
            : ExecuteRaw(plan);

        if (plan.Ast.OrderByColumn is not null)
        {
            rows = plan.Ast.OrderDescending
                ? rows.OrderByDescending(RowSortKey).ToList()
                : rows.OrderBy(RowSortKey).ToList();
        }

        rows = plan.Ast.Limit is { } limit ? rows.Take(limit).ToList() : rows;

        var elapsed = Stopwatch.GetElapsedTime(started);

        var response = new QueryResponse
        {
            Columns = BuildColumns(plan.Ast),
            Rows = rows,
            ExecutionTimeMs = elapsed.TotalMilliseconds
        };
        return ValueTask.FromResult(response);
    }

    private List<QueryResultRow> ExecuteRaw(QueryPlan plan)
    {
        var buffers = _store.GetBuffers();
        var metricNames = ResolveMetricNames(plan, buffers);
        var cap = plan.Ast.Limit ?? RawDefaultCap;
        var rows = new List<QueryResultRow>();

        // SELECT method -> one row per metric name (deduplicated)
        if (IsOnlyMethodColumn(plan.Ast))
        {
            foreach (var metricName in metricNames)
            {
                if (!MatchesConditions(metricName, plan.Ast.Conditions)) continue;
                rows.Add(new QueryResultRow
                {
                    Values = [new QueryValue { Text = metricName }]
                });
                if (rows.Count >= cap) break;
            }
            return rows;
        }

        // SELECT * or SELECT <metric> -> one row per sample
        foreach (var metricName in metricNames)
        {
            if (!buffers.TryGetValue(metricName, out var buffer)) continue;
            if (!MatchesConditions(metricName, plan.Ast.Conditions)) continue;

            var entries = buffer.Snapshot();
            if (entries.Length == 0) continue;

            var typeName = GetTypeName(buffer);
            foreach (var (value, timestamp) in entries)
            {
                rows.Add(new QueryResultRow
                {
                    Values =
                    [
                        new QueryValue { Text = metricName },
                        new QueryValue { Number = value },
                        new QueryValue { Timestamp = timestamp },
                        new QueryValue { Text = typeName }
                    ]
                });
                if (rows.Count >= cap) break;
            }
            if (rows.Count >= cap) break;
        }

        return rows;
    }

    private List<QueryResultRow> ExecuteAggregate(QueryPlan plan)
    {
        var buffers = _store.GetBuffers();
        var metricNames = ResolveMetricNames(plan, buffers);
        var rows = new List<QueryResultRow>();

        // Always label each row with its metric name so aggregate results are attributable
        var includeMethodColumn = !plan.Ast.Columns.Any(c =>
            c.Aggregate == AggregateFunction.None &&
            c.Name.Equals("method", StringComparison.OrdinalIgnoreCase));

        foreach (var metricName in metricNames)
        {
            if (!buffers.TryGetValue(metricName, out var buffer)) continue;
            if (!MatchesConditions(metricName, plan.Ast.Conditions)) continue;

            var entries = buffer.Snapshot();
            if (entries.Length == 0) continue;

            var values = new List<QueryValue>(plan.Ast.Columns.Count + (includeMethodColumn ? 1 : 0));
            if (includeMethodColumn)
                values.Add(new QueryValue { Text = metricName });

            foreach (var column in plan.Ast.Columns)
            {
                if (column.Aggregate == AggregateFunction.None)
                {
                    values.Add(new QueryValue { Text = metricName });
                    continue;
                }

                values.Add(column.Aggregate switch
                {
                    AggregateFunction.Avg => new QueryValue { Number = entries.Average(e => e.Value) },
                    AggregateFunction.Count => new QueryValue { Number = entries.Length },
                    AggregateFunction.Max => new QueryValue { Number = entries.Max(e => e.Value) },
                    AggregateFunction.Min => new QueryValue { Number = entries.Min(e => e.Value) },
                    AggregateFunction.P99 => new QueryValue { Number = Percentile(entries.Select(e => e.Value), 0.99) },
                    _ => new QueryValue { Number = 0 }
                });
            }

            rows.Add(new QueryResultRow { Values = values });
        }

        return rows;
    }

    private static List<string> BuildColumns(QueryAst ast)
    {
        var hasAggregates = ast.Columns.Any(c => c.Aggregate != AggregateFunction.None);

        if (hasAggregates)
        {
            var columns = ast.Columns.Select(c =>
                c.Aggregate == AggregateFunction.None ? c.Name : $"{c.Aggregate}({c.Name})".ToUpperInvariant()).ToList();

            if (!ast.Columns.Any(c =>
                    c.Aggregate == AggregateFunction.None &&
                    c.Name.Equals("method", StringComparison.OrdinalIgnoreCase)))
            {
                columns.Insert(0, "method");
            }

            return columns;
        }

        if (IsOnlyMethodColumn(ast))
            return ["method"];

        return ["method", "value", "timestamp", "type"];
    }

    private static bool IsOnlyMethodColumn(QueryAst ast) =>
        ast.Columns.Count == 1 &&
        ast.Columns[0].Aggregate == AggregateFunction.None &&
        ast.Columns[0].Name.Equals("method", StringComparison.OrdinalIgnoreCase);

    private static List<string> ResolveMetricNames(QueryPlan plan, IReadOnlyDictionary<string, MetricBuffer> buffers) =>
        plan.ReferencedMetricNames.Count > 0
            ? plan.ReferencedMetricNames.ToList()
            : buffers.Keys.ToList();

    private static string GetTypeName(MetricBuffer buffer)
    {
        var recent = buffer.ReadRecent(1);
        if (recent.Length == 0) return "unknown";
        return recent[0].Type switch
        {
            MetricType.Counter => "counter",
            MetricType.Gauge => "gauge",
            MetricType.Histogram => "histogram",
            MetricType.MethodExecution => "method",
            _ => "unknown"
        };
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

    private static double RowSortKey(QueryResultRow row) => row.Values[^1].Number ?? 0;
}