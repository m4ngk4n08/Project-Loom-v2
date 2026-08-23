using System;
using Loom.Telemetry.Interfaces;

namespace Loom.Telemetry.Sampling;

/// <summary>
/// Sampling rule based on metric name pattern matching.
/// Allows different sampling rates for different operations.
/// </summary>
public sealed class NamePatternRule : ISamplingRule
{
    private readonly string _pattern;
    private readonly double _sampleRate;
    private readonly Random _random;

    public int Priority => 50; // Medium priority

    /// <summary>
    /// Create a name-based sampling rule.
    /// </summary>
    /// <param name="pattern">Pattern to match in metric name (case-insensitive substring)</param>
    /// <param name="sampleRate">Sample rate for matching metrics (0.0 to 1.0)</param>
    public NamePatternRule(string pattern, double sampleRate)
    {
        if (string.IsNullOrEmpty(pattern))
            throw new ArgumentException("Pattern cannot be null or empty", nameof(pattern));

        if (sampleRate < 0 || sampleRate > 1)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be between 0 and 1");

        _pattern = pattern;
        _sampleRate = sampleRate;
        _random = new Random();
    }

    public bool ShouldRecord(string metricName, TimeSpan? duration, Exception? exception)
    {
        // If name doesn't match pattern, don't apply this rule
        if (!metricName.Contains(_pattern, StringComparison.OrdinalIgnoreCase))
            return true;

        // Name matches - apply sampling rate
        lock (_random)
        {
            return _random.NextDouble() < _sampleRate;
        }
    }
}
