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
    private readonly int _bufferCapacity;

    // Subscribers are copy-on-write rather than a ConcurrentDictionary: the read side runs
    // on every Write (the hottest path in the system) while the write side only runs on
    // Subscribe/Unsubscribe, which is rare. ConcurrentDictionary.Keys acquires *all* of the
    // dictionary's internal locks and allocates an array plus a ReadOnlyCollection wrapper
    // on each access, so notifying subscribers was allocating and contending once per
    // metric write - directly contradicting ADR-5's zero-allocation write guarantee.
    // A volatile array read is a single field load: no locks, no allocation.
    private readonly Lock _subscriberLock = new();
    private volatile Channel<MetricRecord>[] _subscribers = [];

    public InMemoryMetricStore(int bufferCapacity = 8192)
    {
        _bufferCapacity = bufferCapacity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(in MetricRecord record)
    {
        // static lambda + the state-passing GetOrAdd overload: a capturing lambda would
        // close over `this` for _bufferCapacity and allocate a closure on every write.
        var buffer = _buffers.GetOrAdd(
            record.Name,
            static (_, capacity) => new MetricBuffer(capacity),
            _bufferCapacity);
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

    /// <summary>
    /// Snapshot of the currently-known metric names.
    /// Enumerates the dictionary directly instead of using .Keys/.Count - both of those
    /// acquire every internal lock, briefly stalling all writers, and .Keys.ToList()
    /// allocated three times over (array, ReadOnlyCollection wrapper, then the List).
    /// Direct enumeration is lock-free and weakly consistent: a name added concurrently may
    /// or may not appear. That is fine for a name listing, and is the documented way to
    /// iterate a ConcurrentDictionary without blocking writers.
    /// </summary>
    public IReadOnlyCollection<string> GetMetricNames()
    {
        var names = new List<string>();
        foreach (var kvp in _buffers)
        {
            names.Add(kvp.Key);
        }
        return names;
    }

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
        lock (_subscriberLock)
        {
            var current = _subscribers;
            var updated = new Channel<MetricRecord>[current.Length + 1];
            Array.Copy(current, updated, current.Length);
            updated[current.Length] = channel;
            _subscribers = updated;
        }

        return channel.Reader;
    }

    public void Unsubscribe(ChannelReader<MetricRecord> reader)
    {
        Channel<MetricRecord> removed;

        lock (_subscriberLock)
        {
            var current = _subscribers;
            var index = -1;
            for (var i = 0; i < current.Length; i++)
            {
                if (ReferenceEquals(current[i].Reader, reader))
                {
                    index = i;
                    break;
                }
            }

            // Unknown reader, or already unsubscribed - no-op, as before.
            if (index < 0) return;

            removed = current[index];
            var updated = new Channel<MetricRecord>[current.Length - 1];
            Array.Copy(current, 0, updated, 0, index);
            Array.Copy(current, index + 1, updated, index, current.Length - index - 1);
            _subscribers = updated;
        }

        // Complete outside the lock: TryComplete can run continuations inline.
        removed.Writer.TryComplete();
    }

    public void Dispose()
    {
        Channel<MetricRecord>[] current;
        lock (_subscriberLock)
        {
            current = _subscribers;
            _subscribers = [];
        }

        foreach (var channel in current)
        {
            channel.Writer.TryComplete();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NotifySubscribers(in MetricRecord record)
    {
        // Single volatile read of the current array, then an index loop: no lock taken and
        // nothing allocated, so Write() stays allocation-free per ADR-5. A subscriber added
        // or removed concurrently is picked up on the next write.
        var subscribers = _subscribers;
        for (var i = 0; i < subscribers.Length; i++)
        {
            subscribers[i].Writer.TryWrite(record);
        }
    }
}
