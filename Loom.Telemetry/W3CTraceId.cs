using System;
using System.Globalization;

namespace Loom.Telemetry;

/// <summary>
/// W3C Trace Context ids in the form LogRecord stores them: a 128-bit trace id split
/// across two ulongs, and a 64-bit span id. An all-zero id is invalid per the spec, so
/// 0 doubles as "absent" and needs no nullable wrapper.
/// </summary>
public static class W3CTraceId
{
    public static bool TryParseTraceId(ReadOnlySpan<char> hex, out ulong hi, out ulong lo)
    {
        hi = 0;
        lo = 0;
        if (hex.Length != 32)
            return false;

        if (!ulong.TryParse(hex[..16], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var h))
            return false;
        if (!ulong.TryParse(hex[16..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var l))
            return false;

        // W3C: an all-zero id is invalid, and 0 is our "absent" sentinel.
        if (h == 0 && l == 0)
            return false;

        hi = h;
        lo = l;
        return true;
    }

    public static bool TryParseSpanId(ReadOnlySpan<char> hex, out ulong id)
    {
        id = 0;
        if (hex.Length != 16)
            return false;

        if (!ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed))
            return false;

        // W3C: an all-zero id is invalid, and 0 is our "absent" sentinel.
        if (parsed == 0)
            return false;

        id = parsed;
        return true;
    }

    public static string? FormatTraceId(ulong hi, ulong lo)
    {
        if (hi == 0 && lo == 0)
            return null;

        Span<char> span = stackalloc char[32];
        hi.TryFormat(span[..16], out _, "x16", CultureInfo.InvariantCulture);
        lo.TryFormat(span[16..], out _, "x16", CultureInfo.InvariantCulture);
        return new string(span);
    }

    public static string? FormatSpanId(ulong id)
    {
        if (id == 0)
            return null;

        Span<char> span = stackalloc char[16];
        id.TryFormat(span, out _, "x16", CultureInfo.InvariantCulture);
        return new string(span);
    }
}
