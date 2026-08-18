using System.Diagnostics.Tracing;
using Loom.Storage;
using Loom.Telemetry;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Loom.Dashboard;

/// <summary>
/// Background service that pulls metrics from a target process via EventPipe
/// and writes them into the local IMetricStore.
/// </summary>
public sealed class EventPipeBridge : BackgroundService
{
    private readonly int _targetPid;
    private readonly IMetricStore _store;
    private readonly ILogger<EventPipeBridge> _logger;

    public EventPipeBridge(int targetPid, IMetricStore store, ILogger<EventPipeBridge> logger)
    {
        _targetPid = targetPid;
        _store = store;
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
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("EventPipe disconnected: {Message}. Reconnecting in 2s...", ex.Message);
                await Task.Delay(2000, stoppingToken);
            }
        }
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
                })
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

            var payloadNames = traceEvent.PayloadNames;
            if (payloadNames == null) return;

            string? metricName = null;
            double value = 0;
            MetricType metricType = MetricType.Gauge;

            for (int i = 0; i < payloadNames.Length; i++)
            {
                switch (payloadNames[i])
                {
                    case "Name":
                        metricName = traceEvent.PayloadValue(i)?.ToString();
                        break;
                    case "Value":
                    case "Mean":
                    case "Rate":
                        if (double.TryParse(traceEvent.PayloadValue(i)?.ToString(), out var v))
                            value = v;
                        break;
                }
            }

            if (metricName == null) return;

            metricType = eventName switch
            {
                "CounterRateValuePublished" => MetricType.Counter,
                "GaugeValuePublished" => MetricType.Gauge,
                "HistogramValuePublished" => MetricType.Histogram,
                "UpDownCounterValuePublished" => MetricType.Gauge,
                _ => MetricType.Gauge
            };

            var record = new MetricRecord(
                metricName,
                metricType,
                value,
                DateTime.UtcNow.Ticks
            );
            _store.Write(in record);
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
}
