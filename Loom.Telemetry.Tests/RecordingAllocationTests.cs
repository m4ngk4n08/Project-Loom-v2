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

    public RecordingAllocationTests() => LoomSampling.ClearRules();

    public void Dispose() => LoomSampling.ClearRules();

    private static double BytesPerCall(Action call)
    {
        for (var i = 0; i < Warmup; i++) call();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++) call();
        var after = GC.GetAllocatedBytesForCurrentThread();

        return (after - before) / (double)Iterations;
    }

    [Fact]
    public void RecordCounter_NoTags_AllocatesNothing()
    {
        var bytes = BytesPerCall(() => LoomMetrics.RecordCounter("alloc.test.counter.untagged", 1));
        Assert.Equal(0.0, bytes);
    }

    [Fact]
    public void RecordHistogram_NoTags_AllocatesNothing()
    {
        var bytes = BytesPerCall(() => LoomMetrics.RecordHistogram("alloc.test.histogram.untagged", 1.5));
        Assert.Equal(0.0, bytes);
    }

    [Fact]
    public void LoomProfileMethod_NormalReturn_AllocatesNothing()
    {
        var instance = new SampleInstrumentedClass();
        var bytes = BytesPerCall(() => instance.MethodWithReturnValue(1, 2));
        Assert.Equal(0.0, bytes);
    }

    [Fact]
    public void RecordCounter_OneTag_AllocatesOnlyTheParamsArray()
    {
        var tag = new MetricTag("region", "us");
        var bytes = BytesPerCall(() => LoomMetrics.RecordCounter("alloc.test.counter.tagged", 1, tag));
        Assert.True(bytes <= 40, $"1-tag RecordCounter allocated {bytes} B per call; expected <= 40 (the params array only).");
    }

    [Fact]
    public void RecordHistogram_OneTag_AllocatesOnlyTheParamsArray()
    {
        var tag = new MetricTag("route", "checkout");
        var bytes = BytesPerCall(() => LoomMetrics.RecordHistogram("alloc.test.histogram.tagged", 1.5, tag));
        Assert.True(bytes <= 40, $"1-tag RecordHistogram allocated {bytes} B per call; expected <= 40 (the params array only).");
    }
}
