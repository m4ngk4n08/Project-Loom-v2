using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using Loom.Storage;
using Loom.Telemetry;

namespace Loom.Telemetry.Tests.Web;

/// <summary>
/// Minimal IMetricStore test double. MetricsService.GetCpuMetricsAsync only calls
/// GetBuffers(), so every other member is unimplemented on purpose - a call to one
/// would mean the code under test grew a dependency these tests don't yet cover.
/// </summary>
internal sealed class FakeMetricStore : IMetricStore
{
    private readonly Dictionary<string, MetricBuffer> _buffers = new();

    public MetricBuffer AddBuffer(string name)
    {
        var buffer = new MetricBuffer();
        _buffers[name] = buffer;
        return buffer;
    }

    public IReadOnlyDictionary<string, MetricBuffer> GetBuffers() => _buffers;

    public IReadOnlyCollection<string> GetMetricNames() => _buffers.Keys.ToList();

    public void Write(in MetricRecord record) => throw new NotImplementedException();
    public MetricRecord[] ReadRecent(string metricName, int count) => throw new NotImplementedException();
    public MetricRecord[] ReadRecent(int count) => throw new NotImplementedException();
    public MetricRecord[] ReadSince(string metricName, long timestampUtcTicks) => throw new NotImplementedException();
    public MetricRecord[] ReadAll(int limit = 1000) => throw new NotImplementedException();
    public (double Value, DateTime Timestamp)[] Snapshot(string metricName) => throw new NotImplementedException();
    public ChannelReader<MetricRecord> Subscribe() => throw new NotImplementedException();
    public void Unsubscribe(ChannelReader<MetricRecord> reader) { }
}
