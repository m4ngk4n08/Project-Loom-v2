using Loom.DevTools.Rendering;
using Loom.DevTools.Services;
using Loom.Storage;
using Loom.Telemetry;
using Spectre.Console;

namespace Loom.DevTools.Commands;

/// <summary>
/// loom logs <pid> [--count N] [--category X] [--seconds N] — Show recent captured logs.
/// </summary>
public static class LogsCommand
{
    public static async Task RunAsync(int pid, string[] rest, CancellationToken ct)
    {
        var (count, category, seconds) = ParseArgs(rest);

        AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Dim)}]Collecting logs from PID {pid} ({seconds}s)...[/]");

        var metricStore = new InMemoryMetricStore();
        var logStore = new InMemoryLogStore();
        using var collector = new EventPipeCollector(pid, metricStore, logStore);
        await collector.CollectForAsync(TimeSpan.FromSeconds(seconds), ct);

        var records = category is null
            ? logStore.ReadRecent(count)
            : logStore.ReadRecent(category, count);

        if (records.Length == 0)
        {
            AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Warn)}]No logs captured in {seconds}s.[/]");
            var categories = logStore.GetCategories();
            if (categories.Count > 0)
            {
                var seen = categories.Take(10).Select(Markup.Escape);
                AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Dim)}]Categories seen: {string.Join(", ", seen)}[/]");
            }
            return;
        }

        var table = new Table { Border = TableBorder.None };
        table.AddColumn("Time");
        table.AddColumn("Level");
        table.AddColumn("Category");
        table.AddColumn("Message");

        foreach (var record in records)
        {
            var levelColor = Hex(LevelColor(record.Level));
            table.AddRow(
                Markup.Escape(record.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff")),
                $"[{levelColor}]{Markup.Escape(record.Level.ToString())}[/]",
                Markup.Escape(record.Category),
                Markup.Escape(record.Message));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[{Hex(LoomTheme.Dim)}]{records.Length} entr{(records.Length == 1 ? "y" : "ies")}[/]");
    }

    private static (int Count, string? Category, int Seconds) ParseArgs(string[] rest)
    {
        var count = 100;
        string? category = null;
        var seconds = 5;

        for (var i = 0; i < rest.Length; i++)
        {
            switch (rest[i])
            {
                case "--count" when i + 1 < rest.Length && int.TryParse(rest[i + 1], out var c):
                    count = Math.Clamp(c, 1, 1000);
                    i++;
                    break;
                case "--category" when i + 1 < rest.Length:
                    category = rest[i + 1];
                    i++;
                    break;
                case "--seconds" when i + 1 < rest.Length && int.TryParse(rest[i + 1], out var s):
                    seconds = Math.Clamp(s, 1, 60);
                    i++;
                    break;
            }
        }

        return (count, category, seconds);
    }

    private static Color LevelColor(LoomLogLevel level) => level switch
    {
        LoomLogLevel.Critical or LoomLogLevel.Error => LoomTheme.Critical,
        LoomLogLevel.Warning => LoomTheme.Warn,
        _ => LoomTheme.Dim,
    };

    private static string Hex(Color color) => $"#{color.ToHex()}";
}
