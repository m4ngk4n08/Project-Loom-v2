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

// Ramps to a plateau and then HOLDS, rather than oscillating. Steady state is the only
// case that distinguishes the two candidate value fields: once the level stops moving,
// the event's "rate" is 0 forever while its "value" stays at the level. An oscillating
// counter never holds still, so it cannot tell them apart - and a connection pool
// sitting at a steady size is the ordinary case in a real workload, not an edge case.
const int ConnectionPlateau = 5;
var activeConnectionLevel = 0;

void EmitOnce()
{
    LoomMetrics.RecordCounter("fixture.orders.processed", 1, new MetricTag("region", "us"));
    LoomMetrics.RecordGauge("fixture.queue.depth", 10, new MetricTag("queue", "alpha"));
    LoomMetrics.RecordGauge("fixture.queue.depth", 20, new MetricTag("queue", "beta"));
    LoomMetrics.RecordHistogram("fixture.request.duration", 42.0, new MetricTag("route", "checkout"));
    if (activeConnectionLevel < ConnectionPlateau)
    {
        activeConnections.Add(1, new KeyValuePair<string, object?>("pool", "primary"));
        activeConnectionLevel++;
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
