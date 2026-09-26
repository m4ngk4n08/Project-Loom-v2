using System;
using Xunit;

namespace Loom.Telemetry.Tests;

/// <summary>
/// BACKLOG.md 6.35: "zero-allocation" had never been measured, and a closure captured by
/// GetOrCreateBuffer's slow path cost 24 B on every call. These fail on a single byte for
/// the untagged paths. The tagged paths keep the caller's params array (the ring buffer
/// stores it), so they are bounded, not exact: anything over 40 B means the
/// KeyValuePair[] copy is back.
/// </summary>
public sealed class RecordingAllocationTests : IDisposable
{
    private const int Warmup = 5_000;
    private const int Iterations = 10_000;
    private const int Rounds = 3;

    public RecordingAllocationTests() => LoomSampling.ClearRules();

    public void Dispose() => LoomSampling.ClearRules();

    // Minimum of several rounds. A real per-call allocation shows in every round; a one-off
    // allocation the runtime makes on this thread shows in at most one (measured 6.5-7.5 KB in
    // a single 10,000-call window on CI, 2026-09-25/26 - cause not identified). Every round is
    // kept for the failure message, so the next occurrence says which kind it was.
    private static (double Min, string Rounds) BytesPerCall(Action call)
    {
        for (var i = 0; i < Warmup; i++) call();

        var rounds = new double[Rounds];
        for (var r = 0; r < Rounds; r++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++) call();
            var after = GC.GetAllocatedBytesForCurrentThread();
            rounds[r] = (after - before) / (double)Iterations;
        }

        return (Math.Min(rounds[0], Math.Min(rounds[1], rounds[2])), string.Join(", ", rounds));
    }

    [Fact]
    public void RecordCounter_NoTags_AllocatesNothing()
    {
        var (bytes, rounds) = BytesPerCall(() => LoomMetrics.RecordCounter("alloc.test.counter.untagged", 1));
        Assert.True(bytes == 0, $"RecordCounter allocated {bytes} B per call (rounds: {rounds}); expected 0.");
    }

    [Fact]
    public void RecordHistogram_NoTags_AllocatesNothing()
    {
        var (bytes, rounds) = BytesPerCall(() => LoomMetrics.RecordHistogram("alloc.test.histogram.untagged", 1.5));
        Assert.True(bytes == 0, $"RecordHistogram allocated {bytes} B per call (rounds: {rounds}); expected 0.");
    }

    [Fact]
    public void LoomProfileMethod_NormalReturn_AllocatesNothing()
    {
        var instance = new SampleInstrumentedClass();
        var (bytes, rounds) = BytesPerCall(() => instance.MethodWithReturnValue(1, 2));
        Assert.True(bytes == 0, $"[LoomProfile] method allocated {bytes} B per call (rounds: {rounds}); expected 0.");
    }

    [Fact]
    public void RecordCounter_OneTag_AllocatesOnlyTheParamsArray()
    {
        var tag = new MetricTag("region", "us");
        var (bytes, rounds) = BytesPerCall(() => LoomMetrics.RecordCounter("alloc.test.counter.tagged", 1, tag));
        Assert.True(bytes <= 40, $"1-tag RecordCounter allocated {bytes} B per call (rounds: {rounds}); expected <= 40 (the params array only).");
    }

    [Fact]
    public void RecordHistogram_OneTag_AllocatesOnlyTheParamsArray()
    {
        var tag = new MetricTag("route", "checkout");
        var (bytes, rounds) = BytesPerCall(() => LoomMetrics.RecordHistogram("alloc.test.histogram.tagged", 1.5, tag));
        Assert.True(bytes <= 40, $"1-tag RecordHistogram allocated {bytes} B per call (rounds: {rounds}); expected <= 40 (the params array only).");
    }
}
