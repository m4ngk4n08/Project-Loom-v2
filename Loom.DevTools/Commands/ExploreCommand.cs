using Loom.DevTools.Services;
using Loom.Storage;

namespace Loom.DevTools.Commands;

/// <summary>
/// loom explore <pid> — List all metric names and their latest values.
/// </summary>
public static class ExploreCommand
{
    public static async Task RunAsync(int pid, CancellationToken ct)
    {
        Console.WriteLine($"Exploring metrics from PID {pid}...");
        Console.WriteLine("Collecting for 3 seconds...\n");

        var store = new InMemoryMetricStore();
        using var collector = new EventPipeCollector(pid, store);
        await collector.CollectForAsync(TimeSpan.FromSeconds(3), ct);

        var names = store.GetMetricNames();
        if (names.Count == 0)
        {
            Console.WriteLine("No metrics found. Is the process Loom-instrumented?");
            return;
        }

        Console.WriteLine($"{"Metric",-40} {"Type",-12} {"Latest Value",-15} {"Samples"}");
        Console.WriteLine(new string('─', 80));

        foreach (var name in names.OrderBy(n => n))
        {
            var records = store.ReadRecent(name, 100);
            if (records.Length == 0) continue;

            var latest = records[0];
            Console.WriteLine($"{name,-40} {latest.Type,-12} {latest.Value,-15:F2} {records.Length}");
        }

        Console.WriteLine($"\n{names.Count} metric(s) found.");
    }
}
