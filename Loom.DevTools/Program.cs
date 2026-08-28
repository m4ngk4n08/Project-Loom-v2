using Loom.DevTools.Commands;

// Windows terminals (cmd.exe, older PowerShell hosts) default to a legacy OEM
// codepage that can't render the block-drawing sparkline glyphs or the "●" live
// indicator - they show up as "?". Forcing UTF-8 output fixes that; redirected
// output has no console to configure, so this is a no-op there.
if (!Console.IsOutputRedirected)
{
    try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

switch (args)
{
    case ["dev"]:
        await DevCommand.RunAsync(showAll: false, cts.Token);
        break;
    case ["dev", "--all"]:
        await DevCommand.RunAsync(showAll: true, cts.Token);
        break;
    case ["dev", "--dashboard"]:
        await DashboardCommand.RunAsync(cts.Token);
        break;
    case ["dev", "--dashboard", var dashPid] when int.TryParse(dashPid, out var dpid):
        await DashboardCommand.RunAsync(dpid, cts.Token);
        break;
    case ["watch", var pidArg] when int.TryParse(pidArg, out var watchPid):
        await WatchCommand.RunAsync(watchPid, raw: false, cts.Token);
        break;
    case ["watch", var pidArg, "--raw"] when int.TryParse(pidArg, out var watchRawPid):
        await WatchCommand.RunAsync(watchRawPid, raw: true, cts.Token);
        break;
    case ["explore", var pidArg] when int.TryParse(pidArg, out var explorePid):
        await ExploreCommand.RunAsync(explorePid, cts.Token);
        break;
    case ["query", var pidArg, ..] when int.TryParse(pidArg, out var queryPid) && args.Length >= 3:
        await QueryCommand.RunAsync(queryPid, string.Join(" ", args[2..]), cts.Token);
        break;
    case ["logs", var pidArg, ..] when int.TryParse(pidArg, out var logsPid):
        await LogsCommand.RunAsync(logsPid, args[2..], cts.Token);
        break;
    case ["search", var pidArg, var queryArg, ..] when int.TryParse(pidArg, out var searchPid):
        await SearchCommand.RunAsync(searchPid, queryArg, args[3..], cts.Token);
        break;
    case ["metrics", var pidArg] when int.TryParse(pidArg, out var metricsPid):
        await MetricsCommand.RunAsync(metricsPid, null, cts.Token);
        break;
    case ["metrics", var pidArg, "--live"] when int.TryParse(pidArg, out var livePid):
        await MetricsLiveCommand.RunAsync(livePid, cts.Token);
        break;
    case ["metrics", var pidArg, var category] when int.TryParse(pidArg, out var metricsPid2):
        await MetricsCommand.RunAsync(metricsPid2, category, cts.Token);
        break;
    default:
        Console.WriteLine("Usage:");
        Console.WriteLine("  loom dev [--all]                        Discover Loom-instrumented processes");
        Console.WriteLine("  loom dev --dashboard                    Launch dashboard (requires Loom.Dashboard)");
        Console.WriteLine("  loom watch <pid> [--raw]                Stream formatted metric events (--raw for unformatted payload dump)");
        Console.WriteLine("  loom explore <pid>                      List all metrics and latest values");
        Console.WriteLine("  loom metrics <pid> [cpu|memory|thread]  Show formatted metrics");
        Console.WriteLine("  loom metrics <pid> --live               Live-refreshing terminal dashboard (requires an interactive terminal)");
        Console.WriteLine("  loom query <pid> \"SELECT...\"            Execute LoomQL query");
        Console.WriteLine("  loom logs <pid> [--count N] [--category X] [--seconds N]   Show recent captured logs");
        Console.WriteLine("  loom search <pid> \"<query>\" [--max N] [--seconds N]        BM25 search over captured logs");
        break;
}
