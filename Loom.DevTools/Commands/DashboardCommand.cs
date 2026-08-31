using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using System.ComponentModel;
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
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (check == null)
                throw new InvalidOperationException("Process.Start returned null.");

            // Start both reads before awaiting exit: a blocking read after
            // WaitForExitAsync can deadlock on a full pipe.
            var stdoutTask = check.StandardOutput.ReadToEndAsync();
            var stderrTask = check.StandardError.ReadToEndAsync();
            await check.WaitForExitAsync();
            var stderr = await stderrTask;
            await stdoutTask;

            if (check.ExitCode != 0)
            {
                Console.WriteLine($"loom-dashboard --version exited with code {check.ExitCode}.");
                if (!string.IsNullOrWhiteSpace(stderr))
                    Console.WriteLine(stderr.TrimEnd());
                Console.WriteLine("If the dashboard is installed, check LOOM_JWT_KEY_FILE and LOOM_AUTH_USERS_FILE.");
                return;
            }
        }
        catch (Win32Exception)
        {
            Console.WriteLine("Dashboard package not found.");
            Console.WriteLine("Install with: dotnet tool install -g Loom.Dashboard");
            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ex.GetType().Name}: {ex.Message}");
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

        // Forward output. Loop on ReadLineAsync returning null rather than checking
        // EndOfStream: that property blocks synchronously (CA2024), and it can also report
        // false immediately before the stream ends, in which case the awaited read returns
        // null and the old code printed a blank line at exit.
        var stdoutPump = Task.Run(async () =>
        {
            string? line;
            while ((line = await dashboard.StandardOutput.ReadLineAsync()) is not null)
                Console.WriteLine(line);
        });

        var stderrPump = Task.Run(async () =>
        {
            string? line;
            while ((line = await dashboard.StandardError.ReadLineAsync()) is not null)
                Console.Error.WriteLine(line);
        });

        await dashboard.WaitForExitAsync();
        await Task.WhenAll(stdoutPump, stderrPump);

        if (dashboard.ExitCode != 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Dashboard exited with code {dashboard.ExitCode}. See the output above.");
            Console.WriteLine("A configuration error is the usual cause - check LOOM_JWT_KEY_FILE and");
            Console.WriteLine("LOOM_AUTH_USERS_FILE, or run 'loom auth init' if this machine has no dev key.");
        }
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
