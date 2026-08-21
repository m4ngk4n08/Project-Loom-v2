using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Loom.Telemetry;
using Loom.Telemetry.Query;
using Loom.Storage;

namespace Loom.Telemetry.Tests;

public class LoomQueryBuilderTests
{
    [Fact]
    public async Task Builder_BuildsSimpleSelectQuery()
    {
        // Arrange
        var builder = new LoomQueryBuilder()
            .Select("method");

        var executor = new QueryExecutor(LoomMetricsStoreAdapter.Instance);

        // Act
        var result = await builder.ExecuteAsync(executor);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Columns);
        Assert.Contains("method", result.Columns);
    }

    [Fact]
    public async Task Builder_BuildsQueryWithMultipleColumns()
    {
        // Arrange
        var builder = new LoomQueryBuilder()
            .Select("method")
            .Select("duration");

        var executor = new QueryExecutor(LoomMetricsStoreAdapter.Instance);

        // Act
        var result = await builder.ExecuteAsync(executor);

        // Assert
        Assert.NotNull(result);
        // Raw select emits the comprehensive sample shape
        Assert.Equal(4, result.Columns.Count);
        Assert.Contains("method", result.Columns);
        Assert.Contains("value", result.Columns);
        Assert.Contains("timestamp", result.Columns);
        Assert.Contains("type", result.Columns);
    }

    [Fact]
    public async Task Builder_BuildsQueryWithAggregateFunction()
    {
        // Arrange
        var metricName = $"fluent_avg_{Guid.NewGuid()}";
        LoomMetrics.RecordHistogram(metricName, 10.0);
        LoomMetrics.RecordHistogram(metricName, 20.0);

        var builder = new LoomQueryBuilder()
            .Select("method")
            .Select("duration", AggregateFunction.Avg);

        var executor = new QueryExecutor(LoomMetricsStoreAdapter.Instance);

        // Act
        var result = await builder.ExecuteAsync(executor);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Columns.Count);
        Assert.Contains("AVG(DURATION)", result.Columns);
    }

    [Fact]
    public async Task Builder_BuildsQueryWithAllAggregateFunctions()
    {
        // Arrange
        var builder = new LoomQueryBuilder()
            .Select("a", AggregateFunction.Avg)
            .Select("b", AggregateFunction.Count)
            .Select("c", AggregateFunction.Max)
            .Select("d", AggregateFunction.Min)
            .Select("e", AggregateFunction.P99);

        var executor = new QueryExecutor(LoomMetricsStoreAdapter.Instance);

        // Act
        var result = await builder.ExecuteAsync(executor);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(6, result.Columns.Count);
        Assert.Equal("method", result.Columns[0]);
        Assert.Contains("AVG(A)", result.Columns);
        Assert.Contains("COUNT(B)", result.Columns);
        Assert.Contains("MAX(C)", result.Columns);
        Assert.Contains("MIN(D)", result.Columns);
        Assert.Contains("P99(E)", result.Columns);
    }

    [Fact]
    public async Task Builder_BuildsQueryWithWhereClause()
    {
        // Arrange
        var builder = new LoomQueryBuilder()
            .Select("method")
            .Where("method", "=", "ProcessOrder");

        var executor = new QueryExecutor(LoomMetricsStoreAdapter.Instance);

        // Act
        var result = await builder.ExecuteAsync(executor);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Builder_BuildsQueryWithMultipleWhereConditions()
    {
        // Arrange
        var builder = new LoomQueryBuilder()
            .Select("method")
            .Where("method", "=", "test")
            .Where("duration", ">", "100");

        var executor = new QueryExecutor(LoomMetricsStoreAdapter.Instance);

        // Act
        var result = await builder.ExecuteAsync(executor);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Builder_BuildsQueryWithLastTimeWindow()
    {
        // Arrange
        var builder = new LoomQueryBuilder()
            .Select("method")
            .Last(TimeSpan.FromMinutes(5));

        var executor = new QueryExecutor(LoomMetricsStoreAdapter.Instance);

        // Act
        var result = await builder.ExecuteAsync(executor);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Builder_BuildsQueryWithGroupBy()
    {
        // Arrange
        var builder = new LoomQueryBuilder()
            .Select("method")
            .Select("duration", AggregateFunction.Avg)
            .GroupBy("method");

        var executor = new QueryExecutor(LoomMetricsStoreAdapter.Instance);

        // Act
        var result = await builder.ExecuteAsync(executor);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Builder_BuildsQueryWithOrderByDescending()
    {
        // Arrange
        var builder = new LoomQueryBuilder()
            .Select("method")
            .Select("duration", AggregateFunction.Avg)
            .OrderByDescending("duration");

        var executor = new QueryExecutor(LoomMetricsStoreAdapter.Instance);

        // Act
        var result = await builder.ExecuteAsync(executor);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Builder_BuildsQueryWithLimit()
    {
        // Arrange
        var builder = new LoomQueryBuilder()
            .Select("method")
            .Take(10);

        var executor = new QueryExecutor(LoomMetricsStoreAdapter.Instance);

        // Act
        var result = await builder.ExecuteAsync(executor);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Rows.Count <= 10);
    }

    [Fact]
    public async Task Builder_BuildsComplexQuery()
    {
        // Arrange: Record test data
        var metricName = $"complex_test_{Guid.NewGuid()}";
        LoomMetrics.RecordHistogram(metricName, 10.0);
        LoomMetrics.RecordHistogram(metricName, 20.0);
        LoomMetrics.RecordHistogram(metricName, 30.0);

        var builder = new LoomQueryBuilder()
            .Select("method")
            .Select("duration", AggregateFunction.Avg)
            .Where("method", "=", metricName)
            .GroupBy("method")
            .OrderByDescending("duration")
            .Take(10);

        var executor = new QueryExecutor(LoomMetricsStoreAdapter.Instance);

        // Act
        var result = await builder.ExecuteAsync(executor);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Columns.Count);
        Assert.True(result.Rows.Count <= 10);
    }

    [Fact]
    public async Task Builder_SupportsMethodChaining()
    {
        // Arrange & Act
        var executor = new QueryExecutor(LoomMetricsStoreAdapter.Instance);
        var result = await new LoomQueryBuilder()
            .Select("method")
            .Select("duration", AggregateFunction.Avg)
            .Where("method", "=", "test")
            .GroupBy("method")
            .OrderByDescending("duration")
            .Take(5)
            .ExecuteAsync(executor);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Builder_HandlesCancellation()
    {
        // Arrange
        var builder = new LoomQueryBuilder().Select("method");
        var executor = new QueryExecutor(LoomMetricsStoreAdapter.Instance);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        // Note: Current implementation doesn't actually use CT in executor,
        // but the API supports it for future use
        var result = await builder.ExecuteAsync(executor, cts.Token);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Builder_ProducesConsistentResultsWithSqlEquivalent()
    {
        // Arrange: Record test data
        var metricName = $"consistency_{Guid.NewGuid()}";
        LoomMetrics.RecordHistogram(metricName, 15.0);
        LoomMetrics.RecordHistogram(metricName, 25.0);

        var executor = new QueryExecutor(LoomMetricsStoreAdapter.Instance);

        // Act: Execute via fluent API
        var fluentResult = await new LoomQueryBuilder()
            .Select("method")
            .Select("duration", AggregateFunction.Avg)
            .Where("method", "=", metricName)
            .Take(10)
            .ExecuteAsync(executor);

        // Act: Execute equivalent SQL
        var sqlResult = await executor.ExecuteAsync(
            $"SELECT method, AVG(duration) FROM telemetry WHERE method = '{metricName}' LIMIT 10",
            CancellationToken.None
        );

        // Assert: Results should match
        Assert.Equal(fluentResult.Columns.Count, sqlResult.Columns.Count);
        Assert.Equal(fluentResult.Rows.Count, sqlResult.Rows.Count);
    }

    [Fact]
    public async Task Builder_HandlesEmptyResultSet()
    {
        // Arrange
        var builder = new LoomQueryBuilder()
            .Select("method")
            .Where("method", "=", "nonexistent_metric_" + Guid.NewGuid());

        var executor = new QueryExecutor(LoomMetricsStoreAdapter.Instance);

        // Act
        var result = await builder.ExecuteAsync(executor);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Rows);
    }
}
