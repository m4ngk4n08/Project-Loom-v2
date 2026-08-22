using Loom.Storage;

namespace Loom.DevTools.Rendering;

public readonly record struct HotpathEntry(string Name, double AverageMs, double P99Ms, long SampleCount);

/// <summary>
/// Ranks instrumented-method metrics by average duration. A pure function over
/// IMetricStore so it can be used by both the static and live-refreshing CLI
/// commands without re-collecting anything.
/// </summary>
public static class HotpathRanker
{
    /// <summary>
    /// Returns the top <paramref name="top"/> instrumented methods by average value,
    /// each read from up to <paramref name="sampleWindow"/> of its most recent records.
    /// Ties break by name so results are deterministic; fewer than <paramref name="top"/>
    /// instrumented methods just returns fewer entries.
    /// </summary>
    public static IReadOnlyList<HotpathEntry> Rank(IMetricStore store, int top = 3, int sampleWindow = 1000)
    {
        var entries = new List<HotpathEntry>();

        foreach (var name in store.GetMetricNames())
        {
            if (!IsInstrumentedMethod(name)) continue;

            var records = store.ReadRecent(name, sampleWindow);
            if (records.Length == 0) continue;

            var values = new double[records.Length];
            for (var i = 0; i < records.Length; i++) values[i] = records[i].Value;
            Array.Sort(values);

            double sum = 0;
            foreach (var v in values) sum += v;
            var avg = sum / values.Length;

            var p99Index = Math.Min(values.Length - 1, (int)(values.Length * 0.99));
            var p99 = values[p99Index];

            entries.Add(new HotpathEntry(name, avg, p99, values.Length));
        }

        return entries
            .OrderByDescending(e => e.AverageMs)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .Take(top)
            .ToArray();
    }

    /// <summary>
    /// Same heuristic as MetricsService.IsInstrumentedMethod (Loom.Web.Api/Services/
    /// MetricsService.cs:129-137) so the CLI and API agree on what counts as a hotpath.
    /// </summary>
    private static bool IsInstrumentedMethod(string name) =>
        name.Contains("latency", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("duration", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("elapsed", StringComparison.OrdinalIgnoreCase) ||
        name.Contains(".time", StringComparison.OrdinalIgnoreCase);
}
