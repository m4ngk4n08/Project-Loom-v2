using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
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
    public void ReadSince_IsInclusiveAtTheBoundary()
    {
        using var store = new InMemoryLogStore();
        store.Write(Record("a", "cat", Base + 1));
        store.Write(Record("b", "cat", Base + 5));
        store.Write(Record("c", "cat", Base + 9));

        var result = store.ReadSince(Base + 5);

        // Matches MetricBuffer.ReadSince: inclusive ">=". This is a time-range query,
        // not a tail cursor - ReadAfter (sequence-based) is exact for tailing.
        Assert.Equal(new[] { "c", "b" }, result.Select(r => r.Message));
    }

    [Fact]
    public void ReadAfter_ReportsDroppedCount_WhenCursorHasBeenOverwritten()
    {
        using var store = new InMemoryLogStore(bufferCapacity: 4);
        for (var i = 1; i <= 3; i++) store.Write(Record($"m{i}", "cat", Base + i));

        // Cursor after the very first record (sequence 1); nothing has wrapped yet.
        var afterFirst = store.CurrentSequence - 2; // sequence of m1 == 1 == CurrentSequence(3) - 2

        for (var i = 4; i <= 6; i++) store.Write(Record($"m{i}", "cat", Base + i)); // now 6 writes, capacity 4

        var result = store.ReadAfter(afterFirst);

        // Only m3..m6 are still live (capacity 4); m2 was overwritten and dropped.
        Assert.True(result.DroppedCount > 0);
        Assert.Equal(new[] { "m3", "m4", "m5", "m6" }, result.Records.Select(r => r.Message));
        Assert.Equal(store.CurrentSequence, result.NextSequence);
    }

    [Fact]
    public void ReadAfter_CursorAtCurrentSequence_ReturnsEmpty()
    {
        using var store = new InMemoryLogStore();
        store.Write(Record("a", "cat", Base));

        var result = store.ReadAfter(store.CurrentSequence);

        Assert.Empty(result.Records);
        Assert.Equal(0, result.DroppedCount);
        Assert.Equal(store.CurrentSequence, result.NextSequence);
    }

    [Fact]
    public void ReadAfter_NegativeCursor_ReplaysEverythingBuffered()
    {
        using var store = new InMemoryLogStore();
        store.Write(Record("a", "cat", Base));
        store.Write(Record("b", "cat", Base + 1));

        var result = store.ReadAfter(-1);

        Assert.Equal(0, result.DroppedCount);
        Assert.Equal(new[] { "a", "b" }, result.Records.Select(r => r.Message));
    }

    [Fact]
    public void ReadRecent_ByCategory_CountMuchSmallerThanCapacity_StillFiltersCorrectly()
    {
        using var store = new InMemoryLogStore(); // default capacity 8192
        for (var i = 1; i <= 20; i++)
            store.Write(Record($"m{i}", i % 2 == 0 ? "even" : "odd", Base + i));

        var result = store.ReadRecent("even", 3);

        Assert.Equal(new[] { "m20", "m18", "m16" }, result.Select(r => r.Message));
    }

    [Fact]
    public void SameTickRecords_ReadAfterReassemblesExactlyWithNoGapsOrDuplicates()
    {
        // Deterministic regression guard for the tail-cursor fix: hand-craft the
        // collision (every record stamped with the same tick) instead of relying on
        // timing, so this can never go vacuous or flaky regardless of hardware/CI.
        const int writeCount = 1000;
        using var store = new InMemoryLogStore(bufferCapacity: writeCount);
        for (var i = 0; i < writeCount; i++)
        {
            store.Write(new LogRecord($"msg-{i}", "cat", LoomLogLevel.Information, Base, eventId: i));
        }

        var written = store.ReadRecent(writeCount);
        Assert.Equal(writeCount, written.Length);
        Assert.All(written, r => Assert.Equal(Base, r.TimestampUtcTicks));

        var replayedIds = new HashSet<int>();
        long cursor = 0;
        while (true)
        {
            var page = store.ReadAfter(cursor);
            Assert.Equal(0, page.DroppedCount);
            if (page.Records.Length == 0)
                break;
            foreach (var record in page.Records)
            {
                Assert.True(replayedIds.Add(record.EventId), $"duplicate EventId {record.EventId} in replay");
            }
            cursor = page.NextSequence;
        }

        Assert.Equal(writeCount, replayedIds.Count);
        for (var i = 0; i < writeCount; i++)
        {
            Assert.Contains(i, replayedIds);
        }
    }

    [Fact]
    public void LoggerDrivenCollisions_ConcurrentWritersProduceCollisions()
    {
        // Not the regression guard (that's the deterministic test above) - this
        // confirms the fix is exercised against the real logging pipeline too.
        // Concurrent writers on separate cores routinely observe the same
        // wall-clock tick; a single-threaded loop through the full pipeline does
        // not reliably (measured: zero collisions at 1M single-threaded iterations
        // on this hardware, since a write costs more than the clock's update
        // quantum). On a single-vCPU CI runner, Parallel.For can degenerate toward
        // sequential, so this is a smoke test, not a strict assertion of collision
        // count - it should still pass either way since it no longer asserts
        // distinctTicks < writeCount.
        const int writeCount = 20_000;
        using var store = new InMemoryLogStore(bufferCapacity: writeCount);
        var provider = new LoomLoggerProvider(store);
        var logger = provider.CreateLogger("MyApp.TightLoop");

        Parallel.For(0, writeCount, i =>
        {
            logger.Log(LogLevel.Information, new EventId(i), i, null, static (s, _) => $"msg-{s}");
        });

        var written = store.ReadRecent(writeCount);
        Assert.Equal(writeCount, written.Length);

        var replayedIds = new HashSet<int>();
        long cursor = 0;
        while (true)
        {
            var page = store.ReadAfter(cursor);
            Assert.Equal(0, page.DroppedCount);
            if (page.Records.Length == 0)
                break;
            foreach (var record in page.Records)
            {
                Assert.True(replayedIds.Add(record.EventId), $"duplicate EventId {record.EventId} in replay");
            }
            cursor = page.NextSequence;
        }

        Assert.Equal(writeCount, replayedIds.Count);
        for (var i = 0; i < writeCount; i++)
        {
            Assert.Contains(i, replayedIds);
        }
    }

    [Fact]
    public void ReadAfter_InterleavedWritesAndReads_PagesIncrementallyWithNoGapsOrDuplicates()
    {
        using var store = new InMemoryLogStore(bufferCapacity: 4096);
        var allWritten = new List<string>();
        var allReplayed = new List<string>();
        long cursor = 0;
        var nextId = 0;

        for (var round = 0; round < 5; round++)
        {
            // Write a batch, then read only what's new since the previous cursor -
            // this is the incremental-paging path a real tailing client exercises,
            // as opposed to one page capturing everything in a single ReadAfter(0).
            var batch = new List<string>();
            for (var i = 0; i < 10; i++)
            {
                var message = $"round{round}-{nextId++}";
                store.Write(Record(message, "cat", Base + nextId));
                batch.Add(message);
            }
            allWritten.AddRange(batch);

            var page = store.ReadAfter(cursor);
            Assert.Equal(0, page.DroppedCount);
            Assert.Equal(batch, page.Records.Select(r => r.Message));
            allReplayed.AddRange(page.Records.Select(r => r.Message));
            cursor = page.NextSequence;
        }

        Assert.Equal(allWritten, allReplayed);
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

    private static LogRecord Record(string message, string category, LoomLogLevel level, long ticks) =>
        new(message, category, level, ticks);

    [Fact]
    public void Query_FilterByCategory_ReturnsOnlyMatching()
    {
        using var store = new InMemoryLogStore();
        store.Write(Record("a", "cat1", Base + 1));
        store.Write(Record("b", "cat2", Base + 2));
        store.Write(Record("c", "cat1", Base + 3));

        var result = store.Query(new LogQueryFilter(null, null, "cat1", null, 100));

        Assert.Equal(new[] { "c", "a" }, result.Select(r => r.Message));
    }

    [Fact]
    public void Query_FilterByMinLevel_ReturnsOnlyAtOrAboveLevel()
    {
        using var store = new InMemoryLogStore();
        store.Write(Record("trace", "cat", LoomLogLevel.Trace, Base + 1));
        store.Write(Record("warn", "cat", LoomLogLevel.Warning, Base + 2));
        store.Write(Record("error", "cat", LoomLogLevel.Error, Base + 3));

        var result = store.Query(new LogQueryFilter(null, null, null, LoomLogLevel.Warning, 100));

        Assert.Equal(new[] { "error", "warn" }, result.Select(r => r.Message));
    }

    [Fact]
    public void Query_FilterBySince_ReturnsOnlyAtOrAfterTimestamp()
    {
        using var store = new InMemoryLogStore();
        store.Write(Record("a", "cat", LoomLogLevel.Information, Base + 1));
        store.Write(Record("b", "cat", LoomLogLevel.Information, Base + 2));
        store.Write(Record("c", "cat", LoomLogLevel.Information, Base + 3));

        var result = store.Query(new LogQueryFilter(Base + 2, null, null, null, 100));

        Assert.Equal(new[] { "c", "b" }, result.Select(r => r.Message));
    }

    [Fact]
    public void Query_FilterByUntil_ReturnsOnlyAtOrBeforeTimestamp()
    {
        using var store = new InMemoryLogStore();
        store.Write(Record("a", "cat", LoomLogLevel.Information, Base + 1));
        store.Write(Record("b", "cat", LoomLogLevel.Information, Base + 2));
        store.Write(Record("c", "cat", LoomLogLevel.Information, Base + 3));

        var result = store.Query(new LogQueryFilter(null, Base + 2, null, null, 100));

        Assert.Equal(new[] { "b", "a" }, result.Select(r => r.Message));
    }

    [Fact]
    public void Query_CombinedFilters_ReturnsIntersection()
    {
        using var store = new InMemoryLogStore();
        store.Write(Record("a", "cat1", LoomLogLevel.Trace, Base + 1));
        store.Write(Record("b", "cat1", LoomLogLevel.Error, Base + 2));
        store.Write(Record("c", "cat2", LoomLogLevel.Error, Base + 3));
        store.Write(Record("d", "cat1", LoomLogLevel.Error, Base + 10));

        var result = store.Query(new LogQueryFilter(Base + 2, Base + 5, "cat1", LoomLogLevel.Warning, 100));

        Assert.Equal(new[] { "b" }, result.Select(r => r.Message));
    }

    [Fact]
    public void Query_LimitIsRespected_AndResultsAreNewestFirst()
    {
        using var store = new InMemoryLogStore();
        for (var i = 0; i < 10; i++)
            store.Write(Record($"msg{i}", "cat", LoomLogLevel.Information, Base + i));

        var result = store.Query(new LogQueryFilter(null, null, null, null, 3));

        Assert.Equal(new[] { "msg9", "msg8", "msg7" }, result.Select(r => r.Message));
    }

    [Fact]
    public void Query_EmptyStore_ReturnsEmptyArray()
    {
        using var store = new InMemoryLogStore();

        var result = store.Query(new LogQueryFilter(null, null, null, null, 100));

        Assert.Empty(result);
    }

    [Fact]
    public void Query_ZeroLimit_ReturnsEmptyArray()
    {
        using var store = new InMemoryLogStore();
        store.Write(Record("a", "cat", LoomLogLevel.Information, Base + 1));

        var result = store.Query(new LogQueryFilter(null, null, null, null, 0));

        Assert.Empty(result);
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
        public LogReadResult ReadAfter(long afterSequence) => new(Array.Empty<LogRecord>(), 0, 0);
        public LogRecord[] Query(LogQueryFilter filter) => Array.Empty<LogRecord>();
        public long CurrentSequence => 0;
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
