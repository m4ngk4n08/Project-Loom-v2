using Loom.Web.Contracts.Dtos;

namespace Loom.Telemetry.Query;

/// <summary>Builds the SAME QueryAst the SQL parser produces, via explicit method calls —
/// not Expression&lt;Func&lt;T,bool&gt;&gt;, which would need System.Linq.Expressions
/// (reflection-heavy). Matches ADR-7's fluent example: _loom.Query().Where(...).Last(...)
/// .GroupBy(...).OrderByDescending(...).Take(10).ExecuteAsync().</summary>
public sealed class LoomQueryBuilder
{
    private readonly List<SelectColumn> _columns = [];
    private readonly List<WhereCondition> _conditions = [];
    private string? _groupBy;
    private string? _orderBy;
    private bool _descending;
    private int? _limit;

    public LoomQueryBuilder Select(string column, AggregateFunction aggregate = AggregateFunction.None)
    {
        _columns.Add(new SelectColumn(column, aggregate));
        return this;
    }

    public LoomQueryBuilder Where(string column, string op, string value)
    {
        _conditions.Add(new WhereCondition(column, op, value));
        return this;
    }

    public LoomQueryBuilder Last(TimeSpan window) =>
        Where("timestamp", ">", DateTime.UtcNow.Subtract(window).ToString("O"));

    public LoomQueryBuilder GroupBy(string column) { _groupBy = column; return this; }

    public LoomQueryBuilder OrderByDescending(string column) { _orderBy = column; _descending = true; return this; }

    public LoomQueryBuilder Take(int count) { _limit = count; return this; }

    public ValueTask<QueryResponse> ExecuteAsync(IQueryExecutor executor, CancellationToken ct = default)
    {
        var ast = new QueryAst(_columns, _conditions, _groupBy, _orderBy, _descending, _limit);
        return executor.ExecuteAsync(RenderSql(ast), ct); // round-trips through the same executor as the SQL path
    }

    private static string RenderSql(QueryAst ast)
    {
        var cols = string.Join(", ", ast.Columns.Select(c => c.Aggregate == AggregateFunction.None ? c.Name : $"{c.Aggregate}({c.Name})"));
        var sql = $"SELECT {cols} FROM telemetry";
        if (ast.Conditions.Count > 0) sql += " WHERE " + string.Join(" AND ", ast.Conditions.Select(c => $"{c.Column} {c.Operator} '{c.Value}'"));
        if (ast.GroupByColumn is not null) sql += $" GROUP BY {ast.GroupByColumn}";
        if (ast.OrderByColumn is not null) sql += $" ORDER BY {ast.OrderByColumn} {(ast.OrderDescending ? "DESC" : "ASC")}";
        if (ast.Limit is { } limit) sql += $" LIMIT {limit}";
        return sql;
    }
}
