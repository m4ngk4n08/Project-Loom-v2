namespace Loom.DevTools.Rendering;

/// <summary>
/// Classifies a metric name into the cpu/memory/thread categories used by
/// `loom metrics &lt;pid&gt; [cpu|memory|thread]` and `loom metrics &lt;pid&gt; --live`
/// (the c/m/t keys), so the static and live views agree on what belongs where.
/// </summary>
public static class MetricCategoryFilter
{
    public static bool IsCpu(string name) =>
        Contains(name, "cpu") || Contains(name, "elapsed") || Contains(name, "duration");

    public static bool IsMemory(string name) =>
        Contains(name, "memory") || Contains(name, "alloc") || Contains(name, "gc") || Contains(name, "heap");

    public static bool IsThread(string name) =>
        Contains(name, "thread") || Contains(name, "lock") || Contains(name, "contention");

    private static bool Contains(string name, string token) =>
        name.Contains(token, StringComparison.OrdinalIgnoreCase);
}
