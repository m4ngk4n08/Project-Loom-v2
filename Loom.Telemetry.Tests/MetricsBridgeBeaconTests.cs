using System.Diagnostics.Metrics;
using Xunit;

namespace Loom.Telemetry.Tests;

/// <summary>
/// Black-box test of the discovery beacon added in MetricsBridge. MetricsBridge is
/// internal and there is no InternalsVisibleTo, so this uses MeterListener — public BCL
/// API and a miniature of what loom dev/dashboard's EventPipe probe actually does.
///
/// This proves the instrument exists and reports a value. It does NOT prove the timing
/// half of the fix (that the instrument exists before any Record* call) — xUnit runs
/// many tests in this process and other tests call Record* before this one runs, so
/// there is no idle process to observe here. That half is a separate, out-of-process
/// measurement.
/// </summary>
public sealed class MetricsBridgeBeaconTests
{
    [Fact]
    public void UpGauge_IsPublishedOnTheLoomTelemetryMeter_AndReportsAValue()
    {
        // Force the Loom.Telemetry assembly to load: with --filter isolating this test,
        // nothing else in the run has referenced it yet, and CLR assembly loading is
        // lazy, so without this the module initializer under test hasn't run and there
        // is nothing yet to observe.
        _ = LoomMetrics.GetBufferCapacity();

        var found = false;
        int? observedValue = null;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Loom.Telemetry" && instrument.Name == "loom.telemetry.up")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
        {
            found = true;
            observedValue = measurement;
        });

        listener.Start();
        listener.RecordObservableInstruments();
        listener.Dispose();

        Assert.True(found, "Expected an instrument named 'loom.telemetry.up' on meter 'Loom.Telemetry'.");
        Assert.Equal(1, observedValue);
    }
}
