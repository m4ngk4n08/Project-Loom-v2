using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Loom.Telemetry;

namespace Loom.Storage;

/// <summary>
/// In-memory metric store backed by per-metric ring buffers.
/// Thread-safe, zero-allocation writes, bounded memory.
/// Supports real-time subscriptions via Channel.
/// Writes are zero-allocation EXCEPT for tagged counters: those build a canonical
/// key string per write (see <see cref="RecordCounterTotal"/>). Untagged counters
/// and every gauge/histogram write remain allocation-free.
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

    // Cumulative per-series counter totals, keyed by MetricSeriesKey.Build(...).
    // Monotonic: unaffected by ring-buffer wrap (see GetCounterTotals). Bounded by
    // _maxSeries - see RecordCounterTotal for the cap/admission logic.
    private readonly ConcurrentDictionary<string, CounterAccumulator> _counterTotals = new();
    private readonly int _maxSeries;
    private int _seriesCount;
    private int _capWarned;
    private readonly ILogger? _logger;

    public InMemoryMetricStore(int bufferCapacity = 8192, int maxSeries = 10_000, ILogger? logger = null)
    {
        _bufferCapacity = bufferCapacity;
        _maxSeries = maxSeries;
        _logger = logger;
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

        if (record.Type == MetricType.Counter)
            RecordCounterTotal(in record);

        NotifySubscribers(in record);
    }

    /// <summary>
    /// Adds this write's value to its series' cumulative total. Untagged counters
    /// (the common case) key on the metric name itself - no allocation. Tagged
    /// counters build a canonical key string via MetricSeriesKey - the one
    /// allocation on this otherwise allocation-free path. A zero-allocation variant
    /// (struct key + custom comparer + an already-sorted fast path) is deferred
    /// pending the BACKLOG § 6.3 benchmark harness.
    /// </summary>
    private void RecordCounterTotal(in MetricRecord record)
    {
        var sortedTags = MetricSeriesKey.SortTags(record.Tags);
        var key = MetricSeriesKey.Build(record.Name, sortedTags);

        if (_counterTotals.TryGetValue(key, out var existing))
        {
            existing.Add(record.Value);
            return;
        }

        // Cardinality cap: a memory guard, not a hard invariant. A concurrency race
        // here can admit a small number of series past _maxSeries - that is
        // acceptable and intentionally not locked (this is the hot write path).
        if (Volatile.Read(ref _seriesCount) >= _maxSeries)
        {
            WarnCapReachedOnce();
            return; // untracked - PrometheusFormatter falls back to buffer-summing
        }

        var created = new CounterAccumulator(record.Name, sortedTags, record.Value);
        if (_counterTotals.TryAdd(key, created))
        {
            // Do NOT use ConcurrentDictionary.Count here - it acquires every
            // internal lock. This separate counter, incremented only on admission,
            // is the cheap substitute.
            Interlocked.Increment(ref _seriesCount);
            return;
        }

        // Lost the race to another writer admitting the same series concurrently -
        // it's in the dictionary now, so fold this write into it.
        if (_counterTotals.TryGetValue(key, out existing))
            existing.Add(record.Value);
    }

    private void WarnCapReachedOnce()
    {
        // Guarded by a bool, not a lock: a duplicate warning under a race is
        // harmless, and this is the hot write path.
        if (Interlocked.CompareExchange(ref _capWarned, 1, 0) != 0)
            return;

        _logger?.LogWarning(
            "InMemoryMetricStore counter-series cap ({MaxSeries}) reached; further " +
            "distinct counter series will not be tracked for monotonic totals and " +
            "will fall back to ring-buffer summing. Reduce metric tag cardinality " +
            "(e.g. avoid tagging counters with user IDs, request IDs, or raw URLs).",
            _maxSeries);
    }

    public IReadOnlyCollection<CounterTotal> GetCounterTotals()
    {
        var totals = new List<CounterTotal>(_counterTotals.Count);
        foreach (var accumulator in _counterTotals.Values)
        {
            totals.Add(new CounterTotal(accumulator.MetricName, accumulator.Tags, accumulator.Total));
        }
        return totals;
    }

    /// <summary>
    /// Mutable per-series counter accumulator. Total is updated via a lock-free
    /// compare-and-swap loop rather than `+=`, which is not atomic on a double and
    /// would lose increments under concurrent writers.
    /// </summary>
    private sealed class CounterAccumulator
    {
        public string MetricName { get; }
        public MetricTag[] Tags { get; }
        private double _total;

        public CounterAccumulator(string metricName, MetricTag[] tags, double initial)
        {
            MetricName = metricName;
            Tags = tags;
            _total = initial;
        }

        public double Total => Volatile.Read(ref _total);

        public void Add(double delta)
        {
            double current, updated;
            do
            {
                current = Volatile.Read(ref _total);
                updated = current + delta;
            } while (Interlocked.CompareExchange(ref _total, updated, current) != current);
        }
    }

    public MetricRecord[] ReadRecent(string metricName, int count)
    {
        if (!_buffers.TryGetValue(metricName, out var buffer))
            return Array.Empty<MetricRecord>();
        return buffer.ReadRecent(count);
    }

    public MetricRecord[] ReadRecent(int count) => ReadRecentAcrossBuffers(count);

    public MetricRecord[] ReadSince(string metricName, long timestampUtcTicks)
    {
        if (!_buffers.TryGetValue(metricName, out var buffer))
            return Array.Empty<MetricRecord>();
        return buffer.ReadSince(timestampUtcTicks);
    }

    public MetricRecord[] ReadAll(int limit = 1000) => ReadRecentAcrossBuffers(limit);

    private MetricRecord[] ReadRecentAcrossBuffers(int count)
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
