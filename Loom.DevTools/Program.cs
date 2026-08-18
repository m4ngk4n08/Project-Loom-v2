using Loom.DevTools.Commands;

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
        await WatchCommand.RunAsync(watchPid, cts.Token);
        break;
    case ["explore", var pidArg] when int.TryParse(pidArg, out var explorePid):
        await ExploreCommand.RunAsync(explorePid, cts.Token);
        break;
    case ["query", var pidArg, ..] when int.TryParse(pidArg, out var queryPid) && args.Length >= 3:
        await QueryCommand.RunAsync(queryPid, string.Join(" ", args[2..]), cts.Token);
        break;
    case ["metrics", var pidArg] when int.TryParse(pidArg, out var metricsPid):
        await MetricsCommand.RunAsync(metricsPid, null, cts.Token);
        break;
    case ["metrics", var pidArg, var category] when int.TryParse(pidArg, out var metricsPid2):
        await MetricsCommand.RunAsync(metricsPid2, category, cts.Token);
        break;
    default:
        Console.WriteLine("Usage:");
        Console.WriteLine("  loom dev [--all]              Discover Loom-instrumented processes");
        Console.WriteLine("  loom dev --dashboard          Launch dashboard (requires Loom.Dashboard)");
        Console.WriteLine("  loom watch <pid>              Stream raw metric events");
        Console.WriteLine("  loom explore <pid>            List all metrics and latest values");
        Console.WriteLine("  loom metrics <pid> [cpu|memory|thread]  Show formatted metrics");
        Console.WriteLine("  loom query <pid> \"SELECT...\"  Execute LoomQL query");
        break;
}
