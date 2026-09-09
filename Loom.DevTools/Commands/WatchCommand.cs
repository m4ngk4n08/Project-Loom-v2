using Loom.DevTools.Rendering;
using Loom.DevTools.Services;
using Loom.Storage;
using Loom.Telemetry;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Spectre.Console;
using System.Diagnostics.Tracing;
using System.Threading.Channels;

namespace Loom.DevTools.Commands;

public static class WatchCommand
{
    public static async Task RunAsync(int pid, bool raw, CancellationToken ct)
    {
        if (raw)
        {
            await RunRawAsync(pid, ct);
            return;
        }

        Console.WriteLine($"Watching Loom.Telemetry metrics from process {pid}...\n");
        Console.WriteLine("Press Ctrl+C to stop.\n");

        // Each record renders as exactly one line - a streaming log, not a layout that
        // benefits from wrapping. Without this, Spectre's own width detection (which falls
        // back to a narrow default whenever output is redirected - to a file, to `grep`, to
        // anything other than a live terminal) wraps a tagged line's "[key=value]" suffix
        // onto its own line, splitting one record into two. Measured directly: adding tag
        // rendering below made lines long enough to trigger this for the first time.
        AnsiConsole.Profile.Width = 4096;

        // Formatted mode uses the same shared, tested parser as `loom explore` -
        // EventPipeCollector - instead of the private payload switch this command used to
        // carry. That private copy was the pre-2026-09-07 shape (case "Value"/"Mean"/"Rate"
        // instead of the runtime's actual "lastValue" for gauges), had no "tags" case at
        // all, and read a counter's cumulative "value" instead of its per-interval "rate".
        // Every gauge - including this process's own loom.telemetry.up heartbeat - showed 0
        // forever as a result. See PROMPT-watch-parser.md and BACKLOG.md 6.12.
        //
        // EventPipeCollector also enables the System.Runtime provider, so real .NET runtime
        // counters (cpu-usage, gc-heap-size, ...) now stream here too - they did not before,
        // since the old private session only enabled "System.Diagnostics.Metrics". This is a
        // visible behaviour change, not a silent one: `loom explore` already shows the same
        // runtime counters, so watch now matches it instead of being the odd one out.
        var store = new InMemoryMetricStore();
        var reader = store.Subscribe();
        using var collector = new EventPipeCollector(pid, store);
        collector.Start(ct);

        // Nothing else completes the subscriber channel. Without this, a session that never
        // started (bad PID, permission) or that has ended (target exited) leaves the loop
        // below parked forever printing nothing — which is exactly what the pre-fold code
        // did NOT do: it reported the error and exited. Disposing the store completes every
        // subscriber channel, which ends the ReadAllAsync loop cleanly.
        _ = collector.Completion.ContinueWith(_ => store.Dispose(), TaskScheduler.Default);

        try
        {
            await foreach (var record in reader.ReadAllAsync(ct))
            {
                PrintFormattedRecord(record);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when Ctrl+C is pressed
        }
        finally
        {
            store.Unsubscribe(reader);
        }

        if (!ct.IsCancellationRequested && collector.LastError is { } error)
        {
            // Distinguish "never attached" from "attached, then the target went away" —
            // both surface here as an exception, but only the first is a user error.
            Console.Error.WriteLine(collector.RecordsIngested == 0
                ? $"Failed to attach to process {pid}: {error.Message}"
                : $"Session ended: {error.Message}");
        }

        Console.WriteLine("\nStopped.");
    }

    private static void PrintFormattedRecord(MetricRecord record)
    {
        var color = Hex(ColorForType(record.Type));
        var dim = Hex(LoomTheme.Dim);
        var typeLabel = record.Type.ToString().PadRight(10);
        var tagsSuffix = record.Tags is { Length: > 0 }
            ? $" [{dim}]{Markup.Escape("[" + string.Join(", ", record.Tags.Select(t => t.ToString())) + "]")}[/]"
            : string.Empty;

        AnsiConsole.MarkupLine(
            $"[{dim}]{record.TimestampUtc.ToLocalTime():T}[/]  [{color}]{typeLabel}[/] " +
            $"{Markup.Escape(record.Name)} = {UnitFormatter.Format(record.Name, record.Value)}{tagsSuffix}");
    }

    private static Color ColorForType(MetricType type) => type switch
    {
        MetricType.Gauge => LoomTheme.Series(0),
        MetricType.Counter => LoomTheme.Series(1),
        MetricType.Histogram => LoomTheme.Series(2),
        MetricType.MethodExecution => LoomTheme.Series(3),
        _ => LoomTheme.Dim,
    };

    private static string Hex(Color color) => $"#{color.ToHex()}";

    // --raw keeps its own EventPipe session, deliberately: it dumps every payload field
    // verbatim, which is its whole purpose and which EventPipeCollector's parsing would
    // only get in the way of. It has no value-field switch to get wrong, so there is
    // nothing here that can drift out of sync the way the formatted path did.
    private static async Task RunRawAsync(int pid, CancellationToken ct)
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
