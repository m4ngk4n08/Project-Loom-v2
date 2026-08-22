using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Loom.Telemetry;

/// <summary>
/// Lock-free circular buffer for storing metric records.
/// Zero-allocation writes, bounded memory.
/// NOTE: Public for query engine access (Phase 10), but not part of main API surface.
/// </summary>
public sealed class MetricBuffer
{
    private const int DefaultCapacity = 8192;
    private readonly MetricRecord[] _buffer;
    private readonly int _mask;
    private long _writeIndex;

    public MetricBuffer(int capacity = DefaultCapacity)
    {
        // Round up to next power of 2 for fast modulo via bitwise AND
        capacity = RoundUpToPowerOfTwo(capacity);
        _buffer = new MetricRecord[capacity];
        _mask = capacity - 1;
        _writeIndex = 0;
    }

    public int Capacity => _buffer.Length;

    /// <summary>
    /// Write a metric record to the buffer (lock-free, wait-free).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(in MetricRecord record)
    {
        // Atomically increment write index and get slot
        var index = Interlocked.Increment(ref _writeIndex) - 1;
        var slot = (int)(index & _mask);

        // Write to slot (overwrites old data if buffer is full)
        _buffer[slot] = record;
    }

    /// <summary>
    /// Read recent metrics (non-blocking snapshot).
    /// Returns up to 'count' most recent records.
    /// </summary>
    public MetricRecord[] ReadRecent(int count)
    {
        if (count <= 0)
            return Array.Empty<MetricRecord>();

        var currentIndex = Interlocked.Read(ref _writeIndex);
        var available = Math.Min(count, Math.Min((int)currentIndex, _buffer.Length));

        if (available == 0)
            return Array.Empty<MetricRecord>();

        var result = new MetricRecord[available];

        // Read backwards from most recent
        for (int i = 0; i < available; i++)
        {
            var readIndex = currentIndex - 1 - i;
            var slot = (int)(readIndex & _mask);
            result[i] = _buffer[slot];
        }

        return result;
    }

    /// <summary>
    /// Read recent metrics into a caller-provided span (zero-allocation snapshot).
    /// Fills newest-first, writes no more than destination.Length entries.
    /// Returns the number of records written.
    /// </summary>
    public int TryReadRecent(Span<MetricRecord> destination)
    {
        if (destination.Length == 0)
            return 0;

        var currentIndex = Interlocked.Read(ref _writeIndex);
        var available = Math.Min(destination.Length, Math.Min((int)currentIndex, _buffer.Length));

        for (int i = 0; i < available; i++)
        {
            var readIndex = currentIndex - 1 - i;
            var slot = (int)(readIndex & _mask);
            destination[i] = _buffer[slot];
        }

        return available;
    }

    /// <summary>
    /// Read all metrics written since a specific timestamp.
    /// </summary>
    public MetricRecord[] ReadSince(long timestampUtcTicks)
    {
        var currentIndex = Interlocked.Read(ref _writeIndex);
        var maxRead = Math.Min((int)currentIndex, _buffer.Length);

        if (maxRead == 0)
            return Array.Empty<MetricRecord>();

        var temp = new MetricRecord[maxRead];
        int matchCount = 0;

        // Scan buffer for matching records
        for (int i = 0; i < maxRead; i++)
        {
            var readIndex = currentIndex - 1 - i;
            var slot = (int)(readIndex & _mask);
            var record = _buffer[slot];

            if (record.TimestampUtcTicks >= timestampUtcTicks)
            {
                temp[matchCount++] = record;
            }
        }

        // Return exact-sized array
        if (matchCount == 0)
            return Array.Empty<MetricRecord>();

        var result = new MetricRecord[matchCount];
        Array.Copy(temp, result, matchCount);
        return result;
    }

    /// <summary>
    /// Get snapshot of all entries in buffer (for Phase 10 query executor).
    /// Returns array of (Value, Timestamp) entries.
    /// </summary>
    public (double Value, DateTime Timestamp)[] Snapshot()
    {
        var currentIndex = Interlocked.Read(ref _writeIndex);
        var maxRead = Math.Min((int)currentIndex, _buffer.Length);

        if (maxRead == 0)
            return Array.Empty<(double, DateTime)>();

        var result = new (double, DateTime)[maxRead];

        for (int i = 0; i < maxRead; i++)
        {
            var readIndex = currentIndex - 1 - i;
            var slot = (int)(readIndex & _mask);
            var record = _buffer[slot];
            result[i] = (record.Value, new DateTime(record.TimestampUtcTicks, DateTimeKind.Utc));
        }

        return result;
    }

    private static int RoundUpToPowerOfTwo(int value)
    {
        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        return value + 1;
    }
}
