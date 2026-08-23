using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using Loom.Storage;
using Loom.Storage.Logging;
using Loom.Telemetry;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Loom.Telemetry.Tests.Storage;

/// <summary>
/// Characterization tests for InMemoryLogStore, mirroring InMemoryMetricStoreTests.
/// InMemoryLogStore holds no static state, so these tests construct their own
/// instance and need no global reset.
/// </summary>
public sealed class InMemoryLogStoreTests
{
    private static LogRecord Record(string message, string category, long ticks) =>
        new(message, category, LoomLogLevel.Information, ticks);

    // Fixed base so ordering assertions never depend on wall-clock resolution.
    private static readonly long Base = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    [Fact]
    public void ReadRecent_ReturnsNewestFirst()
    {
        using var store = new InMemoryLogStore();
        store.Write(Record("a", "cat", Base + 1));
        store.Write(Record("b", "cat", Base + 2));
        store.Write(Record("c", "cat", Base + 3));

        var result = store.ReadRecent(10);

        Assert.Equal(new[] { "c", "b", "a" }, result.Select(r => r.Message));
    }

    [Fact]
    public void ReadRecent_RespectsCount()
    {
        using var store = new InMemoryLogStore();
        for (var i = 1; i <= 5; i++) store.Write(Record($"m{i}", "cat", Base + i));

        Assert.Equal(2, store.ReadRecent(2).Length);
    }

    [Fact]
    public void ReadRecent_WrapsAtCapacity()
    {
        using var store = new InMemoryLogStore(bufferCapacity: 4);
        for (var i = 1; i <= 6; i++) store.Write(Record($"m{i}", "cat", Base + i));

        var result = store.ReadRecent(10);

        // Capacity 4, 6 writes -> only the newest 4 survive.
        Assert.Equal(new[] { "m6", "m5", "m4", "m3" }, result.Select(r => r.Message));
    }

    [Fact]
    public void ReadRecent_ByCategory_FiltersAndOrdersNewestFirst()
    {
        using var store = new InMemoryLogStore();
        store.Write(Record("a1", "a", Base + 1));
        store.Write(Record("b1", "b", Base + 2));
        store.Write(Record("a2", "a", Base + 3));

        var result = store.ReadRecent("a", 10);

        Assert.Equal(new[] { "a2", "a1" }, result.Select(r => r.Message));
    }

    [Fact]
    public void ReadRecent_ByCategory_RespectsCount()
    {
        using var store = new InMemoryLogStore();
        for (var i = 1; i <= 5; i++) store.Write(Record($"m{i}", "a", Base + i));

        Assert.Equal(2, store.ReadRecent("a", 2).Length);
    }

    [Fact]
    public void ReadRecent_ByCategory_UnknownCategory_ReturnsEmpty()
    {
        using var store = new InMemoryLogStore();
        store.Write(Record("a1", "a", Base));

        Assert.Empty(store.ReadRecent("nope", 10));
    }

    [Fact]
    public void ReadSince_IsExclusiveAtTheBoundary()
    {
        using var store = new InMemoryLogStore();
        store.Write(Record("a", "cat", Base + 1));
        store.Write(Record("b", "cat", Base + 5));
        store.Write(Record("c", "cat", Base + 9));

        var result = store.ReadSince(Base + 5);

        // Unlike MetricBuffer.ReadSince (>=), LogBuffer.ReadSince is strictly ">" -
        // the record exactly at the boundary (b, ticks Base+5) must not reappear.
        Assert.Equal(new[] { "c" }, result.Select(r => r.Message));
    }

    [Fact]
    public void GetCategories_ReturnsEveryWrittenCategory()
    {
        using var store = new InMemoryLogStore();
        store.Write(Record("a1", "alpha", Base));
        store.Write(Record("b1", "beta", Base));
        store.Write(Record("a2", "alpha", Base + 1));

        var categories = store.GetCategories();

        Assert.Equal(2, categories.Count);
        Assert.Contains("alpha", categories);
        Assert.Contains("beta", categories);
    }

    [Fact]
    public void GetCategories_EmptyStore_ReturnsEmpty()
    {
        using var store = new InMemoryLogStore();
        Assert.Empty(store.GetCategories());
    }

    // --- Subscription semantics ---

    [Fact]
    public void Subscribe_ReceivesSubsequentWrites()
    {
        using var store = new InMemoryLogStore();
        var reader = store.Subscribe();

        store.Write(Record("hello", "cat", Base));

        Assert.True(reader.TryRead(out var received));
        Assert.Equal("hello", received.Message);
    }

    [Fact]
    public void Subscribe_DoesNotReceiveWritesMadeBeforeSubscribing()
    {
        using var store = new InMemoryLogStore();
        store.Write(Record("a", "cat", Base));

        var reader = store.Subscribe();

        Assert.False(reader.TryRead(out _));
    }

    [Fact]
    public void Unsubscribe_StopsDeliveryAndCompletesThatChannelOnly()
    {
        using var store = new InMemoryLogStore();
        var removed = store.Subscribe();
        var kept = store.Subscribe();

        store.Unsubscribe(removed);
        store.Write(Record("a", "cat", Base));

        Assert.False(removed.TryRead(out _));
        Assert.True(removed.Completion.IsCompleted);

        Assert.True(kept.TryRead(out var record));
        Assert.Equal("a", record.Message);
        Assert.False(kept.Completion.IsCompleted);
    }

    [Fact]
    public void Unsubscribe_UnknownReader_IsNoOp()
    {
        using var store = new InMemoryLogStore();
        var kept = store.Subscribe();
        var foreign = Channel.CreateUnbounded<LogRecord>().Reader;

        store.Unsubscribe(foreign);
        store.Write(Record("a", "cat", Base));

        Assert.True(kept.TryRead(out _));
    }

    [Fact]
    public void Dispose_CompletesAllSubscribers()
    {
        var store = new InMemoryLogStore();
        var first = store.Subscribe();
        var second = store.Subscribe();

        store.Dispose();

        Assert.True(first.Completion.IsCompleted);
        Assert.True(second.Completion.IsCompleted);
    }

    // --- Logging provider integration ---

    [Fact]
    public void LoomCategory_WritesNothing()
    {
        using var store = new InMemoryLogStore();
        var provider = new LoomLoggerProvider(store);
        var logger = provider.CreateLogger("Loom.Internal.SomeComponent");

        logger.LogInformation("should not be captured");

        Assert.Empty(store.ReadRecent(10));
    }

    [Fact]
    public void NonLoomCategory_IsCaptured()
    {
        using var store = new InMemoryLogStore();
        var provider = new LoomLoggerProvider(store);
        var logger = provider.CreateLogger("MyApp.Services.OrderService");

        logger.LogWarning("careful now");

        var result = store.ReadRecent(10);
        Assert.Single(result);
        Assert.Equal("careful now", result[0].Message);
        Assert.Equal("MyApp.Services.OrderService", result[0].Category);
        Assert.Equal(LoomLogLevel.Warning, result[0].Level);
    }

    // ILogStore test double that logs again from inside its own Write - simulates a
    // sink that reacts to ingested logs by producing more logs. Without the
    // ThreadStatic guard in LoomLogger.Log, this recurses without bound.
    private sealed class ReentrantLogStore : ILogStore
    {
        public ILogger? Logger;
        public int WriteCount;

        public void Write(in LogRecord record)
        {
            WriteCount++;
            if (WriteCount == 1)
            {
                Logger!.LogInformation("recursive call");
            }
        }

        public LogRecord[] ReadRecent(int count) => Array.Empty<LogRecord>();
        public LogRecord[] ReadRecent(string category, int count) => Array.Empty<LogRecord>();
        public LogRecord[] ReadSince(long timestampUtcTicks) => Array.Empty<LogRecord>();
        public IReadOnlyCollection<string> GetCategories() => Array.Empty<string>();
        public ChannelReader<LogRecord> Subscribe() => Channel.CreateUnbounded<LogRecord>().Reader;
        public void Unsubscribe(ChannelReader<LogRecord> reader) { }
    }

    [Fact]
    public void ThreadStaticGuard_PreventsReentrantLoggingFromRecursing()
    {
        var store = new ReentrantLogStore();
        var logger = new LoomLogger("MyApp.Reentrant", store);
        store.Logger = logger;

        logger.LogInformation("outer message");

        // Only the outer call reached Write. The reentrant call made from inside
        // Write was swallowed by the guard instead of recursing.
        Assert.Equal(1, store.WriteCount);
    }
}
