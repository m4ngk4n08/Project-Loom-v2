using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Loom.Telemetry.Tests.Integration;

/// <summary>
/// Starts Loom.TestFixtureApp once for every test in the class it is shared across and
/// disposes it at the end. Startup is expensive and RefreshInterval=1 means metrics
/// publish only once per second - paying that per-test would make the suite intolerable.
/// </summary>
public sealed class FixtureProcess : IAsyncLifetime
{
    private Process? _process;
    private readonly StringBuilder _stdErr = new();
    private readonly StringBuilder _stdOut = new();

    public int Pid { get; private set; }

    public async Task InitializeAsync()
    {
        var path = ResolveFixtureAppPath();

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "exec \"" + path + "\"",
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        _process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start fixture process at '{path}'.");
        Pid = _process.Id;

        _process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (_stdErr) _stdErr.AppendLine(e.Data); };
        _process.BeginErrorReadLine();

        var readyTask = _process.StandardOutput.ReadLineAsync();
        var completed = await Task.WhenAny(readyTask, Task.Delay(TimeSpan.FromSeconds(30)));

        if (completed != readyTask || readyTask.Result != "READY")
        {
            string stderr;
            lock (_stdErr) stderr = _stdErr.ToString();
            var actualLine = readyTask.IsCompletedSuccessfully ? readyTask.Result : "(timed out waiting for a line)";
            try { _process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException(
                $"Fixture process did not send READY within 30s. Last stdout line: '{actualLine}'. Stderr: {stderr}");
        }

        // Everything after READY is consumed by the EventPipe collector under test, not
        // by this fixture - keep draining stdout so the child's pipe never fills and blocks it.
        _ = Task.Run(async () =>
        {
            try
            {
                while (await _process.StandardOutput.ReadLineAsync() is { } line)
                {
                    lock (_stdOut) _stdOut.AppendLine(line);
                }
            }
            catch { }
        });
    }

    public async Task DisposeAsync()
    {
        if (_process is null) return;

        try
        {
            _process.StandardInput.Close();
        }
        catch { }

        var exited = await Task.WhenAny(_process.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(5)));
        if (!_process.HasExited)
        {
            try { _process.Kill(entireProcessTree: true); } catch { }
        }

        _process.Dispose();
    }

    private static string ResolveFixtureAppPath()
    {
        var attr = typeof(FixtureProcess).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "LoomFixtureAppPath");

        if (attr?.Value is not { } rawPath)
            throw new InvalidOperationException("LoomFixtureAppPath assembly metadata attribute was not found. Check Loom.Telemetry.Tests.csproj.");

        var path = Path.GetFullPath(rawPath);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fixture app not found at resolved path '{path}'. Build Loom.TestFixtureApp before running integration tests.", path);

        return path;
    }
}
