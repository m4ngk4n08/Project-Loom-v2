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

[Trait("Category", "Integration")]
public sealed class EventPipeHarnessTests : IClassFixture<FixtureProcess>
{
    private readonly FixtureProcess _fixture;

    public EventPipeHarnessTests(FixtureProcess fixture)
    {
        _fixture = fixture;
    }

    private static async Task PollUntilAsync(
        Func<bool> condition,
        EventPipeCollector collector,
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
            $"collector.IsFaulted={collector.IsFaulted}, " +
            $"collector.LastError={collector.LastError?.Message ?? "(none)"}, " +
            $"collector.RecordsIngested={collector.RecordsIngested}, " +
            $"metric names present: [{names}]");
    }

    [Fact]
    public async Task Metrics_Arrive()
    {
        using var store = new InMemoryMetricStore();
        using var collector = new EventPipeCollector(_fixture.Pid, store);
        collector.Start(CancellationToken.None);

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
                collector, store);
        }
        finally
        {
            collector.Stop();
        }
    }

    [Fact]
    public async Task Gauge_TagCombinations_SurviveIngest()
    {
        using var store = new InMemoryMetricStore();
        using var collector = new EventPipeCollector(_fixture.Pid, store);
        collector.Start(CancellationToken.None);

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
                collector, store);
        }
        finally
        {
            collector.Stop();
        }
    }

    [Fact]
    public async Task Counter_And_Histogram_Tags_SurviveIngest()
    {
        using var store = new InMemoryMetricStore();
        using var collector = new EventPipeCollector(_fixture.Pid, store);
        collector.Start(CancellationToken.None);

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
                collector, store);
        }
        finally
        {
            collector.Stop();
        }
    }

    [Fact]
    public async Task Logs_Arrive()
    {
        using var metricStore = new InMemoryMetricStore();
        using var logStore = new InMemoryLogStore();
        using var collector = new EventPipeCollector(_fixture.Pid, metricStore, logStore);
        collector.Start(CancellationToken.None);

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
                $"Information log never arrived. collector.IsFaulted={collector.IsFaulted}, LastError={collector.LastError?.Message}, RecordsIngested={collector.RecordsIngested}");
            Assert.True(error.HasValue,
                $"Error log never arrived. collector.IsFaulted={collector.IsFaulted}, LastError={collector.LastError?.Message}, RecordsIngested={collector.RecordsIngested}");

            Assert.Equal("System.InvalidOperationException", error!.Value.ExceptionType);
            Assert.Equal("fixture boom", error.Value.ExceptionMessage);
            Assert.Equal("fixture processed order {OrderId}", info!.Value.Template);
            Assert.NotNull(info.Value.ArgumentsJson);

            // ActivityTraceId/ActivitySpanId arrive as empty strings, not null, when the
            // fixture never starts an Activity. W3CTraceId.TryParseTraceId/TryParseSpanId
            // reject anything whose length isn't 32/16 without throwing, so both records
            // must land with all-zero trace fields.
            Assert.Equal(0UL, info.Value.TraceIdHi);
            Assert.Equal(0UL, info.Value.TraceIdLo);
            Assert.Equal(0UL, info.Value.SpanId);
            Assert.Equal(0UL, error.Value.TraceIdHi);
            Assert.Equal(0UL, error.Value.TraceIdLo);
            Assert.Equal(0UL, error.Value.SpanId);
        }
        finally
        {
            collector.Stop();
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
        using var collector = new EventPipeCollector(deadPid, store);
        collector.Start(CancellationToken.None);

        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline && !collector.IsFaulted)
                await Task.Delay(250);

            Assert.True(collector.IsFaulted, "Attaching to a dead PID must set IsFaulted, not fail silently.");
            Assert.NotNull(collector.LastError);
            Assert.Equal(0, collector.RecordsIngested);
        }
        finally
        {
            collector.Stop();
        }
    }
}
