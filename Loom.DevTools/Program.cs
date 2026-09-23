using System.Reflection;
using Loom.DevTools.Commands;
using Loom.Security;

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
    case ["--version"]:
        {
            // From Directory.Build.props via the assembly, so it can't drift from the package.
            // The SDK appends "+<commit>" to the informational version; that isn't the version.
            var version = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            Console.WriteLine($"loom {version.Split('+')[0]}");
        }
        break;
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
    case ["auth", "init"]:
        if (!AuthCommand.Init(persist: false)) Environment.Exit(1);
        break;
    case ["auth", "init", "--persist"]:
        if (!AuthCommand.Init(persist: true)) Environment.Exit(1);
        break;
    case ["auth", "add-user", var newUser, ..]:
        {
            string? usersFile = null;
            var bad = false;
            for (var i = 3; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--users-file" when i + 1 < args.Length: usersFile = args[++i]; break;
                    default: bad = true; break;
                }
            }
            if (bad)
            {
                Console.Error.WriteLine("Usage: loom auth add-user <name> [--users-file <path>]");
                Environment.Exit(1);
            }
            else if (!AuthCommand.AddUser(newUser, usersFile))
            {
                Environment.Exit(1);
            }
        }
        break;
    case ["auth", "hash"]:
        AuthCommand.Hash();
        break;
    case ["auth", "token", ..] when args.Length >= 4:
        {
            string? sub = null;
            var scope = JwtScope.Full;
            var ttl = TimeSpan.FromDays(90);
            string? keyFile = null;
            var bad = false;
            for (var i = 2; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--sub" when i + 1 < args.Length: sub = args[++i]; break;
                    case "--scope" when i + 1 < args.Length:
                        if (!AuthCommand.TryParseScope(args[++i], out scope)) bad = true;
                        break;
                    case "--ttl" when i + 1 < args.Length:
                        if (!AuthCommand.TryParseTtl(args[++i], out ttl)) bad = true;
                        break;
                    case "--key-file" when i + 1 < args.Length: keyFile = args[++i]; break;
                    default: bad = true; break;
                }
            }
            if (bad || string.IsNullOrEmpty(sub))
            {
                Console.Error.WriteLine("Usage: loom auth token --sub <name> [--scope metrics|full] [--ttl 90d] [--key-file <path>]");
                Environment.Exit(1);
            }
            else if (!AuthCommand.Token(sub, scope, ttl, keyFile))
            {
                Environment.Exit(1);
            }
        }
        break;
    default:
        Console.WriteLine("Usage:");
        Console.WriteLine("  loom dev [--all]                        Discover Loom-instrumented processes");
        Console.WriteLine("  loom dev --dashboard                    Launch dashboard (requires the loom-dashboard tool)");
        Console.WriteLine("  loom watch <pid> [--raw]                Stream formatted metric events (--raw for unformatted payload dump)");
        Console.WriteLine("  loom explore <pid>                      List all metrics and latest values");
        Console.WriteLine("  loom metrics <pid> [cpu|memory|thread]  Show formatted metrics");
        Console.WriteLine("  loom metrics <pid> --live               Live-refreshing terminal dashboard (requires an interactive terminal)");
        Console.WriteLine("  loom query <pid> \"SELECT...\"            Execute LoomQL query");
        Console.WriteLine("  loom logs <pid> [--count N] [--category X] [--seconds N]   Show recent captured logs");
        Console.WriteLine("  loom search <pid> \"<query>\" [--max N] [--seconds N]        BM25 search over captured logs");
        Console.WriteLine("  loom auth init [--persist]              Create a dev signing key and users file");
        Console.WriteLine("                                          (--persist also sets the env vars permanently)");
        Console.WriteLine("  loom auth add-user <name> [--users-file <path>]  Append a user (prompts for a password)");
        Console.WriteLine("  loom auth hash                          Print a password hash to stdout");
        Console.WriteLine("                                          (the users-file line is <name>:<hash>)");
        Console.WriteLine("  loom auth token --sub <name> [--scope metrics|full] [--ttl 90d] [--key-file <path>]  Mint a service token");
        Console.WriteLine("  loom --version                          Print the version");
        break;
}
