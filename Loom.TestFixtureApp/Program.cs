using System.Diagnostics.Metrics;
using Loom.Telemetry;
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Trace);
    builder.AddEventSourceLogger();
});
var logger = loggerFactory.CreateLogger("Fixture.Worker");

using var cts = new CancellationTokenSource();

// LoomMetrics has no up-down counter API (out of scope to add one - see
// PROMPT-counter-rate-ingest.md Phase 2), so this talks to the same
// "Loom.Telemetry" meter directly. EventPipeCollector/EventPipeBridge's
// provider filter is keyed on meter name, not on the API that created the
// instrument, so this is picked up identically to a LoomMetrics.Record* call.
using var meter = new Meter("Loom.Telemetry");
var activeConnections = meter.CreateUpDownCounter<long>("fixture.active.connections");

// Ramps to a starting level, then keeps climbing slowly and monotonically forever. This
// replaced a plain ramp-and-hold that went silent forever once it reached the plateau
// (see PROMPT-fixture-heartbeat.md) - a session attaching after that point saw NOTHING
// for this instrument, measured as zero ingested records, because an up-down counter
// with no activity during a session's window is not reported by that session at all.
//
// A "dip one, recover it immediately" heartbeat was tried first and measured NOT to
// work: EventPipe's UpDownCounterRateValuePublished "value" field is cumulative WITHIN
// THE SESSION, meaning it sums only the Add() calls a given session has observed since
// IT attached, not the instrument's absolute level. A dip and its immediate recovery net
// to zero, so a session attaching at any point after the ramp measured a "value" of 0 -
// invisible in a different, more misleading way than the original bug, since a
// downstream reader can't tell a genuine zero level from an instrument nobody's watched
// long enough. A one-way, monotonic climb is the only shape where a session's own
// since-attach cumulative total is guaranteed positive and growing regardless of when it
// attaches.
//
// Two requirements pull against each other:
//   - there must ALWAYS be activity, so a session attaching at any time reports the
//     instrument at all, and accumulates a positive "value" within its own window;
//   - the step size must stay small enough that the interval "rate" (the delta since the
//     last ~1s publish) never approaches the ">= 3" threshold the ingest tests assert on
//     - otherwise reading "rate" instead of "value" would ALSO pass, and the tests would
//     stop discriminating between the two fields. See EventPipeUpDownCounterTests /
//     EventPipeBridgeUpDownCounterTests for those assertions, and
//     PROMPT-fixture-heartbeat.md Phase 3 for the sabotage checks that prove reading the
//     wrong field still fails both.
// One +1 every 8 ticks (1.6s at this loop's 200ms rate) guarantees at least 4 increments
// land inside ANY 8-second collection window (floor(8s / 1.6s)), so a late-attaching
// session's own "value" is safely >= 3 well before such a window closes, while the 1.6s
// spacing - wider than the ~1s aggregation interval - means at most one increment can
// ever land inside a single published interval, so "rate" never exceeds 1. The level
// itself never goes below its InitialLevel of 5 (it only ever climbs), clear of the ">=
// 3" floor the existing tests need.
const int InitialLevel = 5;
const int ClimbEveryNTicks = 8;
var activeConnectionLevel = 0;
var ticksSinceClimb = 0;

void EmitOnce()
{
    LoomMetrics.RecordCounter("fixture.orders.processed", 1, new MetricTag("region", "us"));
    LoomMetrics.RecordGauge("fixture.queue.depth", 10, new MetricTag("queue", "alpha"));
    LoomMetrics.RecordGauge("fixture.queue.depth", 20, new MetricTag("queue", "beta"));
    LoomMetrics.RecordHistogram("fixture.request.duration", 42.0, new MetricTag("route", "checkout"));

    var connectionTag = new KeyValuePair<string, object?>("pool", "primary");
    if (activeConnectionLevel < InitialLevel)
    {
        activeConnections.Add(1, connectionTag);
        activeConnectionLevel++;
    }
    else if (++ticksSinceClimb >= ClimbEveryNTicks)
    {
        activeConnections.Add(1, connectionTag);
        activeConnectionLevel++;
        ticksSinceClimb = 0;
    }

    logger.LogInformation("fixture processed order {OrderId}", 4711);
    logger.LogError(new InvalidOperationException("fixture boom"), "fixture failed order {OrderId}", 4712);
}

// First iteration runs before the readiness handshake so a test that blocks on READY
// never races the diagnostics server for the very first event.
EmitOnce();

Console.Out.Write("READY\n");
Console.Out.Flush();

var loopTask = Task.Run(async () =>
{
    while (!cts.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(200, cts.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }

        EmitOnce();
    }
}, cts.Token);

// Blocks until the test closes stdin (ReadLine returns null). Killing the process is
// the test's backstop, not the normal shutdown path.
while (Console.In.ReadLine() is not null)
{
}

cts.Cancel();
try
{
    await loopTask;
}
catch (OperationCanceledException)
{
}

return 0;
