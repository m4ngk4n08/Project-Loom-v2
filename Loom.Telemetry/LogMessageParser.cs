using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Loom.Telemetry;

/// <summary>
/// Splits {OriginalFormat} out of EventPipe log payloads and parses/formats
/// W3C trace/span ids. Not thread-safe by design: the TraceEvent callback
/// that owns this parser dispatches on a single thread, so no locking is used.
/// </summary>
public sealed class LogMessageParser
{
    public const int MaxPooledTemplates = 1024;
    private readonly Dictionary<string, string> _templatePool = new(StringComparer.Ordinal);
    public int PooledTemplateCount => _templatePool.Count;

    public (string? Template, string? Args) ExtractTemplateAndArgs(string? argumentsJson)
    {
        if (string.IsNullOrEmpty(argumentsJson) || argumentsJson == "{}")
            return (null, null);

        var byteCount = Encoding.UTF8.GetByteCount(argumentsJson);
        var rented = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            var written = Encoding.UTF8.GetBytes(argumentsJson, rented);
            var span = rented.AsSpan(0, written);

            int propNameStart = -1;
            int valueEnd = -1;
            string? template = null;

            try
            {
                var reader = new Utf8JsonReader(span);
                // ArgumentsJson is a flat string->string map; depth 1 only, so a nested
                // property of the same name can never be spliced out.
                while (reader.Read())
                {
                    if (reader.CurrentDepth == 1 &&
                        reader.TokenType == JsonTokenType.PropertyName &&
                        reader.ValueTextEquals("{OriginalFormat}"))
                    {
                        propNameStart = (int)reader.TokenStartIndex;

                        if (!reader.Read() || reader.TokenType != JsonTokenType.String)
                        {
                            propNameStart = -1;
                            break;
                        }

                        valueEnd = (int)reader.TokenStartIndex + reader.ValueSpan.Length + 2;
                        template = PoolTemplate(ref reader);
                        break;
                    }
                }
            }
            catch (JsonException)
            {
                return (null, argumentsJson);
            }

            if (propNameStart < 0)
                return (null, argumentsJson);

            var start = propNameStart;
            var end = valueEnd;

            var i = end;
            while (i < span.Length && IsJsonWhitespace(span[i])) i++;
            if (i < span.Length && span[i] == (byte)',')
            {
                end = i + 1;
            }
            else
            {
                var j = start - 1;
                while (j >= 0 && IsJsonWhitespace(span[j])) j--;
                if (j >= 0 && span[j] == (byte)',')
                    start = j;
            }

            var resultLength = span.Length - (end - start);
            var argBuffer = ArrayPool<byte>.Shared.Rent(resultLength);
            try
            {
                span[..start].CopyTo(argBuffer);
                span[end..].CopyTo(argBuffer.AsSpan(start));
                var argsStr = Encoding.UTF8.GetString(argBuffer, 0, resultLength);

                return argsStr == "{}" ? (template, null) : (template, argsStr);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(argBuffer);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static bool IsJsonWhitespace(byte b) => b is 0x20 or 0x09 or 0x0D or 0x0A;

    private string? PoolTemplate(ref Utf8JsonReader reader)
    {
        Span<char> buf = stackalloc char[512];
        if (reader.HasValueSequence || reader.ValueSpan.Length > 512)
            return Admit(reader.GetString());

        var n = reader.CopyString(buf);
        if (_templatePool.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(buf[..n], out var hit))
            return hit;

        return Admit(new string(buf[..n]));
    }

    private string? Admit(string? s)
    {
        if (s == null)
            return null;

        // Check first: the oversize path hands us a freshly allocated string every
        // call, so storing without looking up would return a new instance each time
        // and defeat pooling for exactly the largest templates.
        if (_templatePool.TryGetValue(s, out var hit))
            return hit;

        if (_templatePool.Count < MaxPooledTemplates)
            _templatePool[s] = s;

        return s;
    }
}
