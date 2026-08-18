using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using System.Diagnostics.Tracing;

namespace Loom.DevTools.Commands;

public static class WatchCommand
{
    public static async Task RunAsync(int pid, CancellationToken ct)
    {
        Console.WriteLine($"Watching Loom.Telemetry metrics from process {pid}...\n");
        Console.WriteLine("Press Ctrl+C to stop.\n");

        EventPipeSession? session = null;
        EventPipeEventSource? source = null;

        try
        {
            var client = new DiagnosticsClient(pid);
            var sessionId = Guid.NewGuid().ToString();
            var providers = new[] {
                new EventPipeProvider("System.Diagnostics.Metrics",
                    EventLevel.Informational,
                    0x2, // TimeSeriesValues keyword — required for metric value events
                    new Dictionary<string, string?> {
                        ["SessionId"] = sessionId,
                        ["Metrics"] = "Loom.Telemetry",
                        ["RefreshInterval"] = "1",
                        ["MaxTimeSeries"] = "1000",
                        ["MaxHistograms"] = "20",
                        ["ClientId"] = Guid.NewGuid().ToString()
                    })
            };

            session = client.StartEventPipeSession(providers, requestRundown: false);
            source = new EventPipeEventSource(session.EventStream);

            source.Dynamic.All += traceEvent =>
            {
                if (ct.IsCancellationRequested)
                {
                    source.StopProcessing();
                    return;
                }

                var eventName = traceEvent.EventName;
                if (eventName.Contains("Collection") || eventName.Contains("ProcessInfo"))
                    return;

                // Debug: dump raw event structure
                var payloadNames = traceEvent.PayloadNames;
                var fields = payloadNames != null
                    ? string.Join(", ", payloadNames.Select((n, i) => $"{n}={traceEvent.PayloadValue(i)}"))
                    : "(no payloads)";
                Console.WriteLine($"[{DateTime.Now:T}] {eventName}: {fields}");
            };

            // Register cancellation to stop the session
            using var _ = ct.Register(() =>
            {
                try
                {
                    session?.Stop();
                    source?.StopProcessing();
                }
                catch { }
            });

            await Task.Run(() =>
            {
                try
                {
                    source.Process();
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        Console.WriteLine($"\nError processing events: {ex.Message}");
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Expected when Ctrl+C is pressed
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                Console.WriteLine($"Failed to attach to process {pid}: {ex.Message}");
        }
        finally
        {
            try
            {
                source?.Dispose();
                session?.Dispose();
            }
            catch { }
        }

        Console.WriteLine("\nStopped.");
    }
}
