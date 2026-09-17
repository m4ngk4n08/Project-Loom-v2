using System;
using System.Linq;
using Xunit;

namespace Loom.Telemetry.Tests;

/// <summary>
/// Covers LoomRuntime.RecordPropertyChange&lt;T&gt;, the entry point [LoomTrack]-generated
/// setters call into. Convert.ToDouble throws for non-numeric IConvertible types (string,
/// char, DateTime) - RecordPropertyChange must skip those silently instead of throwing.
/// </summary>
public sealed class LoomRuntimeTests : IDisposable
{
    public LoomRuntimeTests()
    {
        LoomSampling.ClearRules();
    }

    public void Dispose()
    {
        LoomSampling.ClearRules();
    }

    private enum Status
    {
        Idle = 0,
        Running = 1
    }

    [Fact]
    public void RecordPropertyChange_WithNonNumericString_DoesNotThrowAndRecordsNothing()
    {
        var metricName = $"test.loomruntime.string.{Guid.NewGuid()}";

        var exception = Record.Exception(() => LoomRuntime.RecordPropertyChange(metricName, "Running"));

        Assert.Null(exception);
        var recent = LoomMetrics.GetRecentMetrics(100);
        Assert.Null(recent.FirstOrDefault(m => m.Name == metricName).Name);
    }

    [Fact]
    public void RecordPropertyChange_WithChar_DoesNotThrowAndRecordsNothing()
    {
        var metricName = $"test.loomruntime.char.{Guid.NewGuid()}";

        var exception = Record.Exception(() => LoomRuntime.RecordPropertyChange(metricName, 'x'));

        Assert.Null(exception);
        var recent = LoomMetrics.GetRecentMetrics(100);
        Assert.Null(recent.FirstOrDefault(m => m.Name == metricName).Name);
    }

    [Fact]
    public void RecordPropertyChange_WithDateTime_DoesNotThrowAndRecordsNothing()
    {
        var metricName = $"test.loomruntime.datetime.{Guid.NewGuid()}";

        var exception = Record.Exception(() => LoomRuntime.RecordPropertyChange(metricName, DateTime.UtcNow));

        Assert.Null(exception);
        var recent = LoomMetrics.GetRecentMetrics(100);
        Assert.Null(recent.FirstOrDefault(m => m.Name == metricName).Name);
    }

    [Fact]
    public void RecordPropertyChange_WithInt_RecordsAGaugeWithTheExpectedValue()
    {
        var metricName = $"test.loomruntime.int.{Guid.NewGuid()}";

        LoomRuntime.RecordPropertyChange(metricName, 42);

        var recent = LoomMetrics.GetRecentMetrics(100);
        var found = recent.FirstOrDefault(m => m.Name == metricName);

        Assert.NotNull(found.Name);
        Assert.Equal(MetricType.Gauge, found.Type);
        Assert.Equal(42, found.Value);
    }

    [Fact]
    public void RecordPropertyChange_WithBool_RecordsAGaugeWithTheExpectedValue()
    {
        var metricName = $"test.loomruntime.bool.{Guid.NewGuid()}";

        LoomRuntime.RecordPropertyChange(metricName, true);

        var recent = LoomMetrics.GetRecentMetrics(100);
        var found = recent.FirstOrDefault(m => m.Name == metricName);

        Assert.NotNull(found.Name);
        Assert.Equal(MetricType.Gauge, found.Type);
        Assert.Equal(1, found.Value);
    }

    [Fact]
    public void RecordPropertyChange_WithEnum_RecordsAGaugeWithTheExpectedValue()
    {
        var metricName = $"test.loomruntime.enum.{Guid.NewGuid()}";

        LoomRuntime.RecordPropertyChange(metricName, Status.Running);

        var recent = LoomMetrics.GetRecentMetrics(100);
        var found = recent.FirstOrDefault(m => m.Name == metricName);

        Assert.NotNull(found.Name);
        Assert.Equal(MetricType.Gauge, found.Type);
        Assert.Equal(1, found.Value);
    }
}
