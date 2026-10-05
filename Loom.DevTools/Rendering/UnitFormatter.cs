using Loom.Telemetry;

namespace Loom.DevTools.Rendering;

/// <summary>
/// Formats a value in the unit its source declared (see <see cref="MetricUnit"/>). It
/// used to infer the unit from the metric's name, which labelled a [LoomProfile] timing
/// "count" and anything named "*total*" as dollars. A metric with no declared unit is now
/// shown as a plain number, not given a guessed one.
/// </summary>
public static class UnitFormatter
{
    public static string Format(MetricUnit unit, double value) => unit switch
    {
        MetricUnit.Percent or MetricUnit.Milliseconds or MetricUnit.Megabytes or MetricUnit.MillisecondsPerSecond
            => $"{value:F1}{unit.Symbol()}",
        MetricUnit.Bytes => FormatBytes(value),
        // Scaled to KB/MB first, so the symbol is the scaled one plus the rate suffix.
        MetricUnit.BytesPerSecond => $"{FormatBytes(value)}{MetricUnit.PerSecond.Symbol()}",
        MetricUnit.PerSecond => $"{value:F2}{unit.Symbol()}",
        _ => value.ToString("0.##"),
    };

    private static string FormatBytes(double bytes)
    {
        Span<string> suffixes = ["B", "KB", "MB", "GB"];
        var sign = bytes < 0 ? -1 : 1;
        var abs = Math.Abs(bytes);
        var idx = 0;
        while (abs >= 1024 && idx < suffixes.Length - 1)
        {
            abs /= 1024;
            idx++;
        }
        return $"{sign * abs:F1}{suffixes[idx]}";
    }
}
