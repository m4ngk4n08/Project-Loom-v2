using Loom.Telemetry;
using Xunit;

namespace Loom.Telemetry.Tests;

public sealed class W3CTraceIdTests
{
    [Fact]
    public void TryParseTraceId_ValidHex_RoundTripsThroughFormat()
    {
        var hex = "4bf92f3577b34da6a3ce929d0e0e4736";

        var parsed = W3CTraceId.TryParseTraceId(hex, out var hi, out var lo);

        Assert.True(parsed);
        var formatted = W3CTraceId.FormatTraceId(hi, lo);
        Assert.Equal(hex, formatted);
    }

    [Fact]
    public void TryParseTraceId_Empty_ReturnsFalseAndZeros()
    {
        var parsed = W3CTraceId.TryParseTraceId("", out var hi, out var lo);

        Assert.False(parsed);
        Assert.Equal(0UL, hi);
        Assert.Equal(0UL, lo);
    }

    [Fact]
    public void TryParseTraceId_WrongLength_ReturnsFalse()
    {
        var parsed = W3CTraceId.TryParseTraceId("abcd", out var hi, out var lo);

        Assert.False(parsed);
        Assert.Equal(0UL, hi);
        Assert.Equal(0UL, lo);
    }

    [Fact]
    public void TryParseTraceId_NonHex_ReturnsFalse()
    {
        var parsed = W3CTraceId.TryParseTraceId("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz", out var hi, out var lo);

        Assert.False(parsed);
        Assert.Equal(0UL, hi);
        Assert.Equal(0UL, lo);
    }

    [Fact]
    public void FormatTraceId_BothZero_ReturnsNull()
    {
        var formatted = W3CTraceId.FormatTraceId(0, 0);

        Assert.Null(formatted);
    }

    [Fact]
    public void TryParseSpanId_ValidHex_RoundTripsThroughFormat()
    {
        var hex = "00f067aa0ba902b7";

        var parsed = W3CTraceId.TryParseSpanId(hex, out var id);

        Assert.True(parsed);
        var formatted = W3CTraceId.FormatSpanId(id);
        Assert.Equal(hex, formatted);
    }

    [Fact]
    public void TryParseSpanId_Empty_ReturnsFalseAndZero()
    {
        var parsed = W3CTraceId.TryParseSpanId("", out var id);

        Assert.False(parsed);
        Assert.Equal(0UL, id);
    }

    [Fact]
    public void TryParseSpanId_WrongLength_ReturnsFalse()
    {
        var parsed = W3CTraceId.TryParseSpanId("abcd", out var id);

        Assert.False(parsed);
        Assert.Equal(0UL, id);
    }

    [Fact]
    public void TryParseSpanId_NonHex_ReturnsFalse()
    {
        var parsed = W3CTraceId.TryParseSpanId("zzzzzzzzzzzzzzzz", out var id);

        Assert.False(parsed);
        Assert.Equal(0UL, id);
    }

    [Fact]
    public void FormatSpanId_Zero_ReturnsNull()
    {
        var formatted = W3CTraceId.FormatSpanId(0);

        Assert.Null(formatted);
    }

    [Fact]
    public void TryParseTraceId_AllZeroHex_ReturnsFalse()
    {
        var parsed = W3CTraceId.TryParseTraceId(new string('0', 32), out var hi, out var lo);

        Assert.False(parsed);
        Assert.Equal(0UL, hi);
        Assert.Equal(0UL, lo);
    }

    [Fact]
    public void TryParseSpanId_AllZeroHex_ReturnsFalse()
    {
        var parsed = W3CTraceId.TryParseSpanId(new string('0', 16), out var id);

        Assert.False(parsed);
        Assert.Equal(0UL, id);
    }

    [Fact]
    public void TryParseTraceId_EmbeddedWhitespace_ReturnsFalseAndZeros()
    {
        var parsed = W3CTraceId.TryParseTraceId(" af7651916cd43d  af7651916cd43d ", out var hi, out var lo);

        Assert.False(parsed);
        Assert.Equal(0UL, hi);
        Assert.Equal(0UL, lo);
    }

    [Fact]
    public void TryParseSpanId_PaddedWithSpaces_ReturnsFalseAndZero()
    {
        var parsed = W3CTraceId.TryParseSpanId(" af7651916cd43 ", out var id);

        Assert.False(parsed);
        Assert.Equal(0UL, id);
    }

    [Fact]
    public void TryParseTraceId_UppercaseHex_ParsesAndFormatsLowercase()
    {
        var lower = "4bf92f3577b34da6a3ce929d0e0e4736";
        var upper = lower.ToUpperInvariant();

        var parsed = W3CTraceId.TryParseTraceId(upper, out var hi, out var lo);

        Assert.True(parsed);
        var formatted = W3CTraceId.FormatTraceId(hi, lo);
        Assert.Equal(lower, formatted);
    }
}
