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
var connectionDelta = 1;

void EmitOnce()
{
    LoomMetrics.RecordCounter("fixture.orders.processed", 1, new MetricTag("region", "us"));
    LoomMetrics.RecordGauge("fixture.queue.depth", 10, new MetricTag("queue", "alpha"));
    LoomMetrics.RecordGauge("fixture.queue.depth", 20, new MetricTag("queue", "beta"));
    LoomMetrics.RecordHistogram("fixture.request.duration", 42.0, new MetricTag("route", "checkout"));
    activeConnections.Add(connectionDelta, new KeyValuePair<string, object?>("pool", "primary"));
    connectionDelta = -connectionDelta; // oscillates +1/-1 so the counter visibly moves both ways
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
