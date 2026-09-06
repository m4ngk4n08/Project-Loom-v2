using System;
using Xunit;

namespace Loom.Telemetry.Tests;

/// <summary>
/// LOOM_METRIC_BUFFER_CAPACITY is read once into a static field at type-init time (see
/// LoomMetrics.ConfiguredCapacity), matching AssistOptions' read-once-at-startup pattern.
/// That means these tests can only observe the capacity actually in effect for this test
/// process, not exercise different values within one run — setting the env var mid-test
/// would have no effect because the static field was already initialized. The unset
/// (default) case is what every other test in this project already assumes (e.g.
/// MetricsApiTests.MetricBuffer_CircularBehavior uses GetBufferCapacity() directly), so
/// that is what's covered here; the parse/clamp logic itself is exercised independently
/// via MetricBuffer's own constructor in MetricBufferTests.
/// </summary>
public sealed class LoomMetricsBufferCapacityTests
{
    [Fact]
    public void GetBufferCapacity_DefaultsTo8192_WhenEnvVarIsUnset()
    {
        // This test asserts the default that applies when LOOM_METRIC_BUFFER_CAPACITY is
        // unset in the environment running this test suite (the normal case in CI/local
        // dev). It is not a live env-var round trip -- see the class-level remark.
        Assert.Null(Environment.GetEnvironmentVariable("LOOM_METRIC_BUFFER_CAPACITY"));

        var name = $"test.capacity.default.{Guid.NewGuid():N}";
        LoomMetrics.RecordCounter(name, 1);

        Assert.Equal(8192, LoomMetrics.GetBufferCapacity());
    }

    [Fact]
    public void GetDroppedCount_IsZero_ForANameWithNoBuffer()
    {
        var name = $"test.dropped.nobuffer.{Guid.NewGuid():N}";
        Assert.Equal(0, LoomMetrics.GetDroppedCount(name));
    }

    [Fact]
    public void GetDroppedCount_ReflectsBufferWraparound()
    {
        var name = $"test.dropped.wrapped.{Guid.NewGuid():N}";
        var capacity = LoomMetrics.GetBufferCapacity();
        const int overwrite = 7;

        for (var i = 0; i < capacity + overwrite; i++)
        {
            LoomMetrics.RecordCounter(name, i);
        }

        Assert.Equal(overwrite, LoomMetrics.GetDroppedCount(name));
    }
}
