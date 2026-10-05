using System;

namespace Loom.Telemetry;

/// <summary>
/// The unit a metric's source declared. Never inferred from the metric's name: a metric
/// whose source declared nothing is <see cref="None"/>, and the tools show no unit.
/// The members are exactly the units Loom's two sources publish - [LoomProfile] timings
/// (<see cref="Milliseconds"/>) and the System.Runtime EventCounters' DisplayUnits,
/// measured against a live .NET 10 session: "%", "B", "MB", "ms" or empty.
/// </summary>
public enum MetricUnit
{
    None,
    Percent,
    Bytes,
    Megabytes,
    Milliseconds,

    /// <summary>A count per second: a unitless incrementing counter's per-interval delta.</summary>
    PerSecond,
    BytesPerSecond,
    MillisecondsPerSecond,
}

/// <summary>
/// The only place a <see cref="MetricUnit"/> meets its text form. The symbols are a wire
/// contract - EventPipe carries units as strings, and the dashboard API sends them to the
/// UI - so they live here once and every other caller uses the enum.
/// </summary>
public static class MetricUnits
{
    private static readonly MetricUnit[] All = Enum.GetValues<MetricUnit>();

    /// <summary>The display symbol; empty for <see cref="MetricUnit.None"/>.</summary>
    public static string Symbol(this MetricUnit unit) => unit switch
    {
        MetricUnit.Percent => "%",
        MetricUnit.Bytes => "B",
        MetricUnit.Megabytes => "MB",
        MetricUnit.Milliseconds => "ms",
        MetricUnit.PerSecond => "/s",
        MetricUnit.BytesPerSecond => "B/s",
        MetricUnit.MillisecondsPerSecond => "ms/s",
        _ => string.Empty,
    };

    /// <summary>
    /// The unit whose <see cref="Symbol"/> is exactly <paramref name="symbol"/>;
    /// <see cref="MetricUnit.None"/> for null, empty or an unrecognised symbol.
    /// </summary>
    public static MetricUnit Parse(string? symbol)
    {
        if (string.IsNullOrEmpty(symbol))
            return MetricUnit.None;

        foreach (var unit in All)
        {
            if (unit != MetricUnit.None && string.Equals(unit.Symbol(), symbol, StringComparison.Ordinal))
                return unit;
        }
        return MetricUnit.None;
    }

    /// <summary>
    /// The per-second form of a unit, for a value that is a delta over a one-second
    /// interval. <see cref="MetricUnit.None"/> for a unit with no rate form here.
    /// </summary>
    public static MetricUnit ToRate(this MetricUnit unit) => unit switch
    {
        MetricUnit.None => MetricUnit.PerSecond,
        MetricUnit.Bytes => MetricUnit.BytesPerSecond,
        MetricUnit.Milliseconds => MetricUnit.MillisecondsPerSecond,
        _ => MetricUnit.None,
    };
}
