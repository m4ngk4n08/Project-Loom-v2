using System;
using System.Linq;
using Xunit;

namespace Loom.Telemetry.Tests;

/// <summary>
/// [LoomProfile] must instrument the ORIGINAL method call - the way every real caller
/// already writes it, with no _Profiled rename - via a compile-time interceptor. This is
/// the test that would have caught the original bug: the generator used to only emit a
/// separate *_Profiled() extension method that nothing ever redirected existing calls to,
/// so a normally-called [LoomProfile] method recorded nothing. These tests call the
/// SampleInstrumentedClass methods (see SampleInstrumentedClass.cs) by their real names.
/// </summary>
public sealed class LoomProfileInterceptorTests : IDisposable
{
    public LoomProfileInterceptorTests()
    {
        LoomSampling.ClearRules();
    }

    public void Dispose()
    {
        LoomSampling.ClearRules();
    }

    [Fact]
    public void OriginalCall_RecordsTelemetry_ViaInterceptor()
    {
        // Arrange
        var instance = new SampleInstrumentedClass();

        // Act - call the method by its ORIGINAL name, exactly as any real caller would.
        // No _Profiled, no generator-specific naming convention.
        var result = instance.MethodWithReturnValue(5, 7);

        // Assert - the interceptor must have recorded telemetry under the explicit
        // metric name, and the original call semantics (return value) still hold.
        Assert.Equal(12, result);

        var recent = LoomMetrics.GetRecentMetrics(100);
        var found = recent.FirstOrDefault(m => m.Name == "CustomMetricName");

        Assert.NotNull(found.Name);
        Assert.Equal(MetricType.Histogram, found.Type);
    }

    [Fact]
    public void OriginalCall_RecordsError_ViaInterceptor_WhenMethodThrows()
    {
        // Arrange
        var instance = new SampleInstrumentedClass();

        // Act - original call name, exception must still propagate to the caller.
        var exception = Assert.Throws<InvalidOperationException>(() =>
            instance.ThrowingMethod());

        Assert.Equal("Test exception", exception.Message);

        // Assert - the interceptor recorded the failure metric.
        var recent = LoomMetrics.GetRecentMetrics(200);
        var found = recent.FirstOrDefault(m => m.Name == "SampleInstrumentedClass.ThrowingMethod.errors");

        Assert.NotNull(found.Name);
        Assert.Equal(MetricType.Counter, found.Type);
    }
}
