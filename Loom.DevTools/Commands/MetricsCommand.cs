using Loom.DevTools.Rendering;
using Loom.DevTools.Services;
using Loom.Storage;
using Spectre.Console;

namespace Loom.DevTools.Commands;

/// <summary>
/// loom metrics <pid> [cpu|memory|thread] — Show formatted metrics summary.
/// </summary>
public static class MetricsCommand
{
    private const int SparkWidth = 10;

    public static async Task RunAsync(int pid, string? category, CancellationToken ct)
    {
        AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Dim)}]Collecting metrics from PID {pid} (3s)...[/]");

        var store = new InMemoryMetricStore();
        using var collector = new EventPipeCollector(pid, store);
        await collector.CollectForAsync(TimeSpan.FromSeconds(3), ct);

        var names = store.GetMetricNames();
        if (names.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Warn)}]No metrics found.[/]");
            return;
        }

        switch (category?.ToLowerInvariant())
        {
            case "cpu":
                PrintFilteredMetrics(store, "CPU", names.Where(MetricCategoryFilter.IsCpu).ToList());
                break;
            case "memory":
                PrintFilteredMetrics(store, "Memory", names.Where(MetricCategoryFilter.IsMemory).ToList());
                break;
            case "thread":
                PrintFilteredMetrics(store, "Thread", names.Where(MetricCategoryFilter.IsThread).ToList());
                break;
            default:
                PrintAllMetrics(store, names);
                break;
        }
    }

    private static void PrintAllMetrics(IMetricStore store, IReadOnlyCollection<string> names)
    {
        var table = new Table { Border = TableBorder.None };
        table.AddColumn("Metric");
        table.AddColumn("Type");
        table.AddColumn(new TableColumn("Count") { Alignment = Justify.Right });
        table.AddColumn("Unit");
        table.AddColumn(new TableColumn("Avg") { Alignment = Justify.Right });
        table.AddColumn(new TableColumn("Min") { Alignment = Justify.Right });
        table.AddColumn(new TableColumn("Max") { Alignment = Justify.Right });
        table.AddColumn("Trend");

        foreach (var name in names.OrderBy(n => n, StringComparer.Ordinal))
        {
            var records = store.ReadRecent(name, 1000);
            if (records.Length == 0) continue;

            var values = records.Select(r => r.Value).ToArray();

            table.AddRow(
                Markup.Escape(name),
                records[0].Type.ToString(),
                records.Length.ToString(),
                UnitFormatter.InferUnit(name),
                UnitFormatter.Format(name, values.Average()),
                UnitFormatter.Format(name, values.Min()),
                UnitFormatter.Format(name, values.Max()),
                Trend(values));
        }

        AnsiConsole.Write(table);
    }

    private static void PrintFilteredMetrics(IMetricStore store, string title, List<string> names)
    {
        AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Accent)} bold]═══ {title} Metrics ═══[/]");

        if (names.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Warn)}]No {title.ToLowerInvariant()}-related metrics found.[/]");
            var available = store.GetMetricNames().Take(5).Select(Markup.Escape);
            AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Dim)}]Available: {string.Join(", ", available)}[/]");
            return;
        }

        var table = new Table { Border = TableBorder.None };
        table.AddColumn("Metric");
        table.AddColumn(new TableColumn("Samples") { Alignment = Justify.Right });
        table.AddColumn(new TableColumn("Avg") { Alignment = Justify.Right });
        table.AddColumn(new TableColumn("P99") { Alignment = Justify.Right });
        table.AddColumn("Trend");

        foreach (var name in names.OrderBy(n => n, StringComparer.Ordinal))
        {
            var records = store.ReadRecent(name, 1000);
            if (records.Length == 0) continue;

            var values = records.Select(r => r.Value).OrderBy(v => v).ToArray();
            var avg = values.Average();
            var p99 = values[(int)(values.Length * 0.99)];

            table.AddRow(
                Markup.Escape(name),
                records.Length.ToString(),
                UnitFormatter.Format(name, avg),
                UnitFormatter.Format(name, p99),
                Trend(records.Select(r => r.Value).ToArray()));
        }

        AnsiConsole.Write(table);
    }

    /// <summary>Chronological (oldest-first) sparkline, blank when there's not enough history to show a trend.</summary>
    private static string Trend(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return string.Empty;

        var chronological = new double[values.Count];
        for (var i = 0; i < values.Count; i++) chronological[i] = values[values.Count - 1 - i];

        return $"[{Hex(LoomTheme.Accent)}]{Markup.Escape(Sparkline.Render(chronological, SparkWidth))}[/]";
    }

    private static string Hex(Color color) => $"#{color.ToHex()}";
}
