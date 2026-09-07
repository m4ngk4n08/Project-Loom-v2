using System;

namespace Loom.Telemetry;

/// <summary>
/// Parses the "tags" field System.Diagnostics.Metrics attaches to
/// CounterRateValuePublished / GaugeValuePublished / HistogramValuePublished payloads.
/// Deliberately takes a plain string rather than a TraceEvent - see EventPipeLogPayload
/// for why. Measured format (verified against a live session): "key1=value1,key2=value2",
/// comma-separated pairs, each split on the FIRST '=' so a value containing '=' survives.
/// </summary>
public static class EventPipeTagPayload
{
    public static MetricTag[]? Parse(string? tagPayload)
    {
        if (string.IsNullOrWhiteSpace(tagPayload))
            return null;

        var span = tagPayload.AsSpan();
        var count = 1;
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == ',')
                count++;
        }

        var tags = new MetricTag[count];
        var written = 0;
        var start = 0;
        for (var i = 0; i <= span.Length; i++)
        {
            if (i == span.Length || span[i] == ',')
            {
                var segment = span[start..i];
                var eq = segment.IndexOf('=');
                if (eq > 0)
                {
                    tags[written++] = new MetricTag(segment[..eq].ToString(), segment[(eq + 1)..].ToString());
                }
                start = i + 1;
            }
        }

        if (written == 0)
            return null;
        if (written != tags.Length)
            Array.Resize(ref tags, written);

        return tags;
    }
}
