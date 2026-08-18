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
    case ["watch", var pidArg] when int.TryParse(pidArg, out var pid):
        await WatchCommand.RunAsync(pid, cts.Token);
        break;
    default:
        Console.WriteLine("Usage:");
        Console.WriteLine("  loom dev [--all]    Discover Loom-instrumented processes");
        Console.WriteLine("  loom watch <pid>    Watch live metrics from a process");
        break;
}
