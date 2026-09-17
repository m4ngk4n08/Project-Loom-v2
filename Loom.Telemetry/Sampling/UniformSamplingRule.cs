using System;
using Loom.Telemetry.Interfaces;

namespace Loom.Telemetry.Sampling;

/// <summary>
/// Sampling rule that samples every metric at a uniform rate, regardless of duration
/// (including duration-less metrics such as property changes). Backs
/// SamplingConfiguration.SampleAll - kept separate from DurationThresholdRule so
/// SampleByDuration's own zero-threshold behavior ("slower than zero is always recorded")
/// isn't overloaded to also mean "sample everything".
/// </summary>
internal sealed class UniformSamplingRule : ISamplingRule
{
    private readonly double _sampleRate;
    private readonly Random _random;

    public int Priority => 50; // Medium priority - same as the other sampling rules.

    /// <summary>
    /// Create a uniform sampling rule.
    /// </summary>
    /// <param name="sampleRate">Sample rate for every metric (0.0 to 1.0)</param>
    public UniformSamplingRule(double sampleRate)
    {
        if (sampleRate < 0 || sampleRate > 1)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be between 0 and 1");

        _sampleRate = sampleRate;
        _random = new Random();
    }

    public bool ShouldRecord(string metricName, TimeSpan? duration, Exception? exception)
    {
        lock (_random) // Random is not thread-safe
        {
            return _random.NextDouble() < _sampleRate;
        }
    }
}
