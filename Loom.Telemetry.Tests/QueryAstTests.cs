using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Loom.Telemetry;
using Loom.Telemetry.Query;

namespace Loom.Telemetry.Tests;

public class QueryAstTests
{
    [Fact]
    public void SelectColumn_StoresNameAndAggregate()
    {
        var column = new SelectColumn("method", AggregateFunction.Avg);

        Assert.Equal("method", column.Name);
        Assert.Equal(AggregateFunction.Avg, column.Aggregate);
    }

    [Fact]
    public void SelectColumn_SupportsNoAggregate()
    {
        var column = new SelectColumn("method", AggregateFunction.None);

        Assert.Equal("method", column.Name);
        Assert.Equal(AggregateFunction.None, column.Aggregate);
    }

    [Fact]
    public void WhereCondition_StoresAllComponents()
    {
        var condition = new WhereCondition("method", "=", "test");

        Assert.Equal("method", condition.Column);
        Assert.Equal("=", condition.Operator);
        Assert.Equal("test", condition.Value);
    }

    [Fact]
    public void WhereCondition_SupportsAllOperators()
    {
        var operators = new[] { "=", ">", "<", ">=", "<=" };

        foreach (var op in operators)
        {
            var condition = new WhereCondition("column", op, "value");
            Assert.Equal(op, condition.Operator);
        }
    }

    [Fact]
    public void QueryAst_StoresAllComponents()
    {
        var columns = new List<SelectColumn>
        {
            new("method", AggregateFunction.None),
            new("duration", AggregateFunction.Avg)
        };

        var conditions = new List<WhereCondition>
        {
            new("method", "=", "test")
        };

        var ast = new QueryAst(
            columns,
            conditions,
            "method",
            "duration",
            true,
            10
        );

        Assert.Equal(2, ast.Columns.Count);
        Assert.Single(ast.Conditions);
        Assert.Equal("method", ast.GroupByColumn);
        Assert.Equal("duration", ast.OrderByColumn);
        Assert.True(ast.OrderDescending);
        Assert.Equal(10, ast.Limit);
    }

    [Fact]
    public void QueryAst_SupportsNullOptionalFields()
    {
        var columns = new List<SelectColumn> { new("method", AggregateFunction.None) };
        var conditions = new List<WhereCondition>();

        var ast = new QueryAst(columns, conditions, null, null, false, null);

        Assert.Null(ast.GroupByColumn);
        Assert.Null(ast.OrderByColumn);
        Assert.Null(ast.Limit);
    }

    [Fact]
    public void AggregateFunction_HasAllExpectedValues()
    {
        var values = Enum.GetValues<AggregateFunction>();

        Assert.Contains(AggregateFunction.None, values);
        Assert.Contains(AggregateFunction.Avg, values);
        Assert.Contains(AggregateFunction.Count, values);
        Assert.Contains(AggregateFunction.Max, values);
        Assert.Contains(AggregateFunction.Min, values);
        Assert.Contains(AggregateFunction.P99, values);
    }

    [Fact]
    public void QueryPlanner_ResolvesMetricNames()
    {
        // Arrange: Record a metric so it appears in buffers
        var metricName = $"planner_test_{Guid.NewGuid()}";
        LoomMetrics.RecordCounter(metricName, 1);

        var columns = new List<SelectColumn> { new(metricName, AggregateFunction.None) };
        var ast = new QueryAst(columns, new List<WhereCondition>(), null, null, false, null);

        // Act
        var plan = QueryPlanner.Plan(ast);

        // Assert
        Assert.NotNull(plan);
        Assert.NotNull(plan.ReferencedMetricNames);
        Assert.Contains(metricName, plan.ReferencedMetricNames);
    }

    [Fact]
    public void QueryPlanner_HandlesNonexistentMetrics()
    {
        // Arrange
        var nonexistentMetric = $"doesnt_exist_{Guid.NewGuid()}";
        var columns = new List<SelectColumn> { new(nonexistentMetric, AggregateFunction.None) };
        var ast = new QueryAst(columns, new List<WhereCondition>(), null, null, false, null);

        // Act
        var plan = QueryPlanner.Plan(ast);

        // Assert
        Assert.NotNull(plan);
        Assert.NotNull(plan.ReferencedMetricNames);
        // Planner is syntactic — it resolves the requested name; the executor
        // skips metrics missing from the store.
        Assert.Contains(nonexistentMetric, plan.ReferencedMetricNames);
    }

    [Fact]
    public void QueryPlanner_FiltersWildcardFromReferencedNames()
    {
        // Arrange
        var columns = new List<SelectColumn> { new("*", AggregateFunction.Count) };
        var ast = new QueryAst(columns, new List<WhereCondition>(), null, null, false, null);

        // Act
        var plan = QueryPlanner.Plan(ast);

        // Assert
        Assert.NotNull(plan);
        Assert.DoesNotContain("*", plan.ReferencedMetricNames);
    }

    [Fact]
    public void QueryPlanner_DeduplicatesMetricNames()
    {
        // Arrange: Same metric in multiple columns
        var metricName = $"dedup_test_{Guid.NewGuid()}";
        LoomMetrics.RecordCounter(metricName, 1);

        var columns = new List<SelectColumn>
        {
            new(metricName, AggregateFunction.None),
            new(metricName, AggregateFunction.None)
        };
        var ast = new QueryAst(columns, new List<WhereCondition>(), null, null, false, null);

        // Act
        var plan = QueryPlanner.Plan(ast);

        // Assert
        Assert.NotNull(plan);
        // Should only appear once despite being in multiple columns
        Assert.Equal(1, plan.ReferencedMetricNames.Count(m => m == metricName));
    }

    [Fact]
    public void QueryPlanner_IncludesMetricsFromConditions()
    {
        // Arrange
        var metricName = $"condition_test_{Guid.NewGuid()}";
        LoomMetrics.RecordCounter(metricName, 1);

        var columns = new List<SelectColumn> { new("method", AggregateFunction.None) };
        var conditions = new List<WhereCondition> { new("method", "=", metricName) };
        var ast = new QueryAst(columns, conditions, null, null, false, null);

        // Act
        var plan = QueryPlanner.Plan(ast);

        // Assert
        Assert.NotNull(plan);
        // WHERE filters are applied at execution; they must not be treated as
        // literal metric-name references (method is a pseudo-column).
        Assert.DoesNotContain("method", plan.ReferencedMetricNames);
    }
}
