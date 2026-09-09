using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Loom.Dashboard;
using Loom.Storage;
using Loom.Telemetry;
using Xunit;

namespace Loom.Telemetry.Tests.Integration;

// These assertions are DELIBERATELY duplicated from EventPipeHarnessTests rather than
// shared through a common helper. The point of this file is to catch the collector and
// the bridge disagreeing with each other; a shared assertion body that runs once against
// whichever implementation is passed in cannot detect that disagreement - it would just
// pass or fail identically for both. Do not "de-duplicate" this against
// EventPipeHarnessTests.
[Trait("Category", "Integration")]
public sealed class EventPipeBridgeTests : IClassFixture<FixtureProcess>
{
    private readonly FixtureProcess _fixture;

    public EventPipeBridgeTests(FixtureProcess fixture)
    {
        _fixture = fixture;
    }

    private static async Task PollUntilAsync(
        Func<bool> condition,
        CapturingLogger<EventPipeBridge> logger,
        IMetricStore store,
        TimeSpan? cap = null)
    {
        var deadline = DateTime.UtcNow + (cap ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(250);
        }

        var names = string.Join(", ", store.GetMetricNames());
        Assert.Fail(
            $"Condition not met within {(cap ?? TimeSpan.FromSeconds(30)).TotalSeconds}s. " +
            $"metric names present: [{names}]. Last log messages:{Environment.NewLine}{logger.DumpLast()}");
    }

    [Fact]
    public async Task Metrics_Arrive()
    {
        using var store = new InMemoryMetricStore();
        using var logStore = new InMemoryLogStore();
        var logger = new CapturingLogger<EventPipeBridge>();
        var bridge = new EventPipeBridge(_fixture.Pid, store, logStore, logger);

        await bridge.StartAsync(CancellationToken.None);
        try
        {
            await PollUntilAsync(
                () =>
                {
                    var names = store.GetMetricNames();
                    return names.Contains("fixture.orders.processed")
                        && names.Contains("fixture.queue.depth")
                        && names.Contains("fixture.request.duration");
                },
                logger, store);
        }
        finally
        {
            await bridge.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Gauge_TagCombinations_SurviveIngest()
    {
        using var store = new InMemoryMetricStore();
        using var logStore = new InMemoryLogStore();
        var logger = new CapturingLogger<EventPipeBridge>();
        var bridge = new EventPipeBridge(_fixture.Pid, store, logStore, logger);

        await bridge.StartAsync(CancellationToken.None);
        try
        {
            await PollUntilAsync(
                () =>
                {
                    var records = store.ReadRecent("fixture.queue.depth", 100);
                    var hasAlpha = records.Any(r => r.Value == 10 && r.Tags != null && r.Tags.Any(t => t.Key == "queue" && t.Value == "alpha"));
                    var hasBeta = records.Any(r => r.Value == 20 && r.Tags != null && r.Tags.Any(t => t.Key == "queue" && t.Value == "beta"));
                    return hasAlpha && hasBeta;
                },
                logger, store);
        }
        finally
        {
            await bridge.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Counter_And_Histogram_Tags_SurviveIngest()
    {
        using var store = new InMemoryMetricStore();
        using var logStore = new InMemoryLogStore();
        var logger = new CapturingLogger<EventPipeBridge>();
        var bridge = new EventPipeBridge(_fixture.Pid, store, logStore, logger);

        await bridge.StartAsync(CancellationToken.None);
        try
        {
            await PollUntilAsync(
                () =>
                {
                    var counterRecords = store.ReadRecent("fixture.orders.processed", 100);
                    var histogramRecords = store.ReadRecent("fixture.request.duration", 100);
                    var counterHasTag = counterRecords.Any(r => r.Tags != null && r.Tags.Any(t => t.Key == "region" && t.Value == "us"));
                    var histogramHasTag = histogramRecords.Any(r => r.Tags != null && r.Tags.Any(t => t.Key == "route" && t.Value == "checkout"));
                    return counterHasTag && histogramHasTag;
                },
                logger, store);
        }
        finally
        {
            await bridge.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task CounterTotal_ReflectsIntervalDeltas_NotCumulativeSum()
    {
        using var store = new InMemoryMetricStore();
        using var logStore = new InMemoryLogStore();
        var logger = new CapturingLogger<EventPipeBridge>();
        var bridge = new EventPipeBridge(_fixture.Pid, store, logStore, logger);

        // FIXED window, not poll-until-true: during the ramp the wrong field ("value",
        // the cumulative total) also produces a passing value, so a loop that breaks as
        // soon as the assertion holds would pass under both the right and the wrong
        // implementation. Collect for a fixed window and assert on the settled state.
        var sw = Stopwatch.StartNew();
        await bridge.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(8));
        await bridge.StopAsync(CancellationToken.None);
        sw.Stop();

        var elapsedSeconds = sw.Elapsed.TotalSeconds;

        // Loom.TestFixtureApp increments fixture.orders.processed 5 times/second, so a
        // correct ingest should land somewhere under elapsedSeconds * 5 (session attach
        // takes a moment, so the bridge always misses the first publish or two). The bug
        // this test guards against ingests the target's CUMULATIVE total once per ~1s
        // publish and sums those totals into the accumulator, which grows triangularly:
        // after N publishes the reported figure is roughly trueTotal * (N+1)/2. 2x the
        // true rate sits well clear of both a correct ingest and the triangular-sum bug.
        var upperBound = elapsedSeconds * 5 * 2;

        var ordersTotal = store.GetCounterTotals().FirstOrDefault(t => t.MetricName == "fixture.orders.processed");

        Assert.True(ordersTotal.MetricName != null,
            $"No counter total recorded for fixture.orders.processed. Last log messages:{Environment.NewLine}{logger.DumpLast()}");
        Assert.True(ordersTotal.Total > 0, $"Counter total was not positive: {ordersTotal.Total}");
        Assert.True(ordersTotal.Total <= upperBound,
            $"Counter total {ordersTotal.Total} exceeds bound {upperBound:F1} (elapsed={elapsedSeconds:F1}s, true rate ~5/s so expected ~{elapsedSeconds * 5:F1}). " +
            "This is the triangular-sum symptom of ingesting cumulative 'value' instead of per-interval 'rate'.");
    }

    [Fact]
    public async Task Logs_Arrive()
    {
        using var store = new InMemoryMetricStore();
        using var logStore = new InMemoryLogStore();
        var logger = new CapturingLogger<EventPipeBridge>();
        var bridge = new EventPipeBridge(_fixture.Pid, store, logStore, logger);

        await bridge.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            Loom.Telemetry.LogRecord? info = null;
            Loom.Telemetry.LogRecord? error = null;

            while (DateTime.UtcNow < deadline && (info is null || error is null))
            {
                var records = logStore.ReadRecent("Fixture.Worker", 200);
                info ??= records.Cast<Loom.Telemetry.LogRecord?>()
                    .FirstOrDefault(r => r!.Value.Level == LoomLogLevel.Information && r.Value.Message.Contains("4711"));
                error ??= records.Cast<Loom.Telemetry.LogRecord?>()
                    .FirstOrDefault(r => r!.Value.Level == LoomLogLevel.Error && r.Value.ExceptionType == "System.InvalidOperationException");

                if (info is null || error is null)
                    await Task.Delay(250);
            }

            Assert.True(info.HasValue,
                $"Information log never arrived. Last log messages:{Environment.NewLine}{logger.DumpLast()}");
            Assert.True(error.HasValue,
                $"Error log never arrived. Last log messages:{Environment.NewLine}{logger.DumpLast()}");

            Assert.Equal("System.InvalidOperationException", error!.Value.ExceptionType);
            Assert.Equal("fixture boom", error.Value.ExceptionMessage);
            Assert.Equal("fixture processed order {OrderId}", info!.Value.Template);
            Assert.NotNull(info.Value.ArgumentsJson);
        }
        finally
        {
            await bridge.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DeadPid_ReportsFailure_NotSilence()
    {
        var psi = new ProcessStartInfo("dotnet", "--version")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var trivial = Process.Start(psi)!;
        await trivial.WaitForExitAsync();
        var deadPid = trivial.Id;

        using var store = new InMemoryMetricStore();
        using var logStore = new InMemoryLogStore();
        var logger = new CapturingLogger<EventPipeBridge>();
        // The bridge retries forever by design (see EventPipeBridge.ExecuteAsync's outer
        // while loop) - do not assert that it stops. Assert only that the failure is
        // VISIBLE in the captured log and that nothing silently landed in the store.
        var bridge = new EventPipeBridge(deadPid, store, logStore, logger);

        await bridge.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline && !logger.Any(m => m.Contains("disconnected") || m.Contains("Reconnecting")))
                await Task.Delay(250);

            Assert.True(
                logger.Any(m => m.Contains("disconnected") || m.Contains("Reconnecting")),
                $"Attaching to a dead PID produced no visible failure in the log. Last log messages:{Environment.NewLine}{logger.DumpLast()}");
            Assert.Empty(store.GetMetricNames());
            Assert.Empty(logStore.ReadRecent(50));
        }
        finally
        {
            await bridge.StopAsync(CancellationToken.None);
        }
    }
}
