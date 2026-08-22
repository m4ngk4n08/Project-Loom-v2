using System;
using System.Linq;
using System.Threading.Channels;
using Loom.Storage;
using Loom.Telemetry;
using Xunit;

namespace Loom.Telemetry.Tests.Storage;

/// <summary>
/// Characterization tests for InMemoryMetricStore, written before the D3b
/// allocation/locking refactor so the existing behavior is pinned first.
///
/// InMemoryMetricStore holds no static state (unlike LoomMetrics), so these tests
/// construct their own instance and need no global reset.
/// </summary>
public sealed class InMemoryMetricStoreTests
{
    private static MetricRecord Record(string name, double value, long ticks) =>
        new(name, MetricType.Gauge, value, ticks);

    // Fixed base so ordering assertions never depend on wall-clock resolution.
    private static readonly long Base = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    [Fact]
    public void ReadRecent_ByName_ReturnsNewestFirst()
    {
        using var store = new InMemoryMetricStore();
        store.Write(Record("a", 1, Base + 1));
        store.Write(Record("a", 2, Base + 2));
        store.Write(Record("a", 3, Base + 3));

        var result = store.ReadRecent("a", 10);

        Assert.Equal(new[] { 3.0, 2.0, 1.0 }, result.Select(r => r.Value));
    }

    [Fact]
    public void ReadRecent_ByName_RespectsCount()
    {
        using var store = new InMemoryMetricStore();
        for (var i = 1; i <= 5; i++) store.Write(Record("a", i, Base + i));

        Assert.Equal(2, store.ReadRecent("a", 2).Length);
    }

    [Fact]
    public void ReadRecent_UnknownName_ReturnsEmpty()
    {
        using var store = new InMemoryMetricStore();
        Assert.Empty(store.ReadRecent("nope", 10));
    }

    [Fact]
    public void ReadRecent_AllBuffers_OrdersByTimestampDescendingAcrossMetrics()
    {
        using var store = new InMemoryMetricStore();
        store.Write(Record("a", 10, Base + 1));
        store.Write(Record("b", 20, Base + 3));
        store.Write(Record("c", 30, Base + 2));

        var result = store.ReadRecent(10);

        Assert.Equal(new[] { 20.0, 30.0, 10.0 }, result.Select(r => r.Value));
    }

    [Fact]
    public void ReadRecent_AllBuffers_CapsAtCount()
    {
        using var store = new InMemoryMetricStore();
        store.Write(Record("a", 10, Base + 1));
        store.Write(Record("b", 20, Base + 3));
        store.Write(Record("c", 30, Base + 2));

        var result = store.ReadRecent(2);

        Assert.Equal(new[] { 20.0, 30.0 }, result.Select(r => r.Value));
    }

    [Fact]
    public void ReadAll_OrdersByTimestampDescendingAndCapsAtLimit()
    {
        using var store = new InMemoryMetricStore();
        store.Write(Record("a", 10, Base + 1));
        store.Write(Record("b", 20, Base + 3));
        store.Write(Record("c", 30, Base + 2));

        Assert.Equal(new[] { 20.0, 30.0, 10.0 }, store.ReadAll().Select(r => r.Value));
        Assert.Equal(new[] { 20.0 }, store.ReadAll(1).Select(r => r.Value));
    }

    [Fact]
    public void ReadSince_FiltersByTimestamp()
    {
        using var store = new InMemoryMetricStore();
        store.Write(Record("a", 1, Base + 1));
        store.Write(Record("a", 2, Base + 5));
        store.Write(Record("a", 3, Base + 9));

        var result = store.ReadSince("a", Base + 5);

        Assert.Equal(new[] { 3.0, 2.0 }, result.Select(r => r.Value));
        Assert.Empty(store.ReadSince("nope", Base));
    }

    [Fact]
    public void GetMetricNames_ReturnsEveryWrittenName()
    {
        using var store = new InMemoryMetricStore();
        store.Write(Record("alpha", 1, Base));
        store.Write(Record("beta", 1, Base));
        store.Write(Record("alpha", 2, Base + 1)); // duplicate name, same buffer

        var names = store.GetMetricNames();

        Assert.Equal(2, names.Count);
        Assert.Contains("alpha", names);
        Assert.Contains("beta", names);
    }

    [Fact]
    public void GetMetricNames_EmptyStore_ReturnsEmpty()
    {
        using var store = new InMemoryMetricStore();
        Assert.Empty(store.GetMetricNames());
    }

    [Fact]
    public void Snapshot_ReturnsValuesNewestFirst_AndEmptyForUnknown()
    {
        using var store = new InMemoryMetricStore();
        store.Write(Record("a", 1, Base + 1));
        store.Write(Record("a", 2, Base + 2));

        var snapshot = store.Snapshot("a");

        Assert.Equal(new[] { 2.0, 1.0 }, snapshot.Select(s => s.Value));
        Assert.Empty(store.Snapshot("nope"));
    }

    [Fact]
    public void GetBuffers_ExposesOneBufferPerMetricName()
    {
        using var store = new InMemoryMetricStore();
        store.Write(Record("a", 1, Base));
        store.Write(Record("b", 1, Base));

        var buffers = store.GetBuffers();

        Assert.Equal(2, buffers.Count);
        Assert.True(buffers.ContainsKey("a"));
        Assert.True(buffers.ContainsKey("b"));
    }

    // --- Subscription semantics (the surface D3b's copy-on-write change touches) ---

    [Fact]
    public void Subscribe_ReceivesSubsequentWrites()
    {
        using var store = new InMemoryMetricStore();
        var reader = store.Subscribe();

        store.Write(Record("a", 42, Base));

        Assert.True(reader.TryRead(out var received));
        Assert.Equal(42, received.Value);
        Assert.Equal("a", received.Name);
    }

    [Fact]
    public void Subscribe_DoesNotReceiveWritesMadeBeforeSubscribing()
    {
        using var store = new InMemoryMetricStore();
        store.Write(Record("a", 1, Base));

        var reader = store.Subscribe();

        Assert.False(reader.TryRead(out _));
    }

    [Fact]
    public void MultipleSubscribers_AllReceiveEveryWrite()
    {
        using var store = new InMemoryMetricStore();
        var first = store.Subscribe();
        var second = store.Subscribe();
        var third = store.Subscribe();

        store.Write(Record("a", 7, Base));

        foreach (var reader in new[] { first, second, third })
        {
            Assert.True(reader.TryRead(out var record));
            Assert.Equal(7, record.Value);
        }
    }

    [Fact]
    public void Unsubscribe_StopsDeliveryAndCompletesThatChannelOnly()
    {
        using var store = new InMemoryMetricStore();
        var removed = store.Subscribe();
        var kept = store.Subscribe();

        store.Unsubscribe(removed);
        store.Write(Record("a", 5, Base));

        // Removed subscriber gets nothing further and is completed.
        Assert.False(removed.TryRead(out _));
        Assert.True(removed.Completion.IsCompleted);

        // Surviving subscriber is unaffected.
        Assert.True(kept.TryRead(out var record));
        Assert.Equal(5, record.Value);
        Assert.False(kept.Completion.IsCompleted);
    }

    [Fact]
    public void Unsubscribe_UnknownReader_IsNoOp()
    {
        using var store = new InMemoryMetricStore();
        var kept = store.Subscribe();
        var foreign = Channel.CreateUnbounded<MetricRecord>().Reader;

        store.Unsubscribe(foreign);
        store.Write(Record("a", 1, Base));

        Assert.True(kept.TryRead(out _));
    }

    [Fact]
    public void Unsubscribe_SameReaderTwice_IsNoOp()
    {
        using var store = new InMemoryMetricStore();
        var reader = store.Subscribe();

        store.Unsubscribe(reader);
        store.Unsubscribe(reader);

        Assert.True(reader.Completion.IsCompleted);
    }

    [Fact]
    public void Dispose_CompletesAllSubscribers()
    {
        var store = new InMemoryMetricStore();
        var first = store.Subscribe();
        var second = store.Subscribe();

        store.Dispose();

        Assert.True(first.Completion.IsCompleted);
        Assert.True(second.Completion.IsCompleted);
    }

    [Fact]
    public void Write_WithNoSubscribers_DoesNotThrow()
    {
        using var store = new InMemoryMetricStore();
        store.Write(Record("a", 1, Base));
        Assert.Single(store.GetMetricNames());
    }

    [Fact]
    public void Subscriber_ExceedingCapacity_DropsOldestRatherThanBlocking()
    {
        using var store = new InMemoryMetricStore();
        var reader = store.Subscribe();

        // Bounded at 1024 with DropOldest; write past capacity.
        for (var i = 0; i < 1100; i++) store.Write(Record("a", i, Base + i));

        Assert.True(reader.TryRead(out var oldestRetained));
        // The first 76 writes were dropped, so the oldest retained is #76.
        Assert.Equal(76, oldestRetained.Value);
    }
}
