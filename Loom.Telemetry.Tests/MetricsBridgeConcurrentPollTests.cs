using System;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Loom.Telemetry.Tests;

/// <summary>
/// MetricsBridge's ObservableGauge callbacks (buffer-dropped and per-gauge) used to size
/// a Measurement[] array from the backing ConcurrentDictionary's .Count and then enumerate
/// the live dictionary into it - if the dictionary grows between the Count read and the
/// enumeration (a concurrent RecordGauge call adding a brand-new tag combination), the
/// enumeration writes past the array's end. This races a poller against a writer adding
/// fresh tag combinations to try to reproduce that.
/// </summary>
public sealed class MetricsBridgeConcurrentPollTests
{
    [Fact]
    public void PollingObservableInstruments_WhileGaugeDictionaryGrows_DoesNotThrow()
    {
        var name = $"test.gauge.concurrentpoll.{Guid.NewGuid():N}";
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Loom.Telemetry" && instrument.Name == name)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) => { });
        listener.Start();

        Exception? pollException = null;
        Exception? writeException = null;
        var stop = 0;

        var pollThread = new Thread(() =>
        {
            try
            {
                while (Volatile.Read(ref stop) == 0)
                {
                    listener.RecordObservableInstruments();
                }
            }
            catch (Exception ex)
            {
                pollException = ex;
            }
        });

        var writeThread = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < 2000; i++)
                {
                    // Fresh tag value each time - new tag combination, new dictionary entry.
                    LoomMetrics.RecordGauge(name, i, new MetricTag("instance", i.ToString()));
                }
            }
            catch (Exception ex)
            {
                writeException = ex;
            }
        });

        pollThread.Start();
        writeThread.Start();
        writeThread.Join();
        Volatile.Write(ref stop, 1);
        pollThread.Join();

        listener.Dispose();

        Assert.Null(writeException);
        Assert.Null(pollException);
    }
}
