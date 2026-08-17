using System;
using System.Collections.Generic;

namespace Loom.Telemetry.Sampling;

/// <summary>
/// Builder for configuring sampling rules.
/// </summary>
public sealed class SamplingConfiguration
{
    private readonly List<ISamplingRule> _rules = new();

    /// <summary>
    /// Sample operations based on duration threshold.
    /// Operations faster than threshold are sampled at the given rate.
    /// Operations slower than threshold are always recorded.
    /// </summary>
    public SamplingConfiguration SampleByDuration(TimeSpan threshold, double rate)
    {
        _rules.Add(new DurationThresholdRule(threshold, rate));
        return this;
    }

    /// <summary>
    /// Sample operations based on metric name pattern.
    /// Metrics matching the pattern are sampled at the given rate.
    /// </summary>
    public SamplingConfiguration SampleByName(string pattern, double rate)
    {
        _rules.Add(new NamePatternRule(pattern, rate));
        return this;
    }

    /// <summary>
    /// Always record metrics matching a predicate (escape hatch).
    /// Use for errors, slow operations, critical paths, etc.
    /// </summary>
    public SamplingConfiguration AlwaysRecordWhen(Func<string, TimeSpan?, Exception?, bool> predicate)
    {
        _rules.Add(new AlwaysRecordRule(predicate));
        return this;
    }

    /// <summary>
    /// Add a custom sampling rule.
    /// </summary>
    public SamplingConfiguration AddRule(ISamplingRule rule)
    {
        if (rule == null)
            throw new ArgumentNullException(nameof(rule));

        _rules.Add(rule);
        return this;
    }

    /// <summary>
    /// Sample all metrics at a uniform rate (useful for testing or dev environments).
    /// </summary>
    public SamplingConfiguration SampleAll(double rate)
    {
        // Use duration rule with zero threshold to apply to everything
        _rules.Add(new DurationThresholdRule(TimeSpan.Zero, rate));
        return this;
    }

    /// <summary>
    /// Build the configured rules into an immutable list.
    /// </summary>
    internal IReadOnlyList<ISamplingRule> Build()
    {
        // Sort by priority (high to low) so important rules evaluated first
        _rules.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        return _rules.AsReadOnly();
    }
}
