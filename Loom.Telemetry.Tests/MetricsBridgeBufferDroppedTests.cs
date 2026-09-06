using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using Xunit;

namespace Loom.Telemetry.Tests;

/// <summary>
/// Covers the loom.telemetry.buffer.dropped ObservableGauge&lt;long&gt; that MetricsBridge
/// publishes once a metric name's buffer exists. Uses the same MeterListener approach as
/// MetricsBridgeGaugeAndTagsTests.cs -- MetricsBridge is internal with no
/// InternalsVisibleTo, so this observes it as any EventPipe-based tool would.
/// </summary>
public sealed class MetricsBridgeBufferDroppedTests
{
    [Fact]
    public void BufferDroppedGauge_ReflectsDroppedCount_ForASpecificMetricName_AfterWraparound()
    {
        var name = $"test.bufferdropped.{System.Guid.NewGuid():N}";
        var capacity = LoomMetrics.GetBufferCapacity();
        const int overwrite = 11;

        for (var i = 0; i < capacity + overwrite; i++)
        {
            LoomMetrics.RecordCounter(name, i);
        }

        long? observed = null;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Loom.Telemetry" && instrument.Name == "loom.telemetry.buffer.dropped")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            var metricNameTag = tags.ToArray().FirstOrDefault(t => t.Key == "metric.name");
            if ((string?)metricNameTag.Value == name)
                observed = measurement;
        });

        listener.Start();
        listener.RecordObservableInstruments();
        listener.Dispose();

        Assert.NotNull(observed);
        Assert.Equal(overwrite, observed!.Value);
    }

    [Fact]
    public void BufferDroppedGauge_DoesNotReportAMetricName_WithNoBufferYet()
    {
        var name = $"test.bufferdropped.nobuffer.{System.Guid.NewGuid():N}";

        var observedNames = new List<string?>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Loom.Telemetry" && instrument.Name == "loom.telemetry.buffer.dropped")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            var metricNameTag = tags.ToArray().FirstOrDefault(t => t.Key == "metric.name");
            observedNames.Add((string?)metricNameTag.Value);
        });

        listener.Start();
        listener.RecordObservableInstruments();
        listener.Dispose();

        Assert.DoesNotContain(name, observedNames);
    }
}
