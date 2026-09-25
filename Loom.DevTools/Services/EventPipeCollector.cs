using System.Diagnostics.Tracing;
using System.Globalization;
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
    private long _recordsIngested;
    private Exception? _lastError;

    /// <summary>
    /// Set when CollectLoop's session terminates via exception (wrong PID, permission
    /// failure, target exit, ...). Without this an empty store is indistinguishable from
    /// "the target published nothing" - see CLAUDE.md, "a negative probe with invalid
    /// input reads exactly like a clean bill of health."
    ///
    /// Volatile because it is written on the collection thread and read by whichever
    /// thread polls it. A plain field carries no ordering guarantee, so a poller can spin
    /// on a cached null while the session is already dead - which is the exact silence
    /// this property exists to break. RecordsIngested already gets this via Interlocked.
    /// </summary>
    public Exception? LastError => Volatile.Read(ref _lastError);

    public bool IsFaulted => LastError is not null;

    public long RecordsIngested => Interlocked.Read(ref _recordsIngested);

    /// <summary>
    /// Completes when CollectLoop returns — the session ended, the target exited, or it
    /// faulted. A caller that streams from a store subscription needs this: nothing else
    /// completes the subscriber channel, so without it a dead session reads as an idle one.
    /// </summary>
    public Task Completion => _collectionTask ?? Task.CompletedTask;

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

        // Join the processing thread instead of only asking it to stop. Cancelling sets
        // a flag; until CollectLoop returns, a "stopped" collector still owns an
        // EventPipe session and an event-processing thread, still writing into a store
        // the caller may already have disposed. _collectionTask was captured but never
        // awaited, so nothing observed that thread's end.
        //
        // Bounded: a wedged session must never hang a CLI command, and Stop() is on the
        // path DashboardCommand/WatchCommand/DevCommand take on shutdown.
        try
        {
            _collectionTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // The task is cancelled or faulted - CollectLoop already recorded anything
            // worth reporting in LastError, and Stop() must not throw during teardown.
        }
    }

    public void Dispose()
    {
        Stop();
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

                if (!EventPipeMetricPayload.TryBuildRecord(eventName, traceEvent.PayloadNames, i => traceEvent.PayloadValue(i), DateTime.UtcNow.Ticks, out var record))
                    return;

                _store.Write(in record);
                Interlocked.Increment(ref _recordsIngested);
            };

            using var reg = ct.Register(() => { try { session.Stop(); } catch { } });
            source.Process();
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastError, ex);
        }
        finally
        {
            session?.Dispose();
        }
    }

    private void IngestLogMessage(TraceEvent traceEvent)
    {
        if (_logStore is null) return;

        if (!EventPipeLogPayload.TryBuildLogRecord(_parser, traceEvent.PayloadNames, i => traceEvent.PayloadValue(i), traceEvent.TimeStamp.ToUniversalTime().Ticks, out var record))
            return;

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
            Interlocked.Increment(ref _recordsIngested);
        }
    }
}
