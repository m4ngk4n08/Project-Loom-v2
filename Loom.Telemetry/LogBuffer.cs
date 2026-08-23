using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Loom.Telemetry;

/// <summary>
/// Lock-free circular buffer for storing log records.
/// Zero-allocation writes, bounded memory.
/// </summary>
public sealed class LogBuffer
{
    private const int DefaultCapacity = 8192;
    private readonly LogRecord[] _buffer;
    private readonly int _mask;
    private long _writeIndex;

    public LogBuffer(int capacity = DefaultCapacity)
    {
        // Round up to next power of 2 for fast modulo via bitwise AND
        capacity = RoundUpToPowerOfTwo(capacity);
        _buffer = new LogRecord[capacity];
        _mask = capacity - 1;
        _writeIndex = 0;
    }

    public int Capacity => _buffer.Length;

    /// <summary>
    /// Monotonic write sequence: the number of records written so far. Pass this (as
    /// captured in a LogReadResult.NextSequence) back into ReadAfter to resume a tail
    /// read from this point.
    /// </summary>
    public long CurrentSequence => Interlocked.Read(ref _writeIndex);

    /// <summary>
    /// Write a log record to the buffer (lock-free, wait-free).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(in LogRecord record)
    {
        // Atomically increment write index and get slot
        var index = Interlocked.Increment(ref _writeIndex) - 1;
        var slot = (int)(index & _mask);

        // Write to slot (overwrites old data if buffer is full)
        _buffer[slot] = record;
    }

    /// <summary>
    /// Read recent logs (non-blocking snapshot).
    /// Returns up to 'count' most recent records, newest first.
    /// </summary>
    public LogRecord[] ReadRecent(int count)
    {
        if (count <= 0)
            return Array.Empty<LogRecord>();

        var currentIndex = Interlocked.Read(ref _writeIndex);
        // Clamp in long space before casting to int - currentIndex can exceed
        // int.MaxValue (~2.4 days at 10k writes/sec) and a truncating cast would go
        // negative, making the subsequent Math.Min/array allocation blow up.
        var live = (int)Math.Min(currentIndex, _buffer.Length);
        var available = Math.Min(count, live);

        if (available == 0)
            return Array.Empty<LogRecord>();

        var result = new LogRecord[available];

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
    /// Read recent logs into a caller-provided span (zero-allocation snapshot).
    /// Fills newest-first, writes no more than destination.Length entries.
    /// Returns the number of records written.
    /// </summary>
    public int TryReadRecent(Span<LogRecord> destination)
    {
        if (destination.Length == 0)
            return 0;

        var currentIndex = Interlocked.Read(ref _writeIndex);
        var live = (int)Math.Min(currentIndex, _buffer.Length);
        var available = Math.Min(destination.Length, live);

        for (int i = 0; i < available; i++)
        {
            var readIndex = currentIndex - 1 - i;
            var slot = (int)(readIndex & _mask);
            destination[i] = _buffer[slot];
        }

        return available;
    }

    /// <summary>
    /// Read all logs timestamped at or after a specific instant (inclusive "since",
    /// like MetricBuffer.ReadSince). This is a TIME-RANGE query - "logs in the last 5
    /// minutes" - not a tail cursor: DateTime.UtcNow.Ticks has coarse resolution (on
    /// .NET 10, ~79% of ticks collide under tight-loop logging), so polling this with
    /// the last-seen record's own timestamp as the next "since" value silently drops
    /// every other record sharing that tick. For resumable tailing, use the sequence
    /// cursor (CurrentSequence / ReadAfter) instead, which is exact.
    /// </summary>
    public LogRecord[] ReadSince(long timestampUtcTicks)
    {
        var currentIndex = Interlocked.Read(ref _writeIndex);
        var maxRead = (int)Math.Min(currentIndex, _buffer.Length);

        if (maxRead == 0)
            return Array.Empty<LogRecord>();

        var temp = new LogRecord[maxRead];
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
            return Array.Empty<LogRecord>();

        var result = new LogRecord[matchCount];
        Array.Copy(temp, result, matchCount);
        return result;
    }

    /// <summary>
    /// Read all logs written after a specific sequence cursor (exclusive), oldest
    /// first - the resumable-tail counterpart to ReadSince's time-range query.
    /// Sequence numbers are exact (derived from the write-index counter), so unlike
    /// timestamp-based polling this cannot silently coalesce or drop same-tick
    /// records. If the caller's cursor has fallen behind the buffer's live window,
    /// the missed records are reported via DroppedCount rather than silently skipped.
    /// </summary>
    public LogReadResult ReadAfter(long afterSequence)
    {
        if (afterSequence < 0)
            afterSequence = 0;

        var currentIndex = Interlocked.Read(ref _writeIndex);

        if (afterSequence >= currentIndex)
            return new LogReadResult(Array.Empty<LogRecord>(), currentIndex, 0);

        // Oldest sequence number still live in the buffer (1-based; sequence s lives
        // at slot (s - 1) & mask). Everything older than this has been overwritten.
        var oldestLiveSequence = currentIndex > _buffer.Length
            ? currentIndex - _buffer.Length + 1
            : 1L;

        var droppedCount = 0;
        var effectiveAfter = afterSequence;
        if (effectiveAfter < oldestLiveSequence - 1)
        {
            droppedCount = (int)Math.Min(oldestLiveSequence - 1 - effectiveAfter, int.MaxValue);
            effectiveAfter = oldestLiveSequence - 1;
        }

        var count = (int)Math.Min(currentIndex - effectiveAfter, _buffer.Length);
        var result = new LogRecord[count];

        // Oldest first: natural replay order for a tail reader.
        for (var i = 0; i < count; i++)
        {
            var seq = effectiveAfter + 1 + i;
            var slot = (int)((seq - 1) & _mask);
            result[i] = _buffer[slot];
        }

        return new LogReadResult(result, currentIndex, droppedCount);
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
