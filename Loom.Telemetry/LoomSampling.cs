using System;
using System.Collections.Generic;
using Loom.Telemetry.Sampling;

namespace Loom.Telemetry;

/// <summary>
/// Global sampling configuration for Loom metrics.
/// Controls which metrics are recorded based on configurable rules.
/// </summary>
public static class LoomSampling
{
    private static IReadOnlyList<ISamplingRule> _rules = Array.Empty<ISamplingRule>();
    private static readonly object Lock = new object();

    /// <summary>
    /// Configure sampling rules using a fluent builder API.
    /// </summary>
    /// <param name="configure">Configuration action</param>
    public static void Configure(Action<SamplingConfiguration> configure)
    {
        if (configure == null)
            throw new ArgumentNullException(nameof(configure));

        var config = new SamplingConfiguration();
        configure(config);

        lock (Lock)
        {
            _rules = config.Build();
        }
    }

    /// <summary>
    /// Check if a metric should be recorded based on current sampling rules.
    /// </summary>
    /// <param name="metricName">Name of the metric</param>
    /// <param name="duration">Duration (for timing metrics)</param>
    /// <param name="exception">Exception (if any)</param>
    /// <returns>True if metric should be recorded, false to skip</returns>
    public static bool ShouldRecord(string metricName, TimeSpan? duration = null, Exception? exception = null)
    {
        IReadOnlyList<ISamplingRule> rules;

        lock (Lock)
        {
            rules = _rules;
        }

        // No rules configured - record everything
        if (rules.Count == 0)
            return true;

        // Two-phase evaluation:
        // Phase 1: Check AlwaysRecord rules (high priority = 100)
        //          If ANY matches, record immediately (short-circuit)
        foreach (var rule in rules)
        {
            if (rule is Sampling.AlwaysRecordRule alwaysRule)
            {
                if (alwaysRule.ShouldRecord(metricName, duration, exception))
                    return true; // AlwaysRecord escape hatch triggered
            }
        }

        // Phase 2: Check sampling rules (lower priority)
        //          If ANY returns false, skip recording
        foreach (var rule in rules)
        {
            if (rule is not Sampling.AlwaysRecordRule)
            {
                if (!rule.ShouldRecord(metricName, duration, exception))
                    return false;
            }
        }

        // No rules blocked it - record
        return true;
    }

    /// <summary>
    /// Clear all sampling rules (record everything).
    /// </summary>
    public static void ClearRules()
    {
        lock (Lock)
        {
            _rules = Array.Empty<ISamplingRule>();
        }
    }

    /// <summary>
    /// Get current sampling rules (for diagnostics).
    /// </summary>
    public static IReadOnlyList<ISamplingRule> GetRules()
    {
        lock (Lock)
        {
            return _rules;
        }
    }
}
