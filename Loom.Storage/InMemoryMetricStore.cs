using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Loom.Telemetry;

namespace Loom.Storage;

/// <summary>
/// In-memory metric store backed by per-metric ring buffers.
/// Thread-safe, zero-allocation writes, bounded memory.
/// Supports real-time subscriptions via Channel.
/// </summary>
public sealed class InMemoryMetricStore : IMetricStore, IDisposable
{
    private readonly ConcurrentDictionary<string, MetricBuffer> _buffers = new();
    private readonly ConcurrentDictionary<ChannelWriter<MetricRecord>, ChannelReader<MetricRecord>> _subscribers = new();
    private readonly int _bufferCapacity;

    public InMemoryMetricStore(int bufferCapacity = 8192)
    {
        _bufferCapacity = bufferCapacity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(in MetricRecord record)
    {
        var buffer = _buffers.GetOrAdd(record.Name, _ => new MetricBuffer(_bufferCapacity));
        buffer.Write(in record);
        NotifySubscribers(in record);
    }

    public MetricRecord[] ReadRecent(string metricName, int count)
    {
        if (!_buffers.TryGetValue(metricName, out var buffer))
            return Array.Empty<MetricRecord>();
        return buffer.ReadRecent(count);
    }

    public MetricRecord[] ReadRecent(int count)
    {
        var allRecords = new List<MetricRecord>();
        foreach (var buffer in _buffers.Values)
        {
            allRecords.AddRange(buffer.ReadRecent(count));
        }
        return allRecords
            .OrderByDescending(r => r.TimestampUtcTicks)
            .Take(count)
            .ToArray();
    }

    public MetricRecord[] ReadSince(string metricName, long timestampUtcTicks)
    {
        if (!_buffers.TryGetValue(metricName, out var buffer))
            return Array.Empty<MetricRecord>();
        return buffer.ReadSince(timestampUtcTicks);
    }

    public MetricRecord[] ReadAll(int limit = 1000)
    {
        var allRecords = new List<MetricRecord>();
        foreach (var buffer in _buffers.Values)
        {
            allRecords.AddRange(buffer.ReadRecent(limit));
        }
        return allRecords
            .OrderByDescending(r => r.TimestampUtcTicks)
            .Take(limit)
            .ToArray();
    }

    public IReadOnlyCollection<string> GetMetricNames() => _buffers.Keys.ToList();

    public (double Value, DateTime Timestamp)[] Snapshot(string metricName)
    {
        if (!_buffers.TryGetValue(metricName, out var buffer))
            return Array.Empty<(double, DateTime)>();
        return buffer.Snapshot();
    }

    public IReadOnlyDictionary<string, MetricBuffer> GetBuffers() => _buffers;

    public ChannelReader<MetricRecord> Subscribe()
    {
        var channel = Channel.CreateBounded<MetricRecord>(new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        });
        _subscribers.TryAdd(channel.Writer, channel.Reader);
        return channel.Reader;
    }

    public void Unsubscribe(ChannelReader<MetricRecord> reader)
    {
        foreach (var kvp in _subscribers)
        {
            if (ReferenceEquals(kvp.Value, reader))
            {
                _subscribers.TryRemove(kvp.Key, out _);
                kvp.Key.TryComplete();
                return;
            }
        }
    }

    public void Dispose()
    {
        foreach (var writer in _subscribers.Keys)
        {
            writer.TryComplete();
        }
        _subscribers.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NotifySubscribers(in MetricRecord record)
    {
        foreach (var writer in _subscribers.Keys)
        {
            writer.TryWrite(record);
        }
    }
}
