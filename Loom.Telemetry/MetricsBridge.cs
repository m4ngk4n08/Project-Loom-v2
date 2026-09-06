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

    public static void PublishCounter(string name, long increment) =>
        Counters.GetOrAdd(name, n => Meter.CreateCounter<long>(n)).Add(increment);

    public static void PublishHistogram(string name, double value) =>
        Histograms.GetOrAdd(name, n => Meter.CreateHistogram<double>(n)).Record(value);

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
