using System;
using System.Linq;
using Xunit;
using Loom.Telemetry.Query;

namespace Loom.Telemetry.Tests;

public class QueryParserTests
{
    [Fact]
    public void Parser_ParsesSimpleSelect()
    {
        var query = "SELECT method FROM telemetry";
        var ast = QueryParser.Parse(query);

        Assert.Single(ast.Columns);
        Assert.Equal("method", ast.Columns[0].Name);
        Assert.Equal(AggregateFunction.None, ast.Columns[0].Aggregate);
        Assert.Empty(ast.Conditions);
        Assert.Null(ast.GroupByColumn);
        Assert.Null(ast.OrderByColumn);
        Assert.Null(ast.Limit);
    }

    [Fact]
    public void Parser_ParsesMultipleColumns()
    {
        var query = "SELECT method, duration, value FROM telemetry";
        var ast = QueryParser.Parse(query);

        Assert.Equal(3, ast.Columns.Count);
        Assert.Equal("method", ast.Columns[0].Name);
        Assert.Equal("duration", ast.Columns[1].Name);
        Assert.Equal("value", ast.Columns[2].Name);
    }

    [Fact]
    public void Parser_ParsesAggregateFunction()
    {
        var query = "SELECT method, AVG(duration) FROM telemetry";
        var ast = QueryParser.Parse(query);

        Assert.Equal(2, ast.Columns.Count);
        Assert.Equal("method", ast.Columns[0].Name);
        Assert.Equal(AggregateFunction.None, ast.Columns[0].Aggregate);
        Assert.Equal("duration", ast.Columns[1].Name);
        Assert.Equal(AggregateFunction.Avg, ast.Columns[1].Aggregate);
    }

    [Fact]
    public void Parser_ParsesAllAggregateFunctions()
    {
        var query = "SELECT AVG(a), COUNT(*), MAX(b), MIN(c), P99(d) FROM telemetry";
        var ast = QueryParser.Parse(query);

        Assert.Equal(5, ast.Columns.Count);
        Assert.Equal(AggregateFunction.Avg, ast.Columns[0].Aggregate);
        Assert.Equal(AggregateFunction.Count, ast.Columns[1].Aggregate);
        Assert.Equal(AggregateFunction.Max, ast.Columns[2].Aggregate);
        Assert.Equal(AggregateFunction.Min, ast.Columns[3].Aggregate);
        Assert.Equal(AggregateFunction.P99, ast.Columns[4].Aggregate);
    }

    [Fact]
    public void Parser_ParsesWhereClause()
    {
        var query = "SELECT method FROM telemetry WHERE method = 'ProcessOrder'";
        var ast = QueryParser.Parse(query);

        Assert.Single(ast.Conditions);
        Assert.Equal("method", ast.Conditions[0].Column);
        Assert.Equal("=", ast.Conditions[0].Operator);
        Assert.Equal("ProcessOrder", ast.Conditions[0].Value);
    }

    [Fact]
    public void Parser_ParsesMultipleWhereConditions()
    {
        var query = "SELECT method FROM telemetry WHERE method = 'test' AND duration > 100";
        var ast = QueryParser.Parse(query);

        Assert.Equal(2, ast.Conditions.Count);
        Assert.Equal("method", ast.Conditions[0].Column);
        Assert.Equal("=", ast.Conditions[0].Operator);
        Assert.Equal("test", ast.Conditions[0].Value);
        Assert.Equal("duration", ast.Conditions[1].Column);
        Assert.Equal(">", ast.Conditions[1].Operator);
        Assert.Equal("100", ast.Conditions[1].Value);
    }

    [Fact]
    public void Parser_ParsesGroupBy()
    {
        var query = "SELECT method, AVG(duration) FROM telemetry GROUP BY method";
        var ast = QueryParser.Parse(query);

        Assert.Equal("method", ast.GroupByColumn);
    }

    [Fact]
    public void Parser_ParsesOrderByAscending()
    {
        var query = "SELECT method FROM telemetry ORDER BY method ASC";
        var ast = QueryParser.Parse(query);

        Assert.Equal("method", ast.OrderByColumn);
        Assert.False(ast.OrderDescending);
    }

    [Fact]
    public void Parser_ParsesOrderByDescending()
    {
        var query = "SELECT method FROM telemetry ORDER BY method DESC";
        var ast = QueryParser.Parse(query);

        Assert.Equal("method", ast.OrderByColumn);
        Assert.True(ast.OrderDescending);
    }

    [Fact]
    public void Parser_ParsesOrderByDefaultAscending()
    {
        var query = "SELECT method FROM telemetry ORDER BY method";
        var ast = QueryParser.Parse(query);

        Assert.Equal("method", ast.OrderByColumn);
        Assert.False(ast.OrderDescending);
    }

    [Fact]
    public void Parser_ParsesLimit()
    {
        var query = "SELECT method FROM telemetry LIMIT 10";
        var ast = QueryParser.Parse(query);

        Assert.Equal(10, ast.Limit);
    }

    [Fact]
    public void Parser_ThrowsQuerySyntaxException_ForNonNumericLimit()
    {
        var query = "SELECT method FROM telemetry LIMIT abc";

        Assert.Throws<QuerySyntaxException>(() => QueryParser.Parse(query));
    }

    [Fact]
    public void Parser_ThrowsQuerySyntaxException_ForOverflowingLimit()
    {
        var query = "SELECT method FROM telemetry LIMIT 99999999999";

        Assert.Throws<QuerySyntaxException>(() => QueryParser.Parse(query));
    }

    [Fact]
    public void Parser_ThrowsQuerySyntaxException_ForOverLengthQuery()
    {
        var query = "SELECT method FROM telemetry WHERE method = '"
            + new string('a', QueryParser.MaxQueryLength) + "'";

        Assert.Throws<QuerySyntaxException>(() => QueryParser.Parse(query));
    }

    [Fact]
    public void Parser_ParsesComplexQuery()
    {
        var query = "SELECT method, AVG(duration) FROM telemetry WHERE method = 'test' AND duration > 100 GROUP BY method ORDER BY AVG(duration) DESC LIMIT 10";
        var ast = QueryParser.Parse(query);

        Assert.Equal(2, ast.Columns.Count);
        Assert.Equal("method", ast.Columns[0].Name);
        Assert.Equal("duration", ast.Columns[1].Name);
        Assert.Equal(AggregateFunction.Avg, ast.Columns[1].Aggregate);
        Assert.Equal(2, ast.Conditions.Count);
        Assert.Equal("method", ast.GroupByColumn);
        Assert.Equal("AVG", ast.OrderByColumn); // Parser extracts just the function name for ORDER BY
        Assert.True(ast.OrderDescending);
        Assert.Equal(10, ast.Limit);
    }

    [Fact]
    public void Parser_ThrowsOnMissingSELECT()
    {
        Assert.Throws<QuerySyntaxException>(() => QueryParser.Parse("FROM telemetry"));
    }

    [Fact]
    public void Parser_ThrowsOnMissingFROM()
    {
        Assert.Throws<QuerySyntaxException>(() => QueryParser.Parse("SELECT method"));
    }

    [Fact]
    public void Parser_ThrowsOnInvalidTableName()
    {
        Assert.Throws<QuerySyntaxException>(() => QueryParser.Parse("SELECT method FROM invalid_table"));
    }

    [Fact]
    public void Parser_HandlesCaseInsensitivity()
    {
        var query = "sElEcT method FrOm TeLeMeTrY wHeRe method = 'test' GrOuP bY method OrDeR bY method DeSc LiMiT 5";
        var ast = QueryParser.Parse(query);

        Assert.Single(ast.Columns);
        Assert.Single(ast.Conditions);
        Assert.Equal("method", ast.GroupByColumn);
        Assert.Equal("method", ast.OrderByColumn);
        Assert.True(ast.OrderDescending);
        Assert.Equal(5, ast.Limit);
    }

    [Fact]
    public void Parser_ParsesStringLiteralsWithSpaces()
    {
        var query = "SELECT method FROM telemetry WHERE region = 'US West'";
        var ast = QueryParser.Parse(query);

        Assert.Single(ast.Conditions);
        Assert.Equal("US West", ast.Conditions[0].Value);
    }

    [Fact]
    public void Parser_ParsesAllOperators()
    {
        var queries = new[]
        {
            "SELECT method FROM telemetry WHERE value = 10",
            "SELECT method FROM telemetry WHERE value > 10",
            "SELECT method FROM telemetry WHERE value < 10",
            "SELECT method FROM telemetry WHERE value >= 10",
            "SELECT method FROM telemetry WHERE value <= 10"
        };

        foreach (var query in queries)
        {
            var ast = QueryParser.Parse(query);
            Assert.Single(ast.Conditions);
        }
    }
}
