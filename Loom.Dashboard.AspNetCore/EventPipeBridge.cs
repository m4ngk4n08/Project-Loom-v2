using System.Diagnostics.Tracing;
using System.Globalization;
using Loom.Storage;
using Loom.Telemetry;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Loom.Dashboard;

/// <summary>
/// Background service that pulls metrics and logs from a target process via
/// EventPipe and writes them into the local IMetricStore / ILogStore.
/// </summary>
public sealed class EventPipeBridge : BackgroundService
{
    private readonly int _targetPid;
    private readonly IMetricStore _store;
    private readonly ILogStore _logStore;
    private readonly ILogger<EventPipeBridge> _logger;
    private long _recordsIngested;
    private long _logRecordsIngested;
    private int _reconnectCount;

    // One parser for the bridge's lifetime so the template pool survives
    // reconnects. LogMessageParser is not thread-safe, and does not need to be:
    // IngestLogMessage runs only on the single thread inside source.Process(),
    // and ExecuteAsync awaits each StreamMetrics call before starting the next,
    // so no two sessions' callbacks ever overlap.
    private readonly LogMessageParser _parser = new();

    public EventPipeBridge(int targetPid, IMetricStore store, ILogStore logStore, ILogger<EventPipeBridge> logger)
    {
        _targetPid = targetPid;
        _store = store;
        _logStore = logStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EventPipe bridge connecting to PID {Pid}...", _targetPid);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StreamMetrics(stoppingToken);
                _logger.LogInformation(
                    "EventPipe session ended after ingesting {RecordCount} metric records and {LogRecordCount} log records.",
                    Interlocked.Read(ref _recordsIngested),
                    Interlocked.Read(ref _logRecordsIngested));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var reconnect = Interlocked.Increment(ref _reconnectCount);
                _logger.LogWarning(
                    "EventPipe disconnected ({ReconnectCount}): {Message}. Reconnecting in 2s...",
                    reconnect, ex.Message);
                await Task.Delay(2000, stoppingToken);
            }
        }

        _logger.LogInformation(
            "EventPipe bridge stopped. Total: {TotalRecords} metric records, {TotalLogRecords} log records, {ReconnectCount} reconnects.",
            Interlocked.Read(ref _recordsIngested),
            Interlocked.Read(ref _logRecordsIngested),
            _reconnectCount);
    }

    private async Task StreamMetrics(CancellationToken ct)
    {
        var client = new DiagnosticsClient(_targetPid);
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
                }),
            // JsonMessage only (keyword 8) - it already carries the formatted text,
            // so enabling FormattedMessage as well would deliver every log twice.
            new EventPipeProvider("Microsoft-Extensions-Logging", EventLevel.Verbose, 8)
        };

        using var session = client.StartEventPipeSession(providers, requestRundown: false);
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

            if (eventName == "MessageJson")
            {
                try
                {
                    IngestLogMessage(traceEvent);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "MessageJson parse failed for PID {Pid}.", _targetPid);
                }
                return;
            }

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
                    _logger.LogWarning(ex, "EventCounters parse failed for PID {Pid}.", _targetPid);
                }
                return;
            }

            // Only ingest actual value-publish events; BeginInstrumentReporting is metadata only
            if (!eventName.Contains("ValuePublished"))
                return;

            var payloadNames = traceEvent.PayloadNames;
            if (payloadNames == null) return;

            // The value field is chosen BY EVENT TYPE, not by a flat set of alternative
            // names: a single incoming "value" field means something different per event
            // shape (CounterRateValuePublished's "rate" is a per-interval delta;
            // GaugeValuePublished's "lastValue" is a point-in-time sample;
            // HistogramValuePublished's "sum" is a summary total), and a name that
            // happened to match more than one of those shapes would let whichever case
            // came later in a flat switch silently win - the exact bug this rewrite
            // replaces (see PROMPT-counter-rate-ingest.md). "rate"/"value"/"lastValue"/
            // "sum" are the only four fields that carry a number on these events -
            // verified against a live session - so nothing else could be captured here.
            //
            // Trade-off: summing per-interval "rate" deltas means a missed or dropped
            // publish undercounts the total permanently, whereas re-reading the
            // cumulative "value" would self-correct on the next publish. That is still
            // the right choice here because InMemoryMetricStore's accumulator contract
            // is increments, and it is monotonic - accepting occasional undercount on a
            // dropped event beats guaranteed, unbounded overcount on every event.
            //
            // UpDownCounterRateValuePublished (not "UpDownCounterValuePublished" - that
            // name was never emitted by the runtime and this mapping never matched it)
            // carries the same rate/value shape as CounterRateValuePublished - measured
            // against a live session with the fixture's up-down counter. It stays a
            // Gauge (an up-down counter isn't monotonic, so this store's Counter-only
            // accumulator is the wrong home for it), but its useful reading is still the
            // interval delta, not the running total.
            var (metricType, valueField) = eventName switch
            {
                "CounterRateValuePublished" => (MetricType.Counter, "rate"),
                "GaugeValuePublished" => (MetricType.Gauge, "lastValue"),
                "HistogramValuePublished" => (MetricType.Histogram, "sum"),
                "UpDownCounterRateValuePublished" => (MetricType.Gauge, "rate"),
                _ => (MetricType.Gauge, (string?)null)
            };

            string? metricName = null;
            double value = 0;
            string? tagPayload = null;

            for (int i = 0; i < payloadNames.Length; i++)
            {
                var payloadName = payloadNames[i];
                if (payloadName is "Name" or "instrumentName")
                {
                    metricName = traceEvent.PayloadValue(i)?.ToString();
                }
                else if (payloadName == "tags")
                {
                    tagPayload = traceEvent.PayloadValue(i)?.ToString();
                }
                else if (valueField != null && payloadName == valueField)
                {
                    if (double.TryParse(traceEvent.PayloadValue(i)?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        value = v;
                }
            }

            if (metricName == null) return;

            var record = new MetricRecord(
                metricName,
                metricType,
                value,
                DateTime.UtcNow.Ticks,
                EventPipeTagPayload.Parse(tagPayload)
            );
            _store.Write(in record);
            Interlocked.Increment(ref _recordsIngested);
        };

        using var reg = ct.Register(() =>
        {
            try { session.Stop(); }
            catch { }
        });

        await Task.Run(() =>
        {
            try { source.Process(); }
            catch { }
        }, ct);
    }

    private void IngestLogMessage(TraceEvent traceEvent)
    {
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
        Interlocked.Increment(ref _logRecordsIngested);
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
            Interlocked.Increment(ref _recordsIngested);
        }
    }
}
