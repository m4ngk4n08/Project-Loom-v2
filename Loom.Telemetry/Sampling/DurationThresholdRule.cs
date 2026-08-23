using System;
using Loom.Telemetry.Interfaces;

namespace Loom.Telemetry.Sampling;

/// <summary>
/// Sampling rule based on operation duration.
/// Fast operations are sampled at a lower rate, slow operations always recorded.
/// </summary>
public sealed class DurationThresholdRule : ISamplingRule
{
    private readonly TimeSpan _threshold;
    private readonly double _sampleRate;
    private readonly Random _random;

    public int Priority => 50; // Medium priority

    /// <summary>
    /// Create a duration-based sampling rule.
    /// </summary>
    /// <param name="threshold">Duration threshold (operations faster than this are sampled)</param>
    /// <param name="sampleRate">Sample rate for fast operations (0.0 to 1.0)</param>
    public DurationThresholdRule(TimeSpan threshold, double sampleRate)
    {
        if (sampleRate < 0 || sampleRate > 1)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be between 0 and 1");

        _threshold = threshold;
        _sampleRate = sampleRate;
        _random = new Random();
    }

    public bool ShouldRecord(string metricName, TimeSpan? duration, Exception? exception)
    {
        // If no duration, can't apply this rule
        if (!duration.HasValue)
            return true;

        // Special case: threshold == 0 means "sample everything uniformly"
        // (used by SampleAll helper)
        if (_threshold == TimeSpan.Zero)
        {
            lock (_random)
            {
                return _random.NextDouble() < _sampleRate;
            }
        }

        // Slow operations (above threshold) always recorded
        if (duration.Value > _threshold)
            return true;

        // Fast operations (at or below threshold) sampled at configured rate
        lock (_random) // Random is not thread-safe
        {
            return _random.NextDouble() < _sampleRate;
        }
    }
}
