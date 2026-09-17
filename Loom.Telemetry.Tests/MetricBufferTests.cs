using System;
using System.Reflection;
using Xunit;

namespace Loom.Telemetry.Tests;

/// <summary>
/// Direct unit tests of MetricBuffer's configurable capacity and DroppedCount, isolated
/// from LoomMetrics so they don't depend on the process-wide env-var-derived capacity.
/// </summary>
public sealed class MetricBufferTests
{
    [Fact]
    public void ReadRecent_NeverReturnsMoreThanConfiguredCapacity()
    {
        var buffer = new MetricBuffer(capacity: 16);
        Assert.Equal(16, buffer.Capacity);

        for (var i = 0; i < 100; i++)
        {
            buffer.Write(new MetricRecord("test.small", MetricType.Counter, i, DateTime.UtcNow.Ticks));
        }

        var recent = buffer.ReadRecent(1000);
        Assert.True(recent.Length <= 16, $"Expected at most 16 records, got {recent.Length}");
        Assert.Equal(16, recent.Length);
    }

    [Fact]
    public void DroppedCount_IsZero_BeforeTheBufferFills()
    {
        var buffer = new MetricBuffer(capacity: 8);

        for (var i = 0; i < 5; i++)
        {
            buffer.Write(new MetricRecord("test.notfull", MetricType.Counter, i, DateTime.UtcNow.Ticks));
        }

        Assert.Equal(0, buffer.DroppedCount);
    }

    [Fact]
    public void DroppedCount_EqualsWritesMinusCapacity_OnceWrapped()
    {
        const int capacity = 8;
        const int overwrite = 13;
        var buffer = new MetricBuffer(capacity);

        for (var i = 0; i < capacity + overwrite; i++)
        {
            buffer.Write(new MetricRecord("test.wrapped", MetricType.Counter, i, DateTime.UtcNow.Ticks));
        }

        Assert.Equal(overwrite, buffer.DroppedCount);
    }

    [Fact]
    public void Capacity_RoundsUpToNextPowerOfTwo()
    {
        var buffer = new MetricBuffer(capacity: 100);
        Assert.Equal(128, buffer.Capacity);
    }

    // Reflection is allowed here (test-only) to jump _writeIndex past int.MaxValue
    // without actually performing 2^31+ writes.
    private static void SetWriteIndex(MetricBuffer buffer, long value)
    {
        var field = typeof(MetricBuffer).GetField("_writeIndex", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("MetricBuffer._writeIndex field not found.");
        field.SetValue(buffer, value);
    }

    [Fact]
    public void ReadOperations_DoNotThrow_WhenWriteIndexExceedsIntMaxValue()
    {
        var buffer = new MetricBuffer(capacity: 16);

        for (var i = 0; i < 16; i++)
            buffer.Write(new MetricRecord("test.overflow", MetricType.Counter, i, DateTime.UtcNow.Ticks));

        SetWriteIndex(buffer, (long)int.MaxValue + 17);

        for (var i = 0; i < 16; i++)
            buffer.Write(new MetricRecord("test.overflow", MetricType.Counter, i, DateTime.UtcNow.Ticks));

        var recentException = Record.Exception(() => buffer.ReadRecent(16));
        Assert.Null(recentException);
        Assert.Equal(16, buffer.ReadRecent(16).Length);

        var destinationArray = new MetricRecord[16];
        var tryReadException = Record.Exception(() => buffer.TryReadRecent(destinationArray));
        Assert.Null(tryReadException);
        Assert.Equal(16, buffer.TryReadRecent(destinationArray));

        var sinceException = Record.Exception(() => buffer.ReadSince(0));
        Assert.Null(sinceException);
        Assert.Equal(16, buffer.ReadSince(0).Length);

        var snapshotException = Record.Exception(() => buffer.Snapshot());
        Assert.Null(snapshotException);
        Assert.Equal(16, buffer.Snapshot().Length);
    }

    [Fact]
    public void ReadRecent_ReturnsFullCapacity_WhenWriteIndexTruncatesToASmallPositiveValue()
    {
        var buffer = new MetricBuffer(capacity: 16);

        for (var i = 0; i < 16; i++)
            buffer.Write(new MetricRecord("test.wraps", MetricType.Counter, i, DateTime.UtcNow.Ticks));

        // (int) truncating cast of (2^32 + 5) wraps to a small positive value (5). The
        // buffer is genuinely long past its capacity here (2^32+5 >> 16), so the correct
        // clamped answer is 16 (full capacity), not 5 - the wrapped value would previously
        // have silently under-reported instead of clamping correctly.
        SetWriteIndex(buffer, (1L << 32) + 5);

        var exception = Record.Exception(() => buffer.ReadRecent(16));
        Assert.Null(exception);
        Assert.Equal(16, buffer.ReadRecent(16).Length);
    }
}
