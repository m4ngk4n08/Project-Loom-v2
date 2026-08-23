using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Loom.Telemetry;

namespace Loom.Storage;

/// <summary>
/// In-memory log store backed by a single global ring buffer.
/// Thread-safe, zero-allocation writes, bounded memory.
/// Supports real-time subscriptions via Channel.
/// Logs are one chronological stream (unlike metrics, which get one buffer per
/// name) - category is applied as a read-time filter over that single buffer.
/// </summary>
public sealed class InMemoryLogStore : ILogStore, IDisposable
{
    private readonly LogBuffer _buffer;
    private readonly ConcurrentDictionary<string, byte> _categories = new();

    // Subscribers are copy-on-write rather than a ConcurrentDictionary: the read side runs
    // on every Write (the hottest path in the system) while the write side only runs on
    // Subscribe/Unsubscribe, which is rare. ConcurrentDictionary.Keys acquires *all* of the
    // dictionary's internal locks and allocates an array plus a ReadOnlyCollection wrapper
    // on each access, so notifying subscribers was allocating and contending once per
    // log write - directly contradicting ADR-5's zero-allocation write guarantee.
    // A volatile array read is a single field load: no locks, no allocation.
    private readonly Lock _subscriberLock = new();
    private volatile Channel<LogRecord>[] _subscribers = [];

    public InMemoryLogStore(int bufferCapacity = 8192)
    {
        _buffer = new LogBuffer(bufferCapacity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(in LogRecord record)
    {
        _categories.TryAdd(record.Category, 0);
        _buffer.Write(in record);
        NotifySubscribers(in record);
    }

    public LogRecord[] ReadRecent(int count) => _buffer.ReadRecent(count);

    public LogRecord[] ReadRecent(string category, int count)
    {
        if (count <= 0)
            return Array.Empty<LogRecord>();

        // Scan the whole buffer (newest first) for matches, then cap at count - the
        // global buffer isn't partitioned by category, so filtering has to happen at
        // read time. Uses the pooled-span overload rather than ReadRecent(Capacity)
        // so scanning the full buffer doesn't allocate a full-capacity array (~48
        // bytes/record) on every call regardless of how small count is.
        var capacity = _buffer.Capacity;
        var pooled = ArrayPool<LogRecord>.Shared.Rent(capacity);
        try
        {
            var span = pooled.AsSpan(0, capacity);
            var available = _buffer.TryReadRecent(span);

            var result = new List<LogRecord>(Math.Min(count, available));
            for (var i = 0; i < available; i++)
            {
                if (result.Count == count)
                    break;
                if (span[i].Category == category)
                    result.Add(span[i]);
            }

            return result.ToArray();
        }
        finally
        {
            // clearArray: true - LogRecord holds string references (Message,
            // Category, exception text); a pooled array left dirty would keep dead
            // log messages alive until the pool happens to reuse or drop the slot.
            ArrayPool<LogRecord>.Shared.Return(pooled, clearArray: true);
        }
    }

    public LogRecord[] ReadSince(long timestampUtcTicks) => _buffer.ReadSince(timestampUtcTicks);

    public LogReadResult ReadAfter(long afterSequence) => _buffer.ReadAfter(afterSequence);

    public long CurrentSequence => _buffer.CurrentSequence;

    /// <summary>
    /// Snapshot of the currently-known log categories.
    /// Enumerates the dictionary directly instead of using .Keys/.Count - both of those
    /// acquire every internal lock, briefly stalling all writers, and .Keys.ToList()
    /// allocated three times over (array, ReadOnlyCollection wrapper, then the List).
    /// Direct enumeration is lock-free and weakly consistent: a category added
    /// concurrently may or may not appear. That is fine for a name listing, and is the
    /// documented way to iterate a ConcurrentDictionary without blocking writers.
    /// </summary>
    public IReadOnlyCollection<string> GetCategories()
    {
        var categories = new List<string>();
        foreach (var kvp in _categories)
        {
            categories.Add(kvp.Key);
        }
        return categories;
    }

    public ChannelReader<LogRecord> Subscribe()
    {
        var channel = Channel.CreateBounded<LogRecord>(new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        });
        lock (_subscriberLock)
        {
            var current = _subscribers;
            var updated = new Channel<LogRecord>[current.Length + 1];
            Array.Copy(current, updated, current.Length);
            updated[current.Length] = channel;
            _subscribers = updated;
        }

        return channel.Reader;
    }

    public void Unsubscribe(ChannelReader<LogRecord> reader)
    {
        Channel<LogRecord> removed;

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
            var updated = new Channel<LogRecord>[current.Length - 1];
            Array.Copy(current, 0, updated, 0, index);
            Array.Copy(current, index + 1, updated, index, current.Length - index - 1);
            _subscribers = updated;
        }

        // Complete outside the lock: TryComplete can run continuations inline.
        removed.Writer.TryComplete();
    }

    public void Dispose()
    {
        Channel<LogRecord>[] current;
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
    private void NotifySubscribers(in LogRecord record)
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
