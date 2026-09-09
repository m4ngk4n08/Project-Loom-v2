using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Loom.DevTools.Services;
using Loom.Storage;
using Loom.Telemetry;
using Xunit;

namespace Loom.Telemetry.Tests.Integration;

/// <summary>
/// The integration test BACKLOG.md 6.11 calls out as missing: before this, there was no
/// test proving a real System.Runtime Increment-shaped counter reaches the store at all,
/// let alone typed correctly. Uses "threadpool-completed-items-count" rather than
/// "gen-0-gc-count": Phase 0's measurement showed it fires every interval regardless of
/// GC activity (Loom.TestFixtureApp's 200ms loop reliably completes ~5 thread-pool work
/// items/second), where a GC-count counter could sit at 0 for the whole window and prove
/// nothing about ingest.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SystemRuntimeCounterIngestTests : IClassFixture<FixtureProcess>
{
    private readonly FixtureProcess _fixture;

    public SystemRuntimeCounterIngestTests(FixtureProcess fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RuntimeIncrementCounter_ArrivesAsCounter_AndDoesNotInflate()
    {
        const string metricName = "threadpool-completed-items-count";

        using var store = new InMemoryMetricStore();
        using var collector = new EventPipeCollector(_fixture.Pid, store);

        // FIXED window, not poll-until-true: same reasoning as
        // EventPipeHarnessTests.CounterTotal_ReflectsIntervalDeltas_NotCumulativeSum -
        // during a short ramp a cumulative read can look plausible too, so this asserts
        // on the settled state over a known elapsed time instead of breaking early.
        var sw = Stopwatch.StartNew();
        collector.Start(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(8));
        collector.Stop();
        sw.Stop();

        var elapsedSeconds = sw.Elapsed.TotalSeconds;

        var records = store.ReadRecent(metricName, 50);
        Assert.True(records.Length > 0,
            $"No {metricName} records ingested at all. collector.IsFaulted={collector.IsFaulted}, " +
            $"LastError={collector.LastError?.Message ?? "(none)"}, RecordsIngested={collector.RecordsIngested}, " +
            $"metric names present: [{string.Join(", ", store.GetMetricNames())}]");
        Assert.Equal(MetricType.Counter, records[0].Type);

        // Phase 0 measured this counter's "Increment" holding steady at ~4-5 per 1s
        // interval - a per-interval delta, not a cumulative total (a cumulative total
        // would climb 5, 10, 15, 20... across successive publishes). Bound at 3x the
        // measured ~5/s rate: generous enough to absorb real thread-pool scheduling
        // noise (this is live OS/runtime behaviour, not a fixture-controlled rate like
        // fixture.orders.processed), while a triangular-sum bug - which grows roughly
        // rate * N * (N+1)/2 for N one-second publishes - would clear it by several
        // times over an 8s window.
        var upperBound = elapsedSeconds * 5 * 3;

        var total = store.GetCounterTotals().FirstOrDefault(t => t.MetricName == metricName);
        Assert.True(total.MetricName != null, $"No counter total recorded for {metricName}.");
        Assert.True(total.Total > 0, $"Counter total was not positive: {total.Total}");
        Assert.True(total.Total <= upperBound,
            $"Counter total {total.Total} exceeds bound {upperBound:F1} (elapsed={elapsedSeconds:F1}s). " +
            "This is the triangular-sum symptom of ingesting a cumulative value instead of a per-interval delta - " +
            "the exact bug BACKLOG.md 6.11's Bug A + Bug B pairing would reproduce if fixed separately.");
    }
}
