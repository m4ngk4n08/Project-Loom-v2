using Loom.DevTools.Rendering;
using Loom.DevTools.Services;
using Loom.Storage;
using Loom.Telemetry.Query;
using Spectre.Console;

namespace Loom.DevTools.Commands;

/// <summary>
/// loom query <pid> "SELECT ..." — Execute LoomQL against live metrics.
/// </summary>
public static class QueryCommand
{
    public static async Task RunAsync(int pid, string query, CancellationToken ct)
    {
        AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Dim)}]Collecting metrics from PID {pid} (3s)...[/]");

        var store = new InMemoryMetricStore();
        using var collector = new EventPipeCollector(pid, store);
        await collector.CollectForAsync(TimeSpan.FromSeconds(3), ct);

        var executor = new QueryExecutor(store);

        try
        {
            var result = await executor.ExecuteAsync(query, ct);

            if (result.Rows.Count == 0)
            {
                AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Warn)}]No results.[/]");
                var names = store.GetMetricNames();
                if (names.Count > 0)
                {
                    var available = names.Take(10).Select(Markup.Escape);
                    AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Dim)}]Available metrics: {string.Join(", ", available)}[/]");
                }
                return;
            }

            var table = new Table { Border = TableBorder.None };
            foreach (var column in result.Columns)
                table.AddColumn(new TableColumn(Markup.Escape(column)) { Alignment = Justify.Right });

            foreach (var row in result.Rows)
            {
                var cells = row.Values.Select(val =>
                    val.Number.HasValue ? val.Number.Value.ToString("F2") : Markup.Escape(val.Text ?? ""));
                table.AddRow(cells.ToArray());
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Dim)}]{result.Rows.Count} row(s) in {result.ExecutionTimeMs:F1}ms[/]");
        }
        catch (QuerySyntaxException ex)
        {
            AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Critical)}]Query error: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private static string Hex(Color color) => $"#{color.ToHex()}";
}
