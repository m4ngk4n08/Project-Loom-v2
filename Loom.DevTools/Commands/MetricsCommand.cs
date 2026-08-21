using Loom.DevTools.Services;
using Loom.Storage;
using Loom.Telemetry;

namespace Loom.DevTools.Commands;

/// <summary>
/// loom metrics <pid> [cpu|memory|thread] — Show formatted metrics summary.
/// </summary>
public static class MetricsCommand
{
    public static async Task RunAsync(int pid, string? category, CancellationToken ct)
    {
        Console.WriteLine($"Collecting metrics from PID {pid} (3s)...\n");

        var store = new InMemoryMetricStore();
        using var collector = new EventPipeCollector(pid, store);
        await collector.CollectForAsync(TimeSpan.FromSeconds(3), ct);

        var names = store.GetMetricNames();
        if (names.Count == 0)
        {
            Console.WriteLine("No metrics found.");
            return;
        }

        switch (category?.ToLowerInvariant())
        {
            case "cpu":
                PrintCpuMetrics(store, names);
                break;
            case "memory":
                PrintMemoryMetrics(store, names);
                break;
            case "thread":
                PrintThreadMetrics(store, names);
                break;
            default:
                PrintAllMetrics(store, names);
                break;
        }
    }

    private static void PrintAllMetrics(IMetricStore store, IReadOnlyCollection<string> names)
    {
        Console.WriteLine($"{"Metric",-40} {"Type",-12} {"Count",-8} {"Unit",-8} {"Avg",-12} {"Min",-12} {"Max",-12}");
        Console.WriteLine(new string('─', 104));

        foreach (var name in names.OrderBy(n => n))
        {
            var records = store.ReadRecent(name, 1000);
            if (records.Length == 0) continue;

            var values = records.Select(r => r.Value).ToArray();
            Console.WriteLine($"{Truncate(name, 39),-40} {records[0].Type,-12} {records.Length,-8} {InferUnit(name),-8} {values.Average(),-12:F2} {values.Min(),-12:F2} {values.Max(),-12:F2}");
        }
    }

    /// <summary>
    /// Infer a human-readable unit from the metric name so mixed-unit tables
    /// (durations, currency, item counts) are unambiguous at a glance.
    /// </summary>
    private static string InferUnit(string name)
    {
        if (name.Contains("cpu-usage", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("usage", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("percent", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("%", StringComparison.OrdinalIgnoreCase))
            return "%";

        if (name.Contains("alloc-rate", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("gc-time", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("lock-contention", StringComparison.OrdinalIgnoreCase))
            return "/s";

        if (name.Contains("cpu-usage", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("time-in-gc", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("gc-fragmentation", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("usage", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("percent", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("%", StringComparison.OrdinalIgnoreCase))
            return "%";

        if (name.Contains("alloc-rate", StringComparison.OrdinalIgnoreCase))
            return "B/s";

        if (name.Contains("working-set", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("gc-heap-size", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("gc-committed", StringComparison.OrdinalIgnoreCase))
            return "MB";

        if (name.Contains("loh-size", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("poh-size", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("gen-0-size", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("gen-1-size", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("gen-2-size", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("il-bytes-jitted", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("bytes", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("alloc", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("memory", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("heap", StringComparison.OrdinalIgnoreCase))
            return "B";

        if (name.Contains("duration", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("latency", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("elapsed", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("time", StringComparison.OrdinalIgnoreCase))
            return "ms";

        if (name.Contains("amount", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("total", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("price", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("revenue", StringComparison.OrdinalIgnoreCase))
            return "$";

        return "count";
    }

    private static void PrintCpuMetrics(IMetricStore store, IReadOnlyCollection<string> names)
    {
        Console.WriteLine("═══ CPU Metrics ═══\n");
        var cpuNames = names.Where(n => n.Contains("cpu") || n.Contains("elapsed") || n.Contains("duration")).ToList();

        if (cpuNames.Count == 0)
        {
            Console.WriteLine("No CPU-related metrics found.");
            Console.WriteLine($"Available: {string.Join(", ", names.Take(5))}");
            return;
        }

        PrintFilteredMetrics(store, cpuNames);
    }

    private static void PrintMemoryMetrics(IMetricStore store, IReadOnlyCollection<string> names)
    {
        Console.WriteLine("═══ Memory Metrics ═══\n");
        var memNames = names.Where(n => n.Contains("memory") || n.Contains("alloc") || n.Contains("gc") || n.Contains("heap")).ToList();

        if (memNames.Count == 0)
        {
            Console.WriteLine("No memory-related metrics found.");
            Console.WriteLine($"Available: {string.Join(", ", names.Take(5))}");
            return;
        }

        PrintFilteredMetrics(store, memNames);
    }

    private static void PrintThreadMetrics(IMetricStore store, IReadOnlyCollection<string> names)
    {
        Console.WriteLine("═══ Thread Metrics ═══\n");
        var threadNames = names.Where(n => n.Contains("thread") || n.Contains("lock") || n.Contains("contention")).ToList();

        if (threadNames.Count == 0)
        {
            Console.WriteLine("No thread-related metrics found.");
            Console.WriteLine($"Available: {string.Join(", ", names.Take(5))}");
            return;
        }

        PrintFilteredMetrics(store, threadNames);
    }

    private static void PrintFilteredMetrics(IMetricStore store, List<string> names)
    {
        Console.WriteLine($"{"Metric",-40} {"Samples",-10} {"Avg",-12} {"P99",-12}");
        Console.WriteLine(new string('─', 74));

        foreach (var name in names.OrderBy(n => n))
        {
            var records = store.ReadRecent(name, 1000);
            if (records.Length == 0) continue;

            var values = records.Select(r => r.Value).OrderBy(v => v).ToArray();
            var avg = values.Average();
            var p99 = values[(int)(values.Length * 0.99)];

            Console.WriteLine($"{Truncate(name, 39),-40} {records.Length,-10} {avg,-12:F2} {p99,-12:F2}");
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 2)] + "..";
}
