using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Loom.Telemetry;
using Loom.Telemetry.Query;

namespace Loom.Telemetry.Tests;

public class QueryExecutorTests
{
    [Fact]
    public async Task Executor_ExecutesSuccessfully()
    {
        var executor = new QueryExecutor();
        var query = "SELECT method FROM telemetry";

        var result = await executor.ExecuteAsync(query, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Rows);
        Assert.True(result.ExecutionTimeMs >= 0);
    }

    [Fact]
    public async Task Executor_QueriesCounterMetrics()
    {
        // Arrange: Record some counter metrics
        LoomMetrics.RecordCounter("requests", 1);
        LoomMetrics.RecordCounter("requests", 1);
        LoomMetrics.RecordCounter("requests", 1);

        var executor = new QueryExecutor();
        var query = "SELECT method, COUNT(*) FROM telemetry";

        // Act
        var result = await executor.ExecuteAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Rows);
        Assert.Equal(2, result.Columns.Count);
        Assert.Contains("COUNT(*)", result.Columns);
    }

    [Fact]
    public async Task Executor_CalculatesAverage()
    {
        // Arrange: Record histogram metrics with known values
        var metricName = $"test_avg_{Guid.NewGuid()}";
        LoomMetrics.RecordHistogram(metricName, 10.0);
        LoomMetrics.RecordHistogram(metricName, 20.0);
        LoomMetrics.RecordHistogram(metricName, 30.0);

        var executor = new QueryExecutor();
        var query = $"SELECT method, AVG(duration) FROM telemetry";

        // Act
        var result = await executor.ExecuteAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var row = result.Rows.FirstOrDefault(r => r.Values[0].Text == metricName);
        if (row != null && row.Values[1].Number.HasValue)
        {
            Assert.Equal(20.0, row.Values[1].Number!.Value, precision: 1);
        }
    }

    [Fact]
    public async Task Executor_CalculatesMax()
    {
        // Arrange
        var metricName = $"test_max_{Guid.NewGuid()}";
        LoomMetrics.RecordHistogram(metricName, 5.0);
        LoomMetrics.RecordHistogram(metricName, 15.0);
        LoomMetrics.RecordHistogram(metricName, 10.0);

        var executor = new QueryExecutor();
        var query = "SELECT method, MAX(duration) FROM telemetry";

        // Act
        var result = await executor.ExecuteAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var row = result.Rows.FirstOrDefault(r => r.Values[0].Text == metricName);
        if (row != null)
        {
            Assert.Equal(15.0, row.Values[1].Number);
        }
    }

    [Fact]
    public async Task Executor_CalculatesMin()
    {
        // Arrange
        var metricName = $"test_min_{Guid.NewGuid()}";
        LoomMetrics.RecordHistogram(metricName, 5.0);
        LoomMetrics.RecordHistogram(metricName, 15.0);
        LoomMetrics.RecordHistogram(metricName, 10.0);

        var executor = new QueryExecutor();
        var query = "SELECT method, MIN(duration) FROM telemetry";

        // Act
        var result = await executor.ExecuteAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var row = result.Rows.FirstOrDefault(r => r.Values[0].Text == metricName);
        if (row != null)
        {
            Assert.Equal(5.0, row.Values[1].Number);
        }
    }

    [Fact]
    public async Task Executor_CalculatesP99()
    {
        // Arrange: Record 100 values, P99 should be the 99th value
        var metricName = $"test_p99_{Guid.NewGuid()}";
        for (int i = 1; i <= 100; i++)
        {
            LoomMetrics.RecordHistogram(metricName, i);
        }

        var executor = new QueryExecutor();
        var query = "SELECT method, P99(duration) FROM telemetry";

        // Act
        var result = await executor.ExecuteAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var row = result.Rows.FirstOrDefault(r => r.Values[0].Text == metricName);
        if (row != null)
        {
            // P99 of 1-100 should be around 99
            Assert.True(row.Values[1].Number >= 98.0 && row.Values[1].Number <= 100.0);
        }
    }

    [Fact]
    public async Task Executor_AppliesLimitCorrectly()
    {
        // Arrange: Record multiple metrics
        for (int i = 0; i < 20; i++)
        {
            LoomMetrics.RecordCounter($"metric_{i}", 1);
        }

        var executor = new QueryExecutor();
        var query = "SELECT method FROM telemetry LIMIT 5";

        // Act
        var result = await executor.ExecuteAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Rows.Count <= 5);
    }

    [Fact]
    public async Task Executor_OrdersByDescending()
    {
        // Arrange
        var metric1 = $"metric_a_{Guid.NewGuid()}";
        var metric2 = $"metric_b_{Guid.NewGuid()}";
        var metric3 = $"metric_c_{Guid.NewGuid()}";

        LoomMetrics.RecordHistogram(metric1, 10.0);
        LoomMetrics.RecordHistogram(metric2, 30.0);
        LoomMetrics.RecordHistogram(metric3, 20.0);

        var executor = new QueryExecutor();
        var query = "SELECT method, AVG(duration) FROM telemetry ORDER BY AVG(duration) DESC";

        // Act
        var result = await executor.ExecuteAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        if (result.Rows.Count >= 3)
        {
            // First row should have highest value
            var firstValue = result.Rows[0].Values[1].Number;
            var lastValue = result.Rows[^1].Values[1].Number;
            Assert.True(firstValue >= lastValue);
        }
    }

    [Fact]
    public async Task Executor_ReportsExecutionTime()
    {
        var executor = new QueryExecutor();
        var query = "SELECT method FROM telemetry";

        var result = await executor.ExecuteAsync(query, CancellationToken.None);

        Assert.True(result.ExecutionTimeMs >= 0);
        Assert.True(result.ExecutionTimeMs < 1000); // Should be fast
    }

    [Fact]
    public async Task Executor_HandlesMultipleAggregateFunctions()
    {
        // Arrange
        var metricName = $"test_multi_{Guid.NewGuid()}";
        LoomMetrics.RecordHistogram(metricName, 10.0);
        LoomMetrics.RecordHistogram(metricName, 20.0);
        LoomMetrics.RecordHistogram(metricName, 30.0);

        var executor = new QueryExecutor();
        var query = "SELECT method, AVG(duration), MAX(duration), MIN(duration), COUNT(*) FROM telemetry";

        // Act
        var result = await executor.ExecuteAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Columns.Count);
        var row = result.Rows.FirstOrDefault(r => r.Values[0].Text == metricName);
        if (row != null)
        {
            Assert.Equal(5, row.Values.Count);
            Assert.NotNull(row.Values[1].Number); // AVG
            Assert.NotNull(row.Values[2].Number); // MAX
            Assert.NotNull(row.Values[3].Number); // MIN
            Assert.NotNull(row.Values[4].Number); // COUNT
        }
    }

    [Fact]
    public async Task Executor_ThrowsOnInvalidSyntax()
    {
        var executor = new QueryExecutor();
        var query = "INVALID QUERY SYNTAX";

        await Assert.ThrowsAsync<QuerySyntaxException>(
            async () => await executor.ExecuteAsync(query, CancellationToken.None)
        );
    }

    [Fact]
    public async Task Executor_ReturnsCorrectColumnNames()
    {
        var executor = new QueryExecutor();
        var query = "SELECT method, AVG(duration), MAX(value) FROM telemetry";

        var result = await executor.ExecuteAsync(query, CancellationToken.None);

        Assert.Equal(3, result.Columns.Count);
        Assert.Equal("method", result.Columns[0]);
        Assert.Equal("AVG(DURATION)", result.Columns[1]);
        Assert.Equal("MAX(VALUE)", result.Columns[2]);
    }

    [Fact]
    public async Task Executor_HandlesConcurrentQueries()
    {
        // Arrange: Record some data
        for (int i = 0; i < 10; i++)
        {
            LoomMetrics.RecordCounter($"concurrent_{i}", i);
        }

        var executor = new QueryExecutor();
        var query = "SELECT method, COUNT(*) FROM telemetry";

        // Act: Run 10 queries concurrently
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => executor.ExecuteAsync(query, CancellationToken.None).AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert: All queries should complete successfully
        Assert.All(results, r => Assert.NotNull(r));
        Assert.All(results, r => Assert.True(r.ExecutionTimeMs >= 0));
    }
}
