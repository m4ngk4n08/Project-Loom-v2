// Loom.AotProbe exists to prove that referencing Loom.Telemetry (and the source
// generator it pulls in) does not break a consumer's Native AOT publish. Its binary
// size is not a product metric - it exists to fail the build when an AOT constraint
// is broken.
//
// It also enforces, natively, that the untagged recording paths allocate nothing
// (BACKLOG.md 6.35). The tests in Loom.Telemetry.Tests measure the same thing under the
// JIT; this is the check that runs against the Native AOT binary in the linux-x64 CI job.
using Loom.Telemetry;

const int Warmup = 5_000;
const int Iterations = 10_000;

var probe = new Probe();
probe.DoWork();

var failed = false;

for (var i = 0; i < Warmup; i++) LoomMetrics.RecordCounter("probe.counter", 1);
var before = GC.GetAllocatedBytesForCurrentThread();
for (var i = 0; i < Iterations; i++) LoomMetrics.RecordCounter("probe.counter", 1);
failed |= Report("RecordCounter (untagged)", GC.GetAllocatedBytesForCurrentThread() - before);

for (var i = 0; i < Warmup; i++) LoomMetrics.RecordHistogram("probe.histogram", 1.5);
before = GC.GetAllocatedBytesForCurrentThread();
for (var i = 0; i < Iterations; i++) LoomMetrics.RecordHistogram("probe.histogram", 1.5);
failed |= Report("RecordHistogram (untagged)", GC.GetAllocatedBytesForCurrentThread() - before);

for (var i = 0; i < Warmup; i++) probe.Trivial();
before = GC.GetAllocatedBytesForCurrentThread();
for (var i = 0; i < Iterations; i++) probe.Trivial();
failed |= Report("[LoomProfile] method (normal return)", GC.GetAllocatedBytesForCurrentThread() - before);

if (failed) return 1;

Console.WriteLine("AOT probe OK");
return 0;

static bool Report(string path, long totalBytes)
{
    if (totalBytes == 0) return false;
    Console.WriteLine($"AOT probe FAILED: {path} allocated {totalBytes / (double)Iterations} bytes/call ({totalBytes} B over {Iterations} calls), expected 0");
    return true;
}

public sealed class Probe
{
    [LoomProfile(Name = "Probe.DoWork")]
    public void DoWork()
    {
        // Deliberately trivial. This exists to make the generator emit a wrapper,
        // not to measure anything.
        Thread.Sleep(1);
    }

    // No sleep: this is the one the allocation check loops, so it must cost only what
    // the generated wrapper and RecordMethodExecution cost.
    [LoomProfile(Name = "Probe.Trivial")]
    public void Trivial()
    {
    }
}
