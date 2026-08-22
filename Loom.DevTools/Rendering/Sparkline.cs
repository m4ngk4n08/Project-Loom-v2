using System.Text;

namespace Loom.DevTools.Rendering;

/// <summary>
/// Renders a series of values as a fixed-width Unicode block sparkline.
/// </summary>
public static class Sparkline
{
    private static readonly char[] Blocks = ['▁', '▂', '▃', '▄', '▅', '▆', '▇', '█'];

    /// <summary>
    /// Renders <paramref name="values"/> (oldest first) into a string exactly
    /// <paramref name="width"/> characters wide. Handles empty input, a single value,
    /// all-identical values, fewer values than width, more values than width, and
    /// negative values.
    /// </summary>
    public static string Render(ReadOnlySpan<double> values, int width)
    {
        if (width <= 0) return string.Empty;
        if (values.Length == 0) return new string(' ', width);

        double[] samples;
        int sampleCount;

        if (values.Length <= width)
        {
            samples = values.ToArray();
            sampleCount = values.Length;
        }
        else
        {
            // Downsample into `width` buckets by averaging each contiguous slice.
            samples = new double[width];
            sampleCount = width;
            var n = values.Length;
            for (var i = 0; i < width; i++)
            {
                var start = (int)((long)i * n / width);
                var end = (int)((long)(i + 1) * n / width);
                if (end <= start) end = start + 1;
                end = Math.Min(end, n);

                double sum = 0;
                var count = 0;
                for (var j = start; j < end; j++) { sum += values[j]; count++; }
                samples[i] = count > 0 ? sum / count : values[Math.Min(start, n - 1)];
            }
        }

        var min = samples[0];
        var max = samples[0];
        for (var i = 1; i < sampleCount; i++)
        {
            if (samples[i] < min) min = samples[i];
            if (samples[i] > max) max = samples[i];
        }
        var range = max - min;

        var sb = new StringBuilder(width);
        // Right-align: pad missing history (fewer samples than width) with blanks on the left.
        for (var i = 0; i < width - sampleCount; i++) sb.Append(' ');

        for (var i = 0; i < sampleCount; i++)
        {
            if (range < double.Epsilon)
            {
                // All-identical values (including the single-value case): no slope to show.
                sb.Append(Blocks[Blocks.Length / 2]);
                continue;
            }

            var normalized = (samples[i] - min) / range;
            var idx = (int)Math.Round(normalized * (Blocks.Length - 1));
            idx = Math.Clamp(idx, 0, Blocks.Length - 1);
            sb.Append(Blocks[idx]);
        }

        return sb.ToString();
    }
}
