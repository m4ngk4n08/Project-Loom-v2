using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using System.Diagnostics.Tracing;

namespace Loom.DevTools.Commands;

public static class DevCommand
{
    public static async Task RunAsync(bool showAll, CancellationToken ct)
    {
        Console.WriteLine("Loom local dev mode — discovering .NET processes...");
        Console.WriteLine(showAll ? "Showing all .NET processes" : "Showing only Loom-instrumented processes");
        Console.WriteLine("Press Ctrl+C to stop.\n");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var processes = DiagnosticsClient.GetPublishedProcesses().ToList();
                var loomProcesses = new List<(int pid, string name, bool hasLoom)>();

                var tasks = processes.Select(pid => Task.Run(async () =>
                {
                    var name = GetProcessName(pid);
                    var hasLoom = await HasLoomMeterAsync(new DiagnosticsClient(pid), ct);
                    return (pid, name, hasLoom);
                })).ToArray();

                var results = await Task.WhenAll(tasks);
                foreach (var (pid, name, hasLoom) in results)
                {
                    if (showAll || hasLoom)
                        loomProcesses.Add((pid, name, hasLoom));
                }

                Console.Clear();
                Console.WriteLine($"Loom dev — {loomProcesses.Count} process(es) — {DateTime.Now:T}");
                Console.WriteLine(showAll ? "Showing all .NET processes" : "Showing only Loom-instrumented processes");
                Console.WriteLine("Press Ctrl+C to stop.\n");

                if (loomProcesses.Count == 0)
                {
                    Console.WriteLine("  No Loom-instrumented processes found.");
                    if (!showAll)
                        Console.WriteLine("  Run 'loom dev --all' to see all .NET processes.");
                }
                else
                {
                    foreach (var (pid, name, hasLoom) in loomProcesses)
                    {
                        Console.WriteLine(hasLoom
                            ? $"  ✓ {name} (pid {pid}) — Loom.Telemetry active"
                            : $"  · {name} (pid {pid}) — .NET process, not Loom-instrumented");
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when Ctrl+C is pressed
        }

        Console.WriteLine("\nStopped.");
    }

    private static string GetProcessName(int pid)
    {
        try
        {
            var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return "unknown";
        }
    }

    private static async Task<bool> HasLoomMeterAsync(DiagnosticsClient client, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return false;

        EventPipeSession? session = null;
        try
        {
            var providers = new[] {
                new EventPipeProvider("System.Diagnostics.Metrics",
                    EventLevel.Informational,
                    0x2,
                    new Dictionary<string, string?> {
                        ["SessionId"] = Guid.NewGuid().ToString(),
                        ["Metrics"] = "Loom.Telemetry",
                        ["RefreshInterval"] = "1",
                        ["MaxTimeSeries"] = "1000",
                        ["MaxHistograms"] = "20",
                        ["ClientId"] = Guid.NewGuid().ToString()
                    })
            };

            session = client.StartEventPipeSession(providers, requestRundown: false);
            var source = new EventPipeEventSource(session.EventStream);
            var found = false;

            source.Dynamic.All += traceEvent =>
            {
                var payloadNames = traceEvent.PayloadNames;
                if (payloadNames == null) return;

                for (int i = 0; i < payloadNames.Length; i++)
                {
                    var val = traceEvent.PayloadValue(i)?.ToString();
                    if (val == "Loom.Telemetry")
                    {
                        found = true;
                        source.StopProcessing();
                        return;
                    }
                }
            };

            var processTask = Task.Run(() =>
            {
                try { source.Process(); }
                catch { }
            });

            // Hard timeout — don't rely on session.Stop() to unblock Process()
            var completed = await Task.WhenAny(processTask, Task.Delay(2000, ct));

            if (!processTask.IsCompleted)
            {
                try { session.Stop(); }
                catch { }
                // Give it a brief moment to exit after Stop
                await Task.WhenAny(processTask, Task.Delay(500));
            }

            return found;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { session?.Dispose(); }
            catch { }
        }
    }
}
