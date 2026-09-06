using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace Loom.Telemetry;

/// <summary>
/// Republishes LoomRuntime's ring-buffer writes through System.Diagnostics.Metrics so
/// any EventPipe-aware tool (dotnet-counters, Loom.DevTools, or a generic APM agent)
/// can observe them — without those tools needing to know anything about ring buffers,
/// tag interning, or Loom-specific wire formats.
/// </summary>
internal static class MetricsBridge
{
    private static readonly Meter Meter = new("Loom.Telemetry", "1.0.0");

    // Discovery (loom dev / loom dashboard) has nothing to observe until an instrument
    // exists on this meter: Counters/Histograms below are created lazily on first
    // Record*, so a running, correctly-instrumented process that hasn't recorded
    // anything yet is indistinguishable from one that never referenced Loom at all. This
    // ObservableGauge is polled by the runtime on every RefreshInterval regardless of
    // whether the app records anything, so the meter always has something to report.
    // It looks like dead code (constant value) — it is not. Deleting it silently
    // restores the "not Loom-instrumented" false negative against an idle app.
    private static readonly ObservableGauge<int> Up =
        Meter.CreateObservableGauge("loom.telemetry.up", () => 1);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Counter<long>> Counters = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Histogram<double>> Histograms = new();

    // Gauges are a point-in-time level, not a distribution — they cannot be pushed
    // through Counter<T>/Histogram<T>. Meter.CreateObservableGauge<T> only offers a pull
    // callback, so PublishGauge writes a last-value cache (and the tags that came with
    // it) and the ObservableGauge, created lazily per metric name, reads that cache when
    // polled.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, GaugeState> Gauges = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ObservableGauge<double>> GaugeInstruments = new();

    private sealed class GaugeState
    {
        public double Value;
        public KeyValuePair<string, object?>[] Tags = Array.Empty<KeyValuePair<string, object?>>();
    }

    public static void PublishCounter(string name, long increment, ReadOnlySpan<MetricTag> tags = default) =>
        Counters.GetOrAdd(name, n => Meter.CreateCounter<long>(n)).Add(increment, ConvertTags(tags));

    public static void PublishHistogram(string name, double value, ReadOnlySpan<MetricTag> tags = default) =>
        Histograms.GetOrAdd(name, n => Meter.CreateHistogram<double>(n)).Record(value, ConvertTags(tags));

    public static void PublishGauge(string name, double value, ReadOnlySpan<MetricTag> tags = default)
    {
        var state = Gauges.GetOrAdd(name, static _ => new GaugeState());
        state.Value = value;
        state.Tags = ConvertTags(tags);

        GaugeInstruments.GetOrAdd(name, n => Meter.CreateObservableGauge(n, () =>
        {
            var s = Gauges[n];
            return new Measurement<double>(s.Value, s.Tags);
        }));
    }

    private static KeyValuePair<string, object?>[] ConvertTags(ReadOnlySpan<MetricTag> tags)
    {
        if (tags.Length == 0)
            return Array.Empty<KeyValuePair<string, object?>>();

        var converted = new KeyValuePair<string, object?>[tags.Length];
        for (var i = 0; i < tags.Length; i++)
            converted[i] = new KeyValuePair<string, object?>(tags[i].Key, tags[i].Value);
        return converted;
    }

    // Placed here, next to the static fields it forces into existence, rather than on
    // AddLoomTelemetry: the DI extension is never called by consumers that use only the
    // static LoomMetrics API (confirmed against HIM, the app that surfaced this bug),
    // so hanging the fix there would fix nothing for them. A module initializer runs on
    // assembly load, which the CLR triggers the first time any type from this assembly
    // is needed — that is earlier than "first Record* call" but it is NOT guaranteed to
    // be process start. An app that references Loom.Telemetry only through a type used
    // late (or conditionally) will not have its meter beacon up until that point. This
    // closes the common case, not every case.
    // CA2255 warns that ModuleInitializer is unusual for library code; here it is the
    // point — see the comment above.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Touching any static member of this class forces its static constructor —
        // and therefore the Up gauge's field initializer — to run now.
        _ = Meter;
    }
#pragma warning restore CA2255
}
