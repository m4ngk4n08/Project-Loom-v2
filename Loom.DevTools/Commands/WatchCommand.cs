using Loom.DevTools.Rendering;
using Loom.Telemetry;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Spectre.Console;
using System.Diagnostics.Tracing;

namespace Loom.DevTools.Commands;

public static class WatchCommand
{
    public static async Task RunAsync(int pid, bool raw, CancellationToken ct)
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

                if (raw)
                {
                    var payloadNames = traceEvent.PayloadNames;
                    var fields = payloadNames != null
                        ? string.Join(", ", payloadNames.Select((n, i) => $"{n}={traceEvent.PayloadValue(i)}"))
                        : "(no payloads)";
                    Console.WriteLine($"[{DateTime.Now:T}] {eventName}: {fields}");
                    return;
                }

                PrintFormattedEvent(eventName, traceEvent);
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

    /// <summary>
    /// Only ValuePublished events carry metric data; BeginInstrumentReporting and
    /// friends are metadata-only and are ignored, same as EventPipeCollector.
    /// </summary>
    private static void PrintFormattedEvent(string eventName, TraceEvent traceEvent)
    {
        if (!eventName.Contains("ValuePublished"))
            return;

        var payloadNames = traceEvent.PayloadNames;
        if (payloadNames == null) return;

        string? metricName = null;
        double value = 0;

        for (var i = 0; i < payloadNames.Length; i++)
        {
            switch (payloadNames[i])
            {
                case "Name":
                case "instrumentName":
                    metricName = traceEvent.PayloadValue(i)?.ToString();
                    break;
                case "Value":
                case "Mean":
                case "Rate":
                case "value":
                case "sum":
                    if (double.TryParse(traceEvent.PayloadValue(i)?.ToString(), out var v))
                        value = v;
                    break;
            }
        }

        if (metricName == null) return;

        var metricType = eventName switch
        {
            "CounterRateValuePublished" => MetricType.Counter,
            "GaugeValuePublished" => MetricType.Gauge,
            "HistogramValuePublished" => MetricType.Histogram,
            _ => MetricType.Gauge
        };

        var color = Hex(ColorForType(metricType));
        var dim = Hex(LoomTheme.Dim);
        var typeLabel = metricType.ToString().PadRight(10);

        AnsiConsole.MarkupLine(
            $"[{dim}]{DateTime.Now:T}[/]  [{color}]{typeLabel}[/] {Markup.Escape(metricName)} = {UnitFormatter.Format(metricName, value)}");
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
}
