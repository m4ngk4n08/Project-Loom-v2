using System;
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
}
