using System;

namespace Loom.Telemetry.Tests;

public partial class SampleInstrumentedClass
{
    [LoomProfile]
    public void SimpleMethod()
    {
        // Simulate some work
        var sum = 0;
        for (var i = 0; i < 100; i++)
        {
            sum += i;
        }
    }

    [LoomProfile(Name = "CustomMetricName")]
    public int MethodWithReturnValue(int x, int y)
    {
        return x + y;
    }

    [LoomProfile]
    public void ThrowingMethod()
    {
        throw new InvalidOperationException("Test exception");
    }


    // TODO: [LoomTrack] implementation will come later
    // For now, regular property
    public int Counter { get; set; }
}