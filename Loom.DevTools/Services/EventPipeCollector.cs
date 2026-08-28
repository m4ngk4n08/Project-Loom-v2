using System.Diagnostics.Tracing;
using Loom.Storage;
using Loom.Telemetry;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;

namespace Loom.DevTools.Services;

/// <summary>
/// Shared EventPipe collector that pulls metrics from a target process
/// into a local IMetricStore. Used by all CLI commands.
/// </summary>
public sealed class EventPipeCollector : IDisposable
{
    private readonly int _pid;
    private readonly IMetricStore _store;
    private readonly ILogStore? _logStore;

    // LogMessageParser is not thread-safe and does not need to be: EventPipe delivers
    // callbacks serialized on the processing thread, and one collector owns one parser.
    private readonly LogMessageParser _parser = new();
    private CancellationTokenSource? _cts;
    private Task? _collectionTask;

    // logStore is an APPENDED optional parameter, so every existing call site compiles
    // unchanged. When it is null the logging provider is not enabled at all - collecting
    // Microsoft-Extensions-Logging at Verbose serializes every log event in the target
    // process, and `metrics`/`query`/`watch` must not silently pay that cost.
    public EventPipeCollector(int pid, IMetricStore store, ILogStore? logStore = null)
    {
        _pid = pid;
        _store = store;
        _logStore = logStore;
    }

    public void Start(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _collectionTask = Task.Run(() => CollectLoop(_cts.Token), _cts.Token);
    }

    public async Task CollectForAsync(TimeSpan duration, CancellationToken ct)
    {
        Start(ct);
        await Task.Delay(duration, ct);
        Stop();
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private void CollectLoop(CancellationToken ct)
    {
        var client = new DiagnosticsClient(_pid);
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
                }),
            // Real system/runtime counters (CPU %, working set, GC heap, allocations/sec,
            // thread pool) for any .NET target — not just Loom-instrumented ones.
            new EventPipeProvider(SystemRuntimeCounters.ProviderName,
                EventLevel.Informational,
                0,
                new Dictionary<string, string?> {
                    ["EventCounterIntervalSec"] = "1"
                })
        };

        // JsonMessage only (keyword 8) - it already carries the formatted text, so
        // enabling FormattedMessage as well would deliver every log twice.
        if (_logStore is not null)
        {
            providers = [.. providers,
                new EventPipeProvider("Microsoft-Extensions-Logging", EventLevel.Verbose, 8)];
        }

        EventPipeSession? session = null;
        try
        {
            session = client.StartEventPipeSession(providers, requestRundown: false);
            var source = new EventPipeEventSource(session.EventStream);

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

                // System.Runtime delivers EventCounters events (JSON payload) rather
                // than the strongly-typed *ValuePublished shape.
                if (eventName == "EventCounters")
                {
                    try
                    {
                        IngestEventCounters(traceEvent);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"EventCounters parse failed: {ex.Message}");
                    }
                    return;
                }

                if (eventName == "MessageJson")
                {
                    try
                    {
                        IngestLogMessage(traceEvent);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"MessageJson parse failed: {ex.Message}");
                    }
                    return;
                }

                // Only ingest actual value-publish events; BeginInstrumentReporting is metadata only
                if (!eventName.Contains("ValuePublished"))
                    return;

                var payloadNames = traceEvent.PayloadNames;
                if (payloadNames == null) return;

                string? metricName = null;
                double value = 0;

                for (int i = 0; i < payloadNames.Length; i++)
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

                var record = new MetricRecord(metricName, metricType, value, DateTime.UtcNow.Ticks);
                _store.Write(in record);
            };

            using var reg = ct.Register(() => { try { session.Stop(); } catch { } });
            source.Process();
        }
        catch { }
        finally
        {
            session?.Dispose();
        }
    }

    private void IngestLogMessage(TraceEvent traceEvent)
    {
        if (_logStore is null) return;

        var payloadNames = traceEvent.PayloadNames;
        if (payloadNames == null) return;

        string? category = null;
        string? formattedMessage = null;
        string? exceptionJson = null;
        string? argumentsJson = null;
        string? activityTraceId = null;
        string? activitySpanId = null;
        int level = -1;
        int eventId = 0;

        for (int i = 0; i < payloadNames.Length; i++)
        {
            switch (payloadNames[i])
            {
                case "LoggerName":
                    category = traceEvent.PayloadValue(i)?.ToString();
                    break;
                case "Level":
                    level = EventPipeLogPayload.ToInt32(traceEvent.PayloadValue(i), -1);
                    break;
                case "EventId":
                    eventId = EventPipeLogPayload.ToInt32(traceEvent.PayloadValue(i), 0);
                    break;
                case "FormattedMessage":
                    formattedMessage = traceEvent.PayloadValue(i)?.ToString();
                    break;
                case "ExceptionJson":
                    exceptionJson = traceEvent.PayloadValue(i)?.ToString();
                    break;
                case "ArgumentsJson":
                    argumentsJson = traceEvent.PayloadValue(i)?.ToString();
                    break;
                case "ActivityTraceId":
                    activityTraceId = traceEvent.PayloadValue(i)?.ToString();
                    break;
                case "ActivitySpanId":
                    activitySpanId = traceEvent.PayloadValue(i)?.ToString();
                    break;
            }
        }

        if (category == null || formattedMessage == null) return;
        // Observed range is 0..5 (Trace..Critical), matching LoomLogLevel's ordering.
        if (level < 0 || level > 5) return;

        var record = EventPipeLogPayload.BuildLogRecord(
            _parser, formattedMessage, category, level,
            traceEvent.TimeStamp.ToUniversalTime().Ticks, eventId,
            exceptionJson, argumentsJson, activityTraceId, activitySpanId);

        _logStore.Write(in record);
    }

    private void IngestEventCounters(TraceEvent traceEvent)
    {
        var payload = traceEvent.PayloadValue(0)?.ToString();
        if (string.IsNullOrEmpty(payload)) return;

        var records = SystemRuntimeCounters.Parse(payload);
        var now = DateTime.UtcNow.Ticks;
        foreach (var (name, type, value) in records)
        {
            _store.Write(new MetricRecord(name, type, value, now));
        }
    }
}
