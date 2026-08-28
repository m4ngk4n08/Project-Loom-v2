using System.Diagnostics;
using Loom.DevTools.Rendering;
using Loom.DevTools.Services;
using Loom.Storage;
using Loom.Telemetry;
using Loom.Telemetry.Query;
using Spectre.Console;

namespace Loom.DevTools.Commands;

/// <summary>
/// loom search <pid> "&lt;query&gt;" [--max N] [--seconds N] — BM25 search over captured logs.
/// Mirrors POST /api/logs/search (EndpointExtensions.cs) so the CLI and HTTP API rank
/// identically.
/// </summary>
public static class SearchCommand
{
    public static async Task RunAsync(int pid, string query, string[] rest, CancellationToken ct)
    {
        var (max, seconds) = ParseArgs(rest);

        AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Dim)}]Collecting logs from PID {pid} ({seconds}s)...[/]");

        var metricStore = new InMemoryMetricStore();
        var logStore = new InMemoryLogStore();
        using var collector = new EventPipeCollector(pid, metricStore, logStore);
        await collector.CollectForAsync(TimeSpan.FromSeconds(seconds), ct);

        var corpus = logStore.Query(new LogQueryFilter(null, null, null, null, 10_000));

        var started = Stopwatch.GetTimestamp();
        var results = Bm25LogSearch.Search(corpus, query, max, minScore: 0.0);
        var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        if (results.Length == 0)
        {
            AnsiConsole.MarkupLine(
                $"[{Hex(LoomTheme.Warn)}]No matches for '{Markup.Escape(query)}' in {corpus.Length} captured entr{(corpus.Length == 1 ? "y" : "ies")}.[/]");
            return;
        }

        var table = new Table { Border = TableBorder.None };
        table.AddColumn(new TableColumn("Score") { Alignment = Justify.Right });
        table.AddColumn("Time");
        table.AddColumn("Level");
        table.AddColumn("Source");
        table.AddColumn("Content");

        foreach (var result in results)
        {
            var content = Markup.Escape(result.Content);
            if (result.ExceptionType is not null)
            {
                content += $" [{Hex(LoomTheme.Dim)}]{Markup.Escape(result.ExceptionType)}[/]";
            }

            table.AddRow(
                result.Score.ToString("F2"),
                Markup.Escape(result.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff")),
                Markup.Escape(result.Level),
                Markup.Escape(result.Source),
                content);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Dim)}]{results.Length} result(s) in {elapsedMs:F1}ms[/]");
    }

    private static (int Max, int Seconds) ParseArgs(string[] rest)
    {
        var max = 20;
        var seconds = 5;

        for (var i = 0; i < rest.Length; i++)
        {
            switch (rest[i])
            {
                case "--max" when i + 1 < rest.Length && int.TryParse(rest[i + 1], out var m):
                    max = Math.Clamp(m, 1, 100);
                    i++;
                    break;
                case "--seconds" when i + 1 < rest.Length && int.TryParse(rest[i + 1], out var s):
                    seconds = Math.Clamp(s, 1, 60);
                    i++;
                    break;
            }
        }

        return (max, seconds);
    }

    private static string Hex(Color color) => $"#{color.ToHex()}";
}
