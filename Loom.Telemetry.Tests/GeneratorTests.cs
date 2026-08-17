using System;
using Xunit;

namespace Loom.Telemetry.Tests;

public sealed class GeneratorTests
{
    [Fact]
    public void SimpleMethod_ExecutesWithoutError()
    {
        var instance = new SampleInstrumentedClass();

        // Call the profiled wrapper - generator creates extension method
        instance.SimpleMethod_Profiled();
    }

    [Fact]
    public void MethodWithReturnValue_ReturnsCorrectResult()
    {
        var instance = new SampleInstrumentedClass();

        // Call the profiled wrapper
        var result = instance.MethodWithReturnValue_Profiled(5, 7);

        Assert.Equal(12, result);
    }

    [Fact]
    public void ThrowingMethod_PropagatesException()
    {
        var instance = new SampleInstrumentedClass();

        // Call the profiled wrapper
        var exception = Assert.Throws<InvalidOperationException>(() =>
            instance.ThrowingMethod_Profiled());

        Assert.Equal("Test exception", exception.Message);
    }

    [Fact]
    public void OriginalMethod_StillWorksWithoutProfiling()
    {
        var instance = new SampleInstrumentedClass();

        // Original method still works - no profiling overhead unless you call *_Profiled()
        var result = instance.MethodWithReturnValue(10, 20);

        Assert.Equal(30, result);
    }
}