using System.Buffers;
using System.Buffers.Text;
using System.Text;
using Loom.Storage;

namespace Loom.Telemetry.Exporters.Prometheus;

/// <summary>
/// Hand-written formatter for the Prometheus text exposition format (version 0.0.4).
/// Zero reflection. Writes UTF-8 directly into an IBufferWriter&lt;byte&gt; - no
/// StringBuilder for the wire output, no per-metric string allocation, no LINQ.
/// This is NOT OpenMetrics: OpenMetrics requires a trailing "# EOF" marker and a
/// different content type. The /prometheus endpoint serves
/// "text/plain; version=0.0.4", so no "# EOF" is emitted here - adding one would
/// break clients expecting 0.0.4.
/// </summary>
public static class PrometheusFormatter
{
    /// <summary>
    /// Format all metrics from the store as Prometheus text exposition format,
    /// writing UTF-8 directly into <paramref name="writer"/> (e.g. an HTTP response's
    /// PipeWriter).
    /// </summary>
    public static void Format(IMetricStore store, IBufferWriter<byte> writer)
    {
        var buffers = store.GetBuffers();

        foreach (var (metricName, buffer) in buffers)
        {
            var capacity = buffer.Capacity;
            // MetricRecord holds reference fields (Tags, ExceptionType), so it can't be
            // stackalloc'd - rent one scratch array sized to the buffer's capacity and
            // read every currently-retained record in one call.
            var rented = ArrayPool<MetricRecord>.Shared.Rent(capacity);
            try
            {
                var recordCount = buffer.TryReadRecent(rented.AsSpan(0, capacity));
                if (recordCount == 0) continue;

                // Newest-first, per TryReadRecent's contract.
                var records = rented.AsSpan(0, recordCount);
                var metricType = records[0].Type;
                var prometheusType = MapToPrometheusTypeUtf8(metricType);

                // HELP before TYPE - the conventional Prometheus exposition order. Both
                // are emitted once per metric NAME, not per label-set series.
                WriteUtf8(writer, "# HELP "u8);
                WriteSanitizedName(writer, metricName);
                WriteUtf8(writer, " Loom telemetry metric"u8);
                WriteNewLine(writer);

                WriteUtf8(writer, "# TYPE "u8);
                WriteSanitizedName(writer, metricName);
                WriteUtf8(writer, " "u8);
                WriteUtf8(writer, prometheusType);
                WriteNewLine(writer);

                var groups = GroupByLabelSet(records);
                var orderedKeys = new List<string>(groups.Keys);
                orderedKeys.Sort(StringComparer.Ordinal);

                if (metricType is MetricType.Counter or MetricType.Gauge)
                {
                    foreach (var key in orderedKeys)
                    {
                        var group = groups[key];
                        // Counters are cumulative totals: RecordCounter writes each call
                        // as an increment, so the series total is the sum of every
                        // retained record - not the most recent one. Gauges are
                        // point-in-time, so they keep the newest record's value (the
                        // first one seen while scanning newest-first).
                        var value = metricType == MetricType.Counter ? group.Sum : group.NewestValue;

                        WriteSanitizedName(writer, metricName);
                        WriteLabels(writer, group.SortedTags, quantile: default);
                        WriteUtf8(writer, " "u8);
                        WriteF2(writer, value);
                        WriteNewLine(writer);
                    }
                }
                else
                {
                    // Histogram / MethodExecution: summary statistics per label set.
                    foreach (var key in orderedKeys)
                    {
                        var group = groups[key];
                        var values = group.Values;
                        values.Sort();
                        var count = values.Count;
                        var p50 = values[count / 2];
                        var p95 = values[(int)(count * 0.95)];
                        var p99 = values[(int)(count * 0.99)];

                        WriteSanitizedName(writer, metricName);
                        WriteUtf8(writer, "_count"u8);
                        WriteLabels(writer, group.SortedTags, quantile: default);
                        WriteUtf8(writer, " "u8);
                        WriteLong(writer, count);
                        WriteNewLine(writer);

                        WriteSanitizedName(writer, metricName);
                        WriteUtf8(writer, "_sum"u8);
                        WriteLabels(writer, group.SortedTags, quantile: default);
                        WriteUtf8(writer, " "u8);
                        WriteF2(writer, group.Sum);
                        WriteNewLine(writer);

                        WriteSanitizedName(writer, metricName);
                        WriteLabels(writer, group.SortedTags, "0.5"u8);
                        WriteUtf8(writer, " "u8);
                        WriteF2(writer, p50);
                        WriteNewLine(writer);

                        WriteSanitizedName(writer, metricName);
                        WriteLabels(writer, group.SortedTags, "0.95"u8);
                        WriteUtf8(writer, " "u8);
                        WriteF2(writer, p95);
                        WriteNewLine(writer);

                        WriteSanitizedName(writer, metricName);
                        WriteLabels(writer, group.SortedTags, "0.99"u8);
                        WriteUtf8(writer, " "u8);
                        WriteF2(writer, p99);
                        WriteNewLine(writer);
                    }
                }

                WriteNewLine(writer);
            }
            finally
            {
                // clearArray: true - MetricRecord holds string references (Name isn't
                // ours to worry about here, but Tags/ExceptionType are), so a pooled
                // array left dirty would keep them alive until the pool reuses the slot.
                ArrayPool<MetricRecord>.Shared.Return(rented, clearArray: true);
            }
        }
    }

    /// <summary>
    /// Convenience string-returning overload for callers that need the full text in
    /// memory (tests, ad-hoc tooling). The production scrape endpoint should call the
    /// IBufferWriter&lt;byte&gt; overload directly against the response body instead.
    /// </summary>
    public static string Format(IMetricStore store)
    {
        var bufferWriter = new PooledBufferWriter();
        try
        {
            Format(store, bufferWriter);
            return Encoding.UTF8.GetString(bufferWriter.WrittenSpan);
        }
        finally
        {
            bufferWriter.Dispose();
        }
    }

    /// <summary>
    /// One label-set series within a metric name: every retained record sharing the
    /// same tags (compared by key, ignoring declaration order).
    /// </summary>
    private sealed class LabelGroup
    {
        public required MetricTag[] SortedTags { get; init; }

        /// <summary>Sum of every retained record's value - the counter total, and
        /// reused as the histogram's _sum (both are "sum of every value in the
        /// group").</summary>
        public double Sum;

        /// <summary>First record's value seen while scanning newest-first - the gauge's
        /// current value.</summary>
        public double NewestValue;

        /// <summary>Every retained record's value - used for histogram/summary count
        /// and quantiles.</summary>
        public List<double> Values { get; } = [];
    }

    /// <summary>
    /// Groups records (newest-first) into one LabelGroup per distinct tag set. Tag
    /// sets are compared by (key, value) pairs sorted by key ordinal, so {a,b} and
    /// {b,a} land in the same group.
    /// KNOWN LIMITATION: the ring buffer is bounded (8192 records by default), so once
    /// a counter's buffer wraps, only the retained records are summed and the reported
    /// total stops growing even though the real cumulative count keeps increasing. A
    /// correct fix needs a monotonic accumulator maintained by the store itself rather
    /// than derived from buffer contents - see BACKLOG.md § 4.
    /// </summary>
    private static Dictionary<string, LabelGroup> GroupByLabelSet(ReadOnlySpan<MetricRecord> records)
    {
        var groups = new Dictionary<string, LabelGroup>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            var sortedTags = SortTags(record.Tags);
            var key = BuildCanonicalKey(sortedTags);

            if (!groups.TryGetValue(key, out var group))
            {
                group = new LabelGroup { SortedTags = sortedTags };
                groups[key] = group;
            }

            group.Sum += record.Value;
            group.Values.Add(record.Value);
            if (group.Values.Count == 1)
                group.NewestValue = record.Value;
        }

        return groups;
    }

    private static MetricTag[] SortTags(MetricTag[]? tags)
    {
        if (tags is null || tags.Length == 0)
            return [];

        var sorted = (MetricTag[])tags.Clone();
        Array.Sort(sorted, static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        return sorted;
    }

    private static string BuildCanonicalKey(MetricTag[] sortedTags)
    {
        if (sortedTags.Length == 0) return string.Empty;

        var sb = new StringBuilder();
        for (var i = 0; i < sortedTags.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(sortedTags[i].Key).Append('=').Append(sortedTags[i].Value);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Writes a label brace group: {k1="v1",k2="v2",quantile="0.X"}. Omits the braces
    /// entirely when there are no tags and no quantile - "name value", not "name{} value".
    /// </summary>
    private static void WriteLabels(IBufferWriter<byte> writer, MetricTag[] sortedTags, ReadOnlySpan<byte> quantile)
    {
        if (sortedTags.Length == 0 && quantile.IsEmpty)
            return;

        WriteUtf8(writer, "{"u8);
        for (var i = 0; i < sortedTags.Length; i++)
        {
            if (i > 0) WriteUtf8(writer, ","u8);
            WriteSanitizedName(writer, sortedTags[i].Key);
            WriteUtf8(writer, "=\""u8);
            WriteEscapedLabelValue(writer, sortedTags[i].Value);
            WriteUtf8(writer, "\""u8);
        }

        if (!quantile.IsEmpty)
        {
            if (sortedTags.Length > 0) WriteUtf8(writer, ","u8);
            WriteUtf8(writer, "quantile=\""u8);
            WriteUtf8(writer, quantile);
            WriteUtf8(writer, "\""u8);
        }

        WriteUtf8(writer, "}"u8);
    }

    /// <summary>
    /// Escapes a label value per the exposition format: backslash -&gt; \\, double-quote
    /// -&gt; \", newline -&gt; \n. Cold path (label values are rare and short), so this
    /// builds the escaped string and encodes once rather than writing byte-by-byte.
    /// Order matters: backslashes are escaped first, so backslashes introduced by the
    /// quote/newline escapes are never re-escaped.
    /// </summary>
    private static void WriteEscapedLabelValue(IBufferWriter<byte> writer, string value)
    {
        var escaped = value.IndexOfAny(['\\', '"', '\n']) < 0
            ? value
            : value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

        var maxBytes = Encoding.UTF8.GetMaxByteCount(escaped.Length);
        Span<byte> encoded = maxBytes <= 256 ? stackalloc byte[256] : new byte[maxBytes];
        var byteCount = Encoding.UTF8.GetBytes(escaped, encoded);

        var dest = writer.GetSpan(byteCount);
        encoded[..byteCount].CopyTo(dest);
        writer.Advance(byteCount);
    }

    private static void WriteNewLine(IBufferWriter<byte> writer) => WriteUtf8(writer, "\n"u8);

    private static void WriteUtf8(IBufferWriter<byte> writer, ReadOnlySpan<byte> literal)
    {
        var span = writer.GetSpan(literal.Length);
        literal.CopyTo(span);
        writer.Advance(literal.Length);
    }

    private static void WriteF2(IBufferWriter<byte> writer, double value)
    {
        var span = writer.GetSpan(32);
        Utf8Formatter.TryFormat(value, span, out var written, new StandardFormat('F', 2));
        writer.Advance(written);
    }

    private static void WriteLong(IBufferWriter<byte> writer, long value)
    {
        var span = writer.GetSpan(20);
        Utf8Formatter.TryFormat(value, span, out var written);
        writer.Advance(written);
    }

    /// <summary>
    /// Writes a name (metric name or label key) UTF-8-encoded, with '.' and '-'
    /// replaced by '_' - transforms bytes in place instead of building an
    /// intermediate sanitized string.
    /// </summary>
    private static void WriteSanitizedName(IBufferWriter<byte> writer, string name)
    {
        var maxBytes = Encoding.UTF8.GetMaxByteCount(name.Length);
        Span<byte> encoded = maxBytes <= 256 ? stackalloc byte[256] : new byte[maxBytes];
        var byteCount = Encoding.UTF8.GetBytes(name, encoded);
        var nameBytes = encoded[..byteCount];

        for (var i = 0; i < nameBytes.Length; i++)
        {
            if (nameBytes[i] == (byte)'.' || nameBytes[i] == (byte)'-')
                nameBytes[i] = (byte)'_';
        }

        var dest = writer.GetSpan(nameBytes.Length);
        nameBytes.CopyTo(dest);
        writer.Advance(nameBytes.Length);
    }

    private static ReadOnlySpan<byte> MapToPrometheusTypeUtf8(MetricType type) => type switch
    {
        MetricType.Counter => "counter"u8,
        MetricType.Gauge => "gauge"u8,
        MetricType.Histogram => "summary"u8,
        MetricType.MethodExecution => "summary"u8,
        _ => "untyped"u8
    };

    /// <summary>
    /// Growable IBufferWriter&lt;byte&gt; over ArrayPool-rented memory, used only by the
    /// string-returning Format(store) convenience overload above.
    /// </summary>
    private sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private byte[] _buffer;
        private int _position;

        public PooledBufferWriter(int initialSize = 4096)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(initialSize);
        }

        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _position);

        public void Advance(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (_position + count > _buffer.Length) throw new InvalidOperationException("Attempted to write past the end of the buffer.");
            _position += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(_position);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(_position);
        }

        private void EnsureCapacity(int sizeHint)
        {
            var requested = sizeHint > 0 ? sizeHint : 256;
            var remaining = _buffer.Length - _position;
            if (remaining >= requested) return;

            var newSize = Math.Max(_buffer.Length * 2, _buffer.Length + (requested - remaining));
            var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
            _buffer.AsSpan(0, _position).CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);
            _buffer = newBuffer;
        }

        public void Dispose() => ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);
    }
}
