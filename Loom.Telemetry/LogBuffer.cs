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
        var available = Math.Min(count, Math.Min((int)currentIndex, _buffer.Length));

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
    /// Read all logs written since a specific timestamp.
    /// Strict greater-than (unlike MetricBuffer.ReadSince, which uses >=): logs are
    /// commonly re-polled by a tailing client using the last-seen record's own
    /// timestamp as the next cursor, and >= would hand that boundary record back on
    /// every poll. Metric polling doesn't have that repeated-boundary caller today, so
    /// MetricBuffer was left as-is rather than risk changing its behavior.
    /// </summary>
    public LogRecord[] ReadSince(long timestampUtcTicks)
    {
        var currentIndex = Interlocked.Read(ref _writeIndex);
        var maxRead = Math.Min((int)currentIndex, _buffer.Length);

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

            if (record.TimestampUtcTicks > timestampUtcTicks)
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
