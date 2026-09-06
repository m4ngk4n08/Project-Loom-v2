using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Xunit;

namespace Loom.Telemetry.Tests;

/// <summary>
/// Covers two MetricsBridge bugs: (1) RecordGauge used to publish through
/// PublishHistogram, so gauges showed up as a Histogram&lt;double&gt; instrument instead
/// of an ObservableGauge; (2) tags accepted by RecordCounter/RecordGauge/RecordHistogram
/// never reached the underlying Meter instruments. Uses MeterListener the same way
/// MetricsBridgeBeaconTests.cs does — MetricsBridge is internal with no
/// InternalsVisibleTo, so this observes it as any EventPipe-based tool would.
/// </summary>
public sealed class MetricsBridgeGaugeAndTagsTests
{
    [Fact]
    public void RecordGauge_PublishesToAnObservableGauge_WithTheLastRecordedValue()
    {
        var name = $"test.gauge.{System.Guid.NewGuid():N}";

        LoomMetrics.RecordGauge(name, 41);
        LoomMetrics.RecordGauge(name, 42);

        var found = false;
        double? observedValue = null;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Loom.Telemetry" && instrument.Name == name)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            found = true;
            observedValue = measurement;
        });

        listener.Start();
        listener.RecordObservableInstruments();
        listener.Dispose();

        Assert.True(found, $"Expected an ObservableGauge instrument named '{name}' on meter 'Loom.Telemetry'.");
        Assert.Equal(42, observedValue);
    }

    [Fact]
    public void RecordGauge_DoesNotPublishToAHistogramInstrument()
    {
        var name = $"test.gaugenothist.{System.Guid.NewGuid():N}";

        LoomMetrics.RecordGauge(name, 7);

        var histogramSeen = false;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Loom.Telemetry" && instrument.Name == name)
            {
                if (instrument.GetType().Name.Contains("Histogram"))
                    histogramSeen = true;
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) => { });

        listener.Start();
        listener.RecordObservableInstruments();
        listener.Dispose();

        Assert.False(histogramSeen, $"'{name}' should be published as a gauge, not a histogram.");
    }

    [Fact]
    public void RecordCounter_WithTags_PropagatesTagsToTheCounterInstrument()
    {
        var name = $"test.counter.tags.{System.Guid.NewGuid():N}";

        var found = false;
        KeyValuePair<string, object?>[]? observedTags = null;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Loom.Telemetry" && instrument.Name == name)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            found = true;
            observedTags = tags.ToArray();
        });
        listener.Start();

        LoomMetrics.RecordCounter(name, 1, new MetricTag("route", "/api/health"), new MetricTag("method", "GET"));

        listener.Dispose();

        Assert.True(found, $"Expected a measurement on counter instrument '{name}'.");
        Assert.Contains(observedTags!, t => t.Key == "route" && (string?)t.Value == "/api/health");
        Assert.Contains(observedTags!, t => t.Key == "method" && (string?)t.Value == "GET");
    }

    [Fact]
    public void RecordHistogram_WithTags_PropagatesTagsToTheHistogramInstrument()
    {
        var name = $"test.histogram.tags.{System.Guid.NewGuid():N}";

        var found = false;
        KeyValuePair<string, object?>[]? observedTags = null;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Loom.Telemetry" && instrument.Name == name)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            found = true;
            observedTags = tags.ToArray();
        });
        listener.Start();

        LoomMetrics.RecordHistogram(name, 12.5, new MetricTag("unit", "ms"));

        listener.Dispose();

        Assert.True(found, $"Expected a measurement on histogram instrument '{name}'.");
        Assert.Contains(observedTags!, t => t.Key == "unit" && (string?)t.Value == "ms");
    }

    [Fact]
    public void RecordGauge_WithTags_PropagatesTagsToTheGaugeInstrument()
    {
        var name = $"test.gauge.tags.{System.Guid.NewGuid():N}";

        LoomMetrics.RecordGauge(name, 3, new MetricTag("queue", "inbound"));

        var found = false;
        KeyValuePair<string, object?>[]? observedTags = null;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Loom.Telemetry" && instrument.Name == name)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            found = true;
            observedTags = tags.ToArray();
        });

        listener.Start();
        listener.RecordObservableInstruments();
        listener.Dispose();

        Assert.True(found, $"Expected a measurement on gauge instrument '{name}'.");
        Assert.Contains(observedTags!, t => t.Key == "queue" && (string?)t.Value == "inbound");
    }
}
