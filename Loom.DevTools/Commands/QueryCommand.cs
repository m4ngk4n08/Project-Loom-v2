using Loom.DevTools.Services;
using Loom.Storage;
using Loom.Telemetry.Query;

namespace Loom.DevTools.Commands;

/// <summary>
/// loom query <pid> "SELECT ..." — Execute LoomQL against live metrics.
/// </summary>
public static class QueryCommand
{
    public static async Task RunAsync(int pid, string query, CancellationToken ct)
    {
        Console.WriteLine($"Collecting metrics from PID {pid} (3s)...\n");

        var store = new InMemoryMetricStore();
        using var collector = new EventPipeCollector(pid, store);
        await collector.CollectForAsync(TimeSpan.FromSeconds(3), ct);

        var executor = new QueryExecutor(store);

        try
        {
            var result = await executor.ExecuteAsync(query, ct);

            if (result.Rows.Count == 0)
            {
                Console.WriteLine("No results.");
                var names = store.GetMetricNames();
                if (names.Count > 0)
                {
                    Console.WriteLine($"\nAvailable metrics: {string.Join(", ", names.Take(10))}");
                }
                return;
            }

            // Print header
            var colWidths = result.Columns.Select(c => Math.Max(c.Length, 15)).ToArray();
            for (int i = 0; i < result.Columns.Count; i++)
                Console.Write($"{result.Columns[i].PadRight(colWidths[i])} ");
            Console.WriteLine();
            Console.WriteLine(new string('─', colWidths.Sum() + colWidths.Length));

            // Print rows
            foreach (var row in result.Rows)
            {
                for (int i = 0; i < row.Values.Count; i++)
                {
                    var val = row.Values[i];
                    var display = val.Number.HasValue ? val.Number.Value.ToString("F2") : (val.Text ?? "");
                    Console.Write($"{display.PadRight(colWidths[i])} ");
                }
                Console.WriteLine();
            }

            Console.WriteLine($"\n{result.Rows.Count} row(s) in {result.ExecutionTimeMs:F1}ms");
        }
        catch (QuerySyntaxException ex)
        {
            Console.WriteLine($"Query error: {ex.Message}");
        }
    }
}
