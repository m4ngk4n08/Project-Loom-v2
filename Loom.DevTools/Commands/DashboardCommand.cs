using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace Loom.DevTools.Commands;

public static class DashboardCommand
{
    public static async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("Discovering Loom-instrumented processes...\n");

        var processes = DiagnosticsClient.GetPublishedProcesses().ToList();
        if (processes.Count == 0)
        {
            Console.WriteLine("No .NET processes found. Start your app first.");
            return;
        }

        // Parallel detection
        var tasks = processes.Select(pid => Task.Run(async () =>
        {
            var name = GetProcessName(pid);
            var hasLoom = await ProbeForLoom(new DiagnosticsClient(pid), ct);
            return (pid, name, hasLoom);
        })).ToArray();

        var results = await Task.WhenAll(tasks);
        var instrumented = results.Where(r => r.hasLoom).ToList();

        if (instrumented.Count == 0)
        {
            Console.WriteLine("No Loom-instrumented processes found.");
            Console.WriteLine("Ensure your app references Loom.Telemetry and is running.");
            return;
        }

        int targetPid;
        if (instrumented.Count == 1)
        {
            targetPid = instrumented[0].pid;
            Console.WriteLine($"Found: {instrumented[0].name} (pid {targetPid})");
        }
        else
        {
            Console.WriteLine("Multiple Loom-instrumented processes found:");
            for (int i = 0; i < instrumented.Count; i++)
                Console.WriteLine($"  [{i + 1}] {instrumented[i].name} (pid {instrumented[i].pid})");

            Console.Write("\nSelect process [1]: ");
            var input = Console.ReadLine();
            var index = string.IsNullOrEmpty(input) ? 0 : int.Parse(input) - 1;
            targetPid = instrumented[index].pid;
        }

        await LaunchDashboard(targetPid);
    }

    public static async Task RunAsync(int pid, CancellationToken ct)
    {
        await LaunchDashboard(pid);
    }

    private static async Task LaunchDashboard(int pid)
    {
        // Check if loom-dashboard is available
        try
        {
            var check = Process.Start(new ProcessStartInfo("loom-dashboard", "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (check != null)
            {
                await check.WaitForExitAsync();
                if (check.ExitCode != 0)
                    throw new Exception();
            }
            else
            {
                throw new Exception();
            }
        }
        catch
        {
            Console.WriteLine("Dashboard package not found.");
            Console.WriteLine("Install with: dotnet tool install -g Loom.Dashboard");
            return;
        }

        // Launch loom-dashboard as a child process
        Console.WriteLine($"\nStarting dashboard for PID {pid}...\n");
        var dashboard = Process.Start(new ProcessStartInfo("loom-dashboard", pid.ToString())
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        if (dashboard == null)
        {
            Console.WriteLine("Failed to start dashboard.");
            return;
        }

        // Forward output
        _ = Task.Run(async () =>
        {
            while (!dashboard.StandardOutput.EndOfStream)
                Console.WriteLine(await dashboard.StandardOutput.ReadLineAsync());
        });

        _ = Task.Run(async () =>
        {
            while (!dashboard.StandardError.EndOfStream)
                Console.Error.WriteLine(await dashboard.StandardError.ReadLineAsync());
        });

        await dashboard.WaitForExitAsync();
    }

    private static string GetProcessName(int pid)
    {
        try { return Process.GetProcessById(pid).ProcessName; }
        catch { return "unknown"; }
    }

    private static async Task<bool> ProbeForLoom(DiagnosticsClient client, CancellationToken ct)
    {
        EventPipeSession? session = null;
        try
        {
            var providers = new[] {
                new EventPipeProvider("System.Diagnostics.Metrics",
                    EventLevel.Informational, 0x2,
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
                if (traceEvent.PayloadNames == null) return;
                for (int i = 0; i < traceEvent.PayloadNames.Length; i++)
                {
                    if (traceEvent.PayloadValue(i)?.ToString() == "Loom.Telemetry")
                    {
                        found = true;
                        source.StopProcessing();
                        return;
                    }
                }
            };

            var processTask = Task.Run(() => { try { source.Process(); } catch { } });
            await Task.WhenAny(processTask, Task.Delay(2000, ct));

            if (!processTask.IsCompleted)
            {
                try { session.Stop(); } catch { }
                await Task.WhenAny(processTask, Task.Delay(500));
            }

            return found;
        }
        catch { return false; }
        finally { try { session?.Dispose(); } catch { } }
    }
}
