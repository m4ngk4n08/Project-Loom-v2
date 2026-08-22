using System.Buffers;
using System.Buffers.Text;
using System.Text;
using Loom.Storage;

namespace Loom.Telemetry.Exporters.Prometheus;

/// <summary>
/// Hand-written OpenMetrics text formatter for Prometheus scraping.
/// Zero reflection. Writes UTF-8 directly into an IBufferWriter&lt;byte&gt; - no
/// StringBuilder, no per-metric string allocation, no LINQ.
/// </summary>
public static class PrometheusFormatter
{
    /// <summary>
    /// Format all metrics from the store as OpenMetrics text, writing UTF-8 directly
    /// into <paramref name="writer"/> (e.g. an HTTP response's PipeWriter).
    /// </summary>
    public static void Format(IMetricStore store, IBufferWriter<byte> writer)
    {
        var buffers = store.GetBuffers();
        // MetricRecord holds reference fields (Tags, ExceptionType), so it can't be
        // stackalloc'd - rent one scratch slot for the whole call instead of once per
        // metric name.
        var typePeekBuffer = new MetricRecord[1];

        foreach (var (metricName, buffer) in buffers)
        {
            var snapshot = buffer.Snapshot();
            if (snapshot.Length == 0) continue;

            // Determine metric type from buffer content.
            if (buffer.TryReadRecent(typePeekBuffer) == 0) continue;

            var metricType = typePeekBuffer[0].Type;
            var prometheusType = MapToPrometheusTypeUtf8(metricType);

            WriteUtf8(writer, "# TYPE "u8);
            WriteSanitizedName(writer, metricName);
            WriteUtf8(writer, " "u8);
            WriteUtf8(writer, prometheusType);
            WriteNewLine(writer);

            WriteUtf8(writer, "# HELP "u8);
            WriteSanitizedName(writer, metricName);
            WriteUtf8(writer, " Loom telemetry metric"u8);
            WriteNewLine(writer);

            // For counter/gauge: output most recent value.
            if (metricType is MetricType.Counter or MetricType.Gauge)
            {
                // Snapshot() returns newest-first, so [0] is the most recent record.
                var latest = snapshot[0];
                WriteSanitizedName(writer, metricName);
                WriteUtf8(writer, " "u8);
                WriteF2(writer, latest.Value);
                WriteNewLine(writer);
            }
            // For histogram/method execution: output summary statistics.
            else
            {
                var count = snapshot.Length;
                var scratch = ArrayPool<double>.Shared.Rent(count);
                try
                {
                    var values = scratch.AsSpan(0, count);
                    double sum = 0;
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = snapshot[i].Value;
                        sum += snapshot[i].Value;
                    }
                    values.Sort();
                    var p50 = values[count / 2];
                    var p95 = values[(int)(count * 0.95)];
                    var p99 = values[(int)(count * 0.99)];

                    WriteSanitizedName(writer, metricName);
                    WriteUtf8(writer, "_count "u8);
                    WriteLong(writer, count);
                    WriteNewLine(writer);

                    WriteSanitizedName(writer, metricName);
                    WriteUtf8(writer, "_sum "u8);
                    WriteF2(writer, sum);
                    WriteNewLine(writer);

                    WriteSanitizedName(writer, metricName);
                    WriteUtf8(writer, "{quantile=\"0.5\"} "u8);
                    WriteF2(writer, p50);
                    WriteNewLine(writer);

                    WriteSanitizedName(writer, metricName);
                    WriteUtf8(writer, "{quantile=\"0.95\"} "u8);
                    WriteF2(writer, p95);
                    WriteNewLine(writer);

                    WriteSanitizedName(writer, metricName);
                    WriteUtf8(writer, "{quantile=\"0.99\"} "u8);
                    WriteF2(writer, p99);
                    WriteNewLine(writer);
                }
                finally
                {
                    ArrayPool<double>.Shared.Return(scratch);
                }
            }

            WriteNewLine(writer);
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

    private static readonly byte[] NewLineBytes = Encoding.UTF8.GetBytes(Environment.NewLine);

    private static void WriteNewLine(IBufferWriter<byte> writer) => WriteUtf8(writer, NewLineBytes);

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
    /// Writes the metric name UTF-8-encoded, with '.' and '-' replaced by '_' -
    /// transforms bytes in place instead of building an intermediate sanitized string.
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
