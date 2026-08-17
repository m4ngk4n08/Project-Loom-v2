using System;
using System.Linq;
using Xunit;

namespace Loom.Telemetry.Tests;

public sealed class MetricsApiTests : IDisposable
{
    public MetricsApiTests()
    {
        LoomSampling.ClearRules();
    }

    public void Dispose()
    {
        LoomSampling.ClearRules();
    }

    [Fact]
    public void RecordCounter_StoresMetric()
    {
        // Arrange
        var metricName = $"test.counter.{Guid.NewGuid()}";

        // Act
        LoomMetrics.RecordCounter(metricName, 1);

        // Assert
        var recent = LoomMetrics.GetRecentMetrics(100);
        var found = recent.FirstOrDefault(m => m.Name == metricName);

        Assert.NotNull(found.Name); // Struct is never null, check Name instead
        Assert.Equal(MetricType.Counter, found.Type);
        Assert.Equal(1, found.Value);
    }

    [Fact]
    public void RecordGauge_StoresMetric()
    {
        // Arrange
        var metricName = $"test.gauge.{Guid.NewGuid()}";

        // Act
        LoomMetrics.RecordGauge(metricName, 42.5);

        // Assert
        var recent = LoomMetrics.GetRecentMetrics(100);
        var found = recent.FirstOrDefault(m => m.Name == metricName);

        Assert.NotNull(found.Name);
        Assert.Equal(MetricType.Gauge, found.Type);
        Assert.Equal(42.5, found.Value);
    }

    [Fact]
    public void RecordHistogram_StoresMetric()
    {
        // Arrange
        var metricName = $"test.histogram.{Guid.NewGuid()}";

        // Act
        LoomMetrics.RecordHistogram(metricName, 123.45);

        // Assert
        var recent = LoomMetrics.GetRecentMetrics(100);
        var found = recent.FirstOrDefault(m => m.Name == metricName);

        Assert.NotNull(found.Name);
        Assert.Equal(MetricType.Histogram, found.Type);
        Assert.Equal(123.45, found.Value);
    }

    [Fact]
    public void RecordMetric_WithTags_StoresTags()
    {
        // Arrange
        var metricName = $"test.tagged.{Guid.NewGuid()}";
        var tag1 = new MetricTag("region", "us-west");
        var tag2 = new MetricTag("tier", "premium");

        // Act
        LoomMetrics.RecordCounter(metricName, 1, tag1, tag2);

        // Assert
        var recent = LoomMetrics.GetRecentMetrics(100);
        var found = recent.FirstOrDefault(m => m.Name == metricName);

        Assert.NotNull(found.Name);
        Assert.NotNull(found.Tags);
        Assert.Equal(2, found.Tags!.Length);
        Assert.Contains(found.Tags, t => t.Key == "region" && t.Value == "us-west");
        Assert.Contains(found.Tags, t => t.Key == "tier" && t.Value == "premium");
    }

    [Fact]
    public void QueryMetrics_FiltersByName()
    {
        // Arrange
        var metricName = $"test.query.{Guid.NewGuid()}";
        LoomMetrics.RecordCounter(metricName, 1);
        LoomMetrics.RecordCounter(metricName, 2);
        LoomMetrics.RecordCounter("other.metric", 999);

        // Act
        var results = LoomMetrics.QueryMetrics(metricName, TimeSpan.FromSeconds(1)).ToArray();

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal(metricName, r.Name));
    }

    [Fact]
    public void QueryMetrics_FiltersByTypeAndName()
    {
        // Arrange
        var metricName = $"test.multitype.{Guid.NewGuid()}";
        LoomMetrics.RecordCounter(metricName, 1);
        LoomMetrics.RecordGauge(metricName, 2);

        // Act
        var counters = LoomMetrics.QueryMetrics(metricName, MetricType.Counter, TimeSpan.FromSeconds(1)).ToArray();
        var gauges = LoomMetrics.QueryMetrics(metricName, MetricType.Gauge, TimeSpan.FromSeconds(1)).ToArray();

        // Assert
        Assert.All(counters, r => Assert.Equal(MetricType.Counter, r.Type));
        Assert.All(gauges, r => Assert.Equal(MetricType.Gauge, r.Type));
    }

    [Fact]
    public void MetricTag_Equality_Works()
    {
        // Arrange
        var tag1 = new MetricTag("key", "value");
        var tag2 = new MetricTag("key", "value");
        var tag3 = new MetricTag("key", "different");

        // Assert
        Assert.Equal(tag1, tag2);
        Assert.NotEqual(tag1, tag3);
        Assert.True(tag1 == tag2);
        Assert.True(tag1 != tag3);
    }

    [Fact]
    public void MetricBuffer_CircularBehavior()
    {
        // Arrange - Get buffer capacity and use unique metric name
        var capacity = LoomMetrics.GetBufferCapacity();
        var metricName = $"overflow.test.{Guid.NewGuid()}";

        // Act - Write more than capacity to a single metric
        for (int i = 0; i < capacity + 100; i++)
        {
            LoomMetrics.RecordCounter(metricName, i);
        }

        // Assert - Query this specific metric, should only have 'capacity' records (oldest overwritten)
        var recent = LoomMetrics.QueryMetrics(metricName, TimeSpan.FromHours(1)).ToArray();
        Assert.True(recent.Length <= capacity, $"Expected at most {capacity} records, got {recent.Length}");
    }
}
