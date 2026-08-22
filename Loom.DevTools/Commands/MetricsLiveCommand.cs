using Loom.DevTools.Rendering;
using Loom.DevTools.Services;
using Loom.Storage;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Text;

namespace Loom.DevTools.Commands;

/// <summary>
/// loom metrics <pid> --live — a 4Hz refreshing terminal dashboard.
/// Opt-in only: refuses to run against redirected output, since the bare
/// `loom metrics <pid>` command must stay scriptable and pipe-friendly.
/// </summary>
public static class MetricsLiveCommand
{
    // Collector's RefreshInterval is 1s; repainting faster than that just burns
    // CPU redrawing identical data, so 4Hz (250ms) is the render cadence, not the
    // sample cadence.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(250);

    // Rolling window per sparkline series - an unbounded buffer on a long-lived
    // live session is the CLI analogue of the WebSocket leak fixed earlier.
    private const int SeriesCapacity = 60;
    private const int SparkWidth = 12;
    private const int NarrowThreshold = 80;

    public static async Task RunAsync(int pid, CancellationToken ct)
    {
        if (Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("loom metrics --live requires an interactive terminal (output is redirected).");
            Console.Error.WriteLine("Use 'loom metrics <pid>' for scriptable, non-live output.");
            return;
        }

        var processName = GetProcessName(pid);

        var store = new InMemoryMetricStore();
        using var collector = new EventPipeCollector(pid, store);
        collector.Start(ct);

        var cpuSeries = new RollingSeries(SeriesCapacity);
        var heapSeries = new RollingSeries(SeriesCapacity);
        var category = "all";
        var quit = false;

        try
        {
            var initial = BuildRenderable(pid, processName, category, cpuSeries, heapSeries, store);
            await AnsiConsole.Live(initial).StartAsync(async ctx =>
            {
                while (!ct.IsCancellationRequested && !quit)
                {
                    var cpu = LatestValue(store, "cpu-usage");
                    var heap = LatestValue(store, "gc-heap-size");
                    cpuSeries.Add(cpu);
                    heapSeries.Add(heap);

                    ctx.UpdateTarget(BuildRenderable(pid, processName, category, cpuSeries, heapSeries, store));
                    ctx.Refresh();

                    if (SafeKeyAvailable())
                    {
                        var key = Console.ReadKey(intercept: true);
                        switch (key.Key)
                        {
                            case ConsoleKey.Q:
                                quit = true;
                                break;
                            case ConsoleKey.C:
                                category = "cpu";
                                break;
                            case ConsoleKey.M:
                                category = "memory";
                                break;
                            case ConsoleKey.T:
                                category = "thread";
                                break;
                        }
                    }

                    if (quit) break;

                    await Task.Delay(RefreshInterval, ct);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Expected on Ctrl+C or 'q'.
        }
        finally
        {
            // Stop the collector's background write loop before disposing the store it
            // writes into - disposing first left a window where a late Write() landed on
            // a disposed store (harmless today since Dispose() only completes subscriber
            // channels, but the ordering was backwards).
            collector.Stop();
            store.Dispose();
        }
    }

    private static IRenderable BuildRenderable(
        int pid, string processName, string category,
        RollingSeries cpuSeries, RollingSeries heapSeries, IMetricStore store)
    {
        var width = SafeWindowWidth();
        var narrow = width < NarrowThreshold;

        var threads = LatestValue(store, "threadpool-thread-count");
        var gen0 = LatestValue(store, "gen-0-collection-count");
        var gen1 = LatestValue(store, "gen-1-collection-count");
        var gen2 = LatestValue(store, "gen-2-collection-count");

        var accent = HexTag(LoomTheme.Accent);
        var dim = HexTag(LoomTheme.Dim);

        var header = new Markup(
            $"[{accent} bold]LOOM[/]  [{accent}]● live[/]   PID {pid}  {Markup.Escape(processName)}   [{dim}]{DateTime.Now:T}[/]");

        var ruleLine = new Markup($"[{dim}]{new string('─', Math.Min(width, 60))}[/]");

        var metricsGrid = new Grid();
        metricsGrid.AddColumn();
        metricsGrid.AddColumn();
        if (!narrow) metricsGrid.AddColumn();
        metricsGrid.AddColumn();
        metricsGrid.AddColumn();

        metricsGrid.AddRow(BuildMetricRow(
            "CPU", UnitFormatter.Format("cpu-usage", cpuSeries.Latest), cpuSeries, narrow,
            "Threads", threads.ToString("F0")));
        metricsGrid.AddRow(BuildMetricRow(
            "Heap", UnitFormatter.Format("gc-heap-size", heapSeries.Latest), heapSeries, narrow,
            "GC (0/1/2)", $"{gen0:F0}/{gen1:F0}/{gen2:F0}"));

        var (lowerHeaderText, lowerBody) = BuildLowerSection(category, store, narrow);
        var lowerHeader = new Markup($"[{accent} bold]{lowerHeaderText}[/]");

        var footer = new Markup(
            $"[{dim}]q[/] quit   [{dim}]c[/] cpu   [{dim}]m[/] memory   [{dim}]t[/] threads");

        var rows = new List<IRenderable>
        {
            header,
            ruleLine,
            metricsGrid,
            new Text(string.Empty),
            lowerHeader,
            ruleLine,
            lowerBody,
            new Text(string.Empty),
            footer,
        };

        return new Rows(rows);
    }

    /// <summary>
    /// The panel below the CPU/heap gauges. "all"/"cpu" show top hotpaths (already
    /// duration-named, so there's nothing further to filter for "cpu" specifically);
    /// "memory"/"thread" switch to a metrics table for that category, using the same
    /// MetricCategoryFilter predicates as the static `metrics &lt;pid&gt; memory|thread`
    /// command so the two views agree. Each category shows its own explicit empty state
    /// rather than silently falling back to a different category's data.
    /// </summary>
    private static (string Header, IRenderable Body) BuildLowerSection(string category, IMetricStore store, bool narrow)
    {
        switch (category)
        {
            case "memory":
            {
                var names = store.GetMetricNames().Where(MetricCategoryFilter.IsMemory)
                    .OrderBy(n => n, StringComparer.Ordinal).ToList();
                return ("MEMORY METRICS", BuildMetricListTable(store, names, "memory", narrow));
            }
            case "thread":
            {
                var names = store.GetMetricNames().Where(MetricCategoryFilter.IsThread)
                    .OrderBy(n => n, StringComparer.Ordinal).ToList();
                return ("THREAD METRICS", BuildMetricListTable(store, names, "thread", narrow));
            }
            default:
            {
                var hotpaths = HotpathRanker.Rank(store, top: 3);
                // "cpu" and "all" show the same data (hotpaths are already duration-named,
                // so there's nothing left to filter for "cpu" specifically) - but the
                // header still needs to change on 'c', or the key looks inert even though
                // it correctly reset the category away from memory/thread.
                var header = category == "cpu" ? "TOP HOTPATHS (cpu)" : "TOP HOTPATHS";
                return (header, BuildHotpathTable(hotpaths, narrow));
            }
        }
    }

    private static IRenderable[] BuildMetricRow(
        string label1, string value1, RollingSeries series1, bool narrow,
        string label2, string value2)
    {
        var dim = HexTag(LoomTheme.Dim);
        var cells = new List<IRenderable>
        {
            new Markup($"[{dim}]{label1}[/]"),
            new Markup(value1.PadLeft(8)),
        };
        if (!narrow)
        {
            var spark = Sparkline.Render(series1.ToOrderedArray(), SparkWidth);
            cells.Add(new Markup($"[{HexTag(LoomTheme.Accent)}]{Markup.Escape(spark)}[/]"));
        }
        cells.Add(new Markup($"[{dim}]{label2}[/]"));
        cells.Add(new Markup(value2.PadLeft(6)));
        return cells.ToArray();
    }

    private static IRenderable BuildHotpathTable(IReadOnlyList<HotpathEntry> hotpaths, bool narrow)
    {
        if (hotpaths.Count == 0)
            return new Markup($"[{HexTag(LoomTheme.Dim)}](no instrumented methods observed yet)[/]");

        var maxAvg = hotpaths.Max(h => h.AverageMs);
        var grid = new Grid();
        grid.AddColumn();
        if (!narrow) grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();

        foreach (var h in hotpaths)
        {
            var cells = new List<IRenderable>();
            if (!narrow)
            {
                var barLen = maxAvg > 0 ? Math.Max(1, (int)Math.Round(h.AverageMs / maxAvg * 8)) : 1;
                var bar = new string('▇', barLen);
                cells.Add(new Markup($"[{HexTag(LoomTheme.Accent)}]{bar}[/]"));
            }
            cells.Add(new Markup(Markup.Escape(h.Name)));
            cells.Add(new Markup($"{h.AverageMs,8:F1}ms"));
            cells.Add(new Markup($"{h.P99Ms,8:F1}ms"));
            cells.Add(new Markup($"{h.SampleCount,8}"));
            grid.AddRow(cells.ToArray());
        }

        return grid;
    }

    /// <summary>Metrics table for the memory/thread category panels - name, latest value, sparkline.</summary>
    private static IRenderable BuildMetricListTable(IMetricStore store, List<string> names, string categoryLabel, bool narrow)
    {
        if (names.Count == 0)
            return new Markup($"[{HexTag(LoomTheme.Dim)}](no {categoryLabel} metrics observed yet)[/]");

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        if (!narrow) grid.AddColumn();

        foreach (var name in names)
        {
            var records = store.ReadRecent(name, SeriesCapacity);
            if (records.Length == 0) continue;

            var cells = new List<IRenderable>
            {
                new Markup(Markup.Escape(name)),
                new Markup(UnitFormatter.Format(name, records[0].Value).PadLeft(10)),
            };
            if (!narrow)
            {
                var chronological = records.Select(r => r.Value).Reverse().ToArray();
                var spark = Sparkline.Render(chronological, SparkWidth);
                cells.Add(new Markup($"[{HexTag(LoomTheme.Accent)}]{Markup.Escape(spark)}[/]"));
            }
            grid.AddRow(cells.ToArray());
        }

        return grid;
    }

    private static double LatestValue(IMetricStore store, string metricName)
    {
        var records = store.ReadRecent(metricName, 1);
        return records.Length == 0 ? 0 : records[0].Value;
    }

    private static string HexTag(Color color) => $"#{color.ToHex()}";

    private static bool SafeKeyAvailable()
    {
        try { return !Console.IsInputRedirected && Console.KeyAvailable; }
        catch { return false; }
    }

    private static int SafeWindowWidth()
    {
        try { return Console.WindowWidth; }
        catch { return 80; }
    }

    private static string GetProcessName(int pid)
    {
        try { return System.Diagnostics.Process.GetProcessById(pid).ProcessName; }
        catch { return "unknown"; }
    }

    /// <summary>Bounded circular buffer backing a live sparkline series.</summary>
    private sealed class RollingSeries
    {
        private readonly double[] _buffer;
        private int _count;
        private int _head;

        public RollingSeries(int capacity) => _buffer = new double[capacity];

        public double Latest => _count == 0 ? 0 : _buffer[(_head - 1 + _buffer.Length) % _buffer.Length];

        public void Add(double value)
        {
            _buffer[_head] = value;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length) _count++;
        }

        public double[] ToOrderedArray()
        {
            var result = new double[_count];
            var start = _count < _buffer.Length ? 0 : _head;
            for (var i = 0; i < _count; i++)
                result[i] = _buffer[(start + i) % _buffer.Length];
            return result;
        }
    }
}
