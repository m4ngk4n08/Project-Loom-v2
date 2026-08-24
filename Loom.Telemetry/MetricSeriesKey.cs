using System;
using System.Text;

namespace Loom.Telemetry;

/// <summary>
/// Canonical (metric name, tag set) -> series key, shared by every producer and
/// consumer that needs to agree on series identity (the store's counter
/// accumulator, the Prometheus formatter). There must be exactly one
/// implementation of this logic: if the store and the formatter each built their
/// own key, a divergence would make lookups miss silently and fall back to the
/// non-monotonic path with no error raised anywhere.
/// </summary>
public static class MetricSeriesKey
{
    /// <summary>
    /// Sorts tags by key (ordinal) so {a,b} and {b,a} produce the same key.
    /// Returns an empty array for null or empty input - never null.
    /// </summary>
    public static MetricTag[] SortTags(MetricTag[]? tags)
    {
        if (tags is null || tags.Length == 0)
            return [];

        var sorted = (MetricTag[])tags.Clone();
        Array.Sort(sorted, static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        return sorted;
    }

    /// <summary>
    /// Builds the canonical series key from a metric name and already-sorted tags
    /// (see <see cref="SortTags"/>). Tags are length-prefixed
    /// (<c>len:key</c><c>len:value</c>) rather than joined with plain '='/',' -
    /// without the prefixes, a tag VALUE containing those separator characters can
    /// forge another tag set's key, e.g. {a="b,c=d"} and {a="b", c="d"} would both
    /// serialize to "a=b,c=d" and silently collide. Do not simplify this back to
    /// plain separator joining.
    /// </summary>
    public static string Build(string metricName, MetricTag[] sortedTags)
    {
        if (sortedTags.Length == 0) return metricName;

        var sb = new StringBuilder(metricName);
        sb.Append('\0');
        for (var i = 0; i < sortedTags.Length; i++)
        {
            if (i > 0) sb.Append(',');
            var tag = sortedTags[i];
            sb.Append(tag.Key.Length).Append(':').Append(tag.Key)
              .Append(tag.Value.Length).Append(':').Append(tag.Value);
        }
        return sb.ToString();
    }
}
