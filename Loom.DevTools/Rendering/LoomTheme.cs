using Spectre.Console;

namespace Loom.DevTools.Rendering;

/// <summary>
/// Single source of truth for CLI colors, matching the web dashboard's design
/// language (Phase 16). No hex literals anywhere else in this project.
/// </summary>
public static class LoomTheme
{
    /// <summary>
    /// Series palette, fixed order — PHASE-16-DASHBOARD.md:193. Do not reorder or add
    /// colors here; use <see cref="Series"/> to cycle through it for an Nth series.
    /// </summary>
    public static readonly Color[] SeriesPalette =
    [
        Color.FromHex("#14b8a6"), // teal
        Color.FromHex("#3b82f6"), // blue
        Color.FromHex("#8b5cf6"), // violet
        Color.FromHex("#f59e0b"), // amber
        Color.FromHex("#ec4899"), // pink
    ];

    public static readonly Color Accent = SeriesPalette[0];
    public static readonly Color Dim = Color.Grey58;
    public static readonly Color Good = SeriesPalette[0];
    public static readonly Color Warn = SeriesPalette[3];

    // Not part of the fixed series palette (which has no red) — needed for
    // threshold coloring (e.g. a metric past a critical bound). Tailwind red-500,
    // consistent with the Tailwind-derived series colors above.
    public static readonly Color Critical = Color.FromHex("#ef4444");

    public static readonly Style AccentStyle = new(foreground: Accent);
    public static readonly Style DimStyle = new(foreground: Dim);
    public static readonly Style GoodStyle = new(foreground: Good);
    public static readonly Style WarnStyle = new(foreground: Warn);
    public static readonly Style CriticalStyle = new(foreground: Critical, decoration: Decoration.Bold);

    /// <summary>Cycles through <see cref="SeriesPalette"/> for the Nth series in a chart.</summary>
    public static Color Series(int index) => SeriesPalette[index % SeriesPalette.Length];
}
