using Loom.DevTools.Rendering;
using Loom.DevTools.Services;
using Loom.Storage;
using Spectre.Console;

namespace Loom.DevTools.Commands;

/// <summary>
/// loom explore <pid> — List all metric names and their latest values.
/// </summary>
public static class ExploreCommand
{
    public static async Task RunAsync(int pid, CancellationToken ct)
    {
        AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Dim)}]Exploring metrics from PID {pid} (3s)...[/]");

        var store = new InMemoryMetricStore();
        using var collector = new EventPipeCollector(pid, store);
        await collector.CollectForAsync(TimeSpan.FromSeconds(3), ct);

        var names = store.GetMetricNames();
        if (names.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Warn)}]No metrics found. Is the process Loom-instrumented?[/]");
            return;
        }

        var table = new Table { Border = TableBorder.None };
        table.AddColumn("Metric");
        table.AddColumn("Type");
        table.AddColumn(new TableColumn("Latest") { Alignment = Justify.Right });
        table.AddColumn("Unit");
        table.AddColumn(new TableColumn("Samples") { Alignment = Justify.Right });

        foreach (var name in names.OrderBy(n => n, StringComparer.Ordinal))
        {
            var records = store.ReadRecent(name, 100);
            if (records.Length == 0) continue;

            var latest = records[0];
            table.AddRow(
                Markup.Escape(name),
                latest.Type.ToString(),
                UnitFormatter.Format(name, latest.Value),
                UnitFormatter.InferUnit(name),
                records.Length.ToString());
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Dim)}]{names.Count} metric(s) found.[/]");
    }

    private static string Hex(Color color) => $"#{color.ToHex()}";
}
