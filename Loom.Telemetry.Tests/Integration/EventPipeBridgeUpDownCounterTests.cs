using System;
using System.Threading;
using System.Threading.Tasks;
using Loom.Dashboard;
using Loom.Storage;
using Loom.Telemetry;
using Xunit;

namespace Loom.Telemetry.Tests.Integration;

/// <summary>
/// Deliberately its own class, so it gets its own FixtureProcess and attaches while the
/// up-down counter is still ramping - mirrors EventPipeUpDownCounterTests for the same
/// reason. Two runtime behaviours make a late attach useless here, both measured
/// directly rather than assumed:
///   - an up-down counter's "value" is cumulative WITHIN THE SESSION, not since the
///     instrument was created;
///   - an up-down counter with no activity during a session is not reported by that
///     session at all - attaching 15s after the fixture reached its plateau ingested
///     zero records for it.
/// Sharing EventPipeBridgeTests' fixture would attach after the ramp finished and test
/// nothing.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EventPipeBridgeUpDownCounterTests : IClassFixture<FixtureProcess>
{
    private readonly FixtureProcess _fixture;

    public EventPipeBridgeUpDownCounterTests(FixtureProcess fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UpDownCounter_ReadsLevel_NotIntervalDelta()
    {
        using var store = new InMemoryMetricStore();
        using var logStore = new InMemoryLogStore();
        var logger = new CapturingLogger<EventPipeBridge>();
        var bridge = new EventPipeBridge(_fixture.Pid, store, logStore, logger);

        await bridge.StartAsync(CancellationToken.None);
        try
        {
            // Collect for a FIXED window rather than polling until the assertion happens
            // to hold. The fixture reaches its plateau within the first second, and
            // during that ramp "rate" is also 4-5 - so a poll-until-true loop breaks
            // mid-ramp and passes under both fields. Only after the level settles do the
            // two diverge, with rate falling to 0 while value holds.
            await Task.Delay(TimeSpan.FromSeconds(8));

            var records = store.ReadRecent("fixture.active.connections", 50);

            Assert.True(records.Length > 0,
                $"No fixture.active.connections records ingested at all. Last log messages:{Environment.NewLine}{logger.DumpLast()}");

            var newest = records[0];
            Assert.True(newest.Value >= 3,
                $"Newest fixture.active.connections value was {newest.Value}; expected the held level (~5). " +
                "A value of 0 here is the signature of ingesting the interval 'rate' instead of 'value': " +
                "the rate drops to 0 as soon as the level stops moving.");

            Assert.NotNull(newest.Tags);
            Assert.Contains(newest.Tags!, t => t.Key == "pool" && t.Value == "primary");
            Assert.Equal(MetricType.Gauge, newest.Type);
        }
        finally
        {
            await bridge.StopAsync(CancellationToken.None);
        }
    }
}
