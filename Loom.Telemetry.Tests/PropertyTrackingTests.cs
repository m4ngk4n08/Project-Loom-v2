using System;
using System.Linq;
using Xunit;

namespace Loom.Telemetry.Tests;

public sealed class PropertyTrackingTests
{
    [Fact]
    public void TrackedProperty_RecordsChanges()
    {
        // Arrange
        var instance = new TrackedPropertySample();
        var metricName = $"TrackedPropertySample.Counter";

        // Act
        instance.Counter_Tracked = 42;

        // Assert
        var recent = LoomMetrics.GetRecentMetrics(100);
        var found = recent.FirstOrDefault(m => m.Name == metricName);

        Assert.NotNull(found.Name);
        Assert.Equal(MetricType.Gauge, found.Type);
        Assert.Equal(42, found.Value);
    }

    [Fact]
    public void TrackedProperty_RecordsMultipleChanges()
    {
        // Arrange
        var instance = new TrackedPropertySample();
        var metricName = $"TrackedPropertySample.Temperature";

        // Act
        instance.Temperature_Tracked = 72.5;
        instance.Temperature_Tracked = 75.0;
        instance.Temperature_Tracked = 68.3;

        // Assert
        var recent = LoomMetrics.QueryMetrics(metricName, TimeSpan.FromSeconds(1)).ToArray();

        Assert.True(recent.Length >= 3);
        Assert.All(recent, r => Assert.Equal(MetricType.Gauge, r.Type));
    }

    [Fact]
    public void TrackedProperty_GetterReturnsValue()
    {
        // Arrange
        var instance = new TrackedPropertySample();

        // Act
        instance.Counter_Tracked = 100;
        var value = instance.Counter_Tracked;

        // Assert
        Assert.Equal(100, value);
    }

    [Fact]
    public void TrackedProperty_WithCustomName()
    {
        // Arrange
        var instance = new TrackedPropertySample();
        var metricName = "CustomQueueMetric";

        // Act
        instance.QueueDepth_Tracked = 25;

        // Assert
        var recent = LoomMetrics.GetRecentMetrics(100);
        var found = recent.FirstOrDefault(m => m.Name == metricName);

        Assert.NotNull(found.Name);
        Assert.Equal(25, found.Value);
    }
}

/// <summary>
/// Sample class with tracked properties for testing.
/// </summary>
public partial class TrackedPropertySample
{
    [LoomTrack]
    public int Counter { get; set; }

    [LoomTrack]
    public double Temperature { get; set; }

    [LoomTrack(Name = "CustomQueueMetric")]
    public int QueueDepth { get; set; }
}
