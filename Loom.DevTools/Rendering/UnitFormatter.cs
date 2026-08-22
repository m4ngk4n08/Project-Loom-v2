namespace Loom.DevTools.Rendering;

/// <summary>
/// Infers a unit from a metric name and formats values with it. Ported from
/// MetricsCommand.InferUnit, which had unreachable duplicate branches (a second
/// "%"-matching block after the first, and an "alloc-rate" -> "B/s" branch placed
/// after a broader "alloc-rate"/"gc-time"/"lock-contention" -> "/s" branch that always
/// matched alloc-rate first). This is a single ordered chain, most-specific first, so
/// "alloc-rate" resolves to "B/s".
/// </summary>
public static class UnitFormatter
{
    public static string InferUnit(string name)
    {
        if (Contains(name, "alloc-rate"))
            return "B/s";

        if (Contains(name, "gc-time") || Contains(name, "lock-contention"))
            return "/s";

        if (Contains(name, "cpu-usage") || Contains(name, "time-in-gc") || Contains(name, "gc-fragmentation") ||
            Contains(name, "usage") || Contains(name, "percent") || name.Contains('%'))
            return "%";

        if (Contains(name, "working-set") || Contains(name, "gc-heap-size") || Contains(name, "gc-committed"))
            return "MB";

        if (Contains(name, "loh-size") || Contains(name, "poh-size") ||
            Contains(name, "gen-0-size") || Contains(name, "gen-1-size") || Contains(name, "gen-2-size") ||
            Contains(name, "il-bytes-jitted") || Contains(name, "bytes") || Contains(name, "alloc") ||
            Contains(name, "memory") || Contains(name, "heap"))
            return "B";

        if (Contains(name, "duration") || Contains(name, "latency") || Contains(name, "elapsed") || Contains(name, "time"))
            return "ms";

        if (Contains(name, "amount") || Contains(name, "total") || Contains(name, "price") || Contains(name, "revenue"))
            return "$";

        return "count";
    }

    public static string Format(string name, double value)
    {
        var unit = InferUnit(name);
        return unit switch
        {
            "%" => $"{value:F1}%",
            "$" => $"${value:N2}",
            "ms" => $"{value:F1}ms",
            "MB" => $"{value:F1}MB",
            "B" => FormatBytes(value),
            "B/s" => $"{FormatBytes(value)}/s",
            "/s" => $"{value:F2}/s",
            _ => $"{value:F0}",
        };
    }

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

    private static bool Contains(string name, string token) =>
        name.Contains(token, StringComparison.OrdinalIgnoreCase);
}
