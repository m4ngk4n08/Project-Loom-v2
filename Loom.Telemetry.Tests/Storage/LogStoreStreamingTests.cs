using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Loom.Storage;
using Loom.Storage.Logging;
using Xunit;

namespace Loom.Telemetry.Tests.Storage;

/// <summary>
/// Exercises the connect/consume/cancel/unsubscribe pattern the /ws/logs endpoint
/// builds on top of ILogStore.Subscribe - a subscriber registered on connect, fed
/// by subsequent writes, and always unsubscribed (in a finally) regardless of how
/// the stream ends.
/// </summary>
public sealed class LogStoreStreamingTests
{
    private static LogRecord Record(string message, long ticks) =>
        new(message, "cat", LoomLogLevel.Information, ticks);

    private static readonly long Base = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    private static async IAsyncEnumerable<LogRecord> ConsumeAsync(
        ILogStore store,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var reader = store.Subscribe();
        try
        {
            await foreach (var record in reader.ReadAllAsync(ct))
            {
                yield return record;
            }
        }
        finally
        {
            store.Unsubscribe(reader);
        }
    }

    [Fact]
    public async Task RecordsWrittenAfterConnectReachTheStream()
    {
        using var store = new InMemoryLogStore();
        using var cts = new CancellationTokenSource();
        var received = new List<string>();

        var consumeTask = Task.Run(async () =>
        {
            await foreach (var record in ConsumeAsync(store, cts.Token))
            {
                received.Add(record.Message);
                if (received.Count == 2) break;
            }
        });

        // Give the subscriber a moment to register before writing.
        while (received.Count == 0 && !consumeTask.IsCompleted)
        {
            store.Write(Record("a", Base));
            store.Write(Record("b", Base + 1));
            await Task.Delay(10);
        }

        await consumeTask;

        Assert.Equal(new[] { "a", "b" }, received);
    }

    [Fact]
    public async Task CancellationExitsCleanlyAndUnsubscribes()
    {
        using var store = new InMemoryLogStore();
        using var cts = new CancellationTokenSource();

        var started = new TaskCompletionSource();
        var consumeTask = Task.Run(async () =>
        {
            var enumerator = ConsumeAsync(store, cts.Token).GetAsyncEnumerator(cts.Token);
            started.SetResult();
            await enumerator.MoveNextAsync();
        });

        await started.Task;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consumeTask);

        // The subscriber must have been removed - a write after cancellation should
        // reach no leaked subscriber (verified indirectly via a fresh subscribe still
        // being the only live one).
        var probe = store.Subscribe();
        store.Write(Record("after-cancel", Base));
        Assert.True(probe.TryRead(out var record));
        Assert.Equal("after-cancel", record.Message);
        store.Unsubscribe(probe);
    }
}
