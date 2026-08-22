using System;
using System.Linq;
using Loom.DevTools.Rendering;
using Xunit;

namespace Loom.Telemetry.Tests.DevTools;

public sealed class SparklineTests
{
    [Fact]
    public void Render_EmptyInput_ReturnsBlankWidth()
    {
        var result = Sparkline.Render(ReadOnlySpan<double>.Empty, 8);

        Assert.Equal(new string(' ', 8), result);
    }

    [Fact]
    public void Render_ZeroWidth_ReturnsEmptyString()
    {
        var result = Sparkline.Render([1.0, 2.0], 0);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Render_SingleValue_ReturnsMidBlockRightAligned()
    {
        var result = Sparkline.Render([5.0], 4);

        Assert.Equal(4, result.Length);
        Assert.Equal("   ▅", result); // 3 spaces pad + mid block (Blocks[Blocks.Length/2] = Blocks[4] = '▅')
    }

    [Fact]
    public void Render_AllIdenticalValues_AvoidsDivideByZero()
    {
        var result = Sparkline.Render([3.0, 3.0, 3.0, 3.0], 4);

        Assert.Equal(4, result.Length);
        Assert.All(result, c => Assert.Equal('▅', c));
    }

    [Fact]
    public void Render_FewerValuesThanWidth_PadsLeftWithSpaces()
    {
        var result = Sparkline.Render([1.0, 2.0], 5);

        Assert.Equal(5, result.Length);
        Assert.Equal("   ", result[..3]);
        Assert.False(result[3] == ' ');
        Assert.False(result[4] == ' ');
    }

    [Fact]
    public void Render_MoreValuesThanWidth_DownsamplesToExactWidth()
    {
        var values = Enumerable.Range(0, 100).Select(i => (double)i).ToArray();

        var result = Sparkline.Render(values, 10);

        Assert.Equal(10, result.Length);
    }

    [Fact]
    public void Render_NegativeValues_DoesNotThrowAndScalesByRange()
    {
        var result = Sparkline.Render([-10.0, -5.0, 0.0, 5.0, 10.0], 5);

        Assert.Equal(5, result.Length);
        // Ascending negative-to-positive series should render as strictly non-decreasing glyphs.
        var indices = result.Select(c => Array.IndexOf("▁▂▃▄▅▆▇█".ToCharArray(), c)).ToArray();
        for (var i = 1; i < indices.Length; i++)
            Assert.True(indices[i] >= indices[i - 1]);
    }

    [Fact]
    public void Render_ExactWidthMatch_UsesAllSamplesWithNoPadding()
    {
        var result = Sparkline.Render([1.0, 5.0, 3.0], 3);

        Assert.Equal(3, result.Length);
        Assert.DoesNotContain(' ', result);
    }
}
