using System;
using System.Threading;
using System.Threading.Tasks;
using Loom.DevTools.Services;
using Loom.Storage;
using Xunit;

namespace Loom.Telemetry.Tests.Integration;

/// <summary>
/// Guards against the fixture's up-down counter going invisible to a session that
/// attaches late - the ordinary case for a person running the dashboard by hand against
/// the fixture (target started first, dashboard second), not an edge case. Before the
/// heartbeat fix (PROMPT-fixture-heartbeat.md), the fixture ramped
/// "fixture.active.connections" to a plateau and then stopped touching it forever; an
/// up-down counter with no activity during a session's window is not reported by that
/// session at all (measured 2026-09-07), so a late attach saw zero records for it -
/// reproduced directly: 15s late, 0 records, while 32 other metric names arrived fine in
/// the same window, proving the session itself was healthy.
///
/// Needs its own FixtureProcess: EventPipeUpDownCounterTests and
/// EventPipeBridgeUpDownCounterTests both attach immediately, during the ramp - sharing
/// either fixture would never exercise a late attach at all.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EventPipeUpDownCounterLateAttachTests : IClassFixture<FixtureProcess>
{
    private readonly FixtureProcess _fixture;

    public EventPipeUpDownCounterLateAttachTests(FixtureProcess fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LateAttach_StillSeesActiveConnections()
    {
        // "Late" only needs to clear the ~1s ramp plus the heartbeat's ~2s hold - 12s
        // gives comfortable margin without inflating the suite unnecessarily.
        await Task.Delay(TimeSpan.FromSeconds(12));

        using var store = new InMemoryMetricStore();
        using var collector = new EventPipeCollector(_fixture.Pid, store);
        collector.Start(CancellationToken.None);

        try
        {
            // FIXED window, not poll-until-true - same reasoning as
            // EventPipeUpDownCounterTests: during any transient the wrong field can look
            // momentarily plausible, so this asserts on the settled state.
            await Task.Delay(TimeSpan.FromSeconds(8));

            var records = store.ReadRecent("fixture.active.connections", 50);

            Assert.True(records.Length > 0,
                $"No fixture.active.connections records ingested for a late-attaching session. " +
                $"IsFaulted={collector.IsFaulted}, LastError={collector.LastError?.Message ?? "(none)"}, " +
                $"RecordsIngested={collector.RecordsIngested}, " +
                $"metric names present: [{string.Join(", ", store.GetMetricNames())}]");

            var newest = records[0];
            Assert.True(newest.Value >= 3,
                $"Newest fixture.active.connections value was {newest.Value} for a late-attaching " +
                "session; expected the held level (4 or 5). A value near 0 here is the signature of " +
                "reading the interval 'rate' instead of the cumulative 'value'.");
        }
        finally
        {
            collector.Stop();
        }
    }
}
