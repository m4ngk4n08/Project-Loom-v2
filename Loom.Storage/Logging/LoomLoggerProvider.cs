using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Loom.Storage.Logging;

/// <summary>
/// Microsoft.Extensions.Logging provider that captures log entries into an ILogStore.
/// </summary>
public sealed class LoomLoggerProvider : ILoggerProvider
{
    private readonly ILogStore _logStore;

    public LoomLoggerProvider(ILogStore logStore)
    {
        _logStore = logStore;
    }

    public ILogger CreateLogger(string categoryName)
    {
        // Reentrancy guard #1: Loom's own components log under the "Loom." prefix.
        // If those log calls were captured, a sink that reads back from the store
        // while ingesting (e.g. the dashboard) would generate more log entries about
        // that read, which generate more entries, forever. Categories under "Loom."
        // get a no-op logger instead of ever reaching the store.
        if (categoryName.StartsWith("Loom.", System.StringComparison.Ordinal))
            return NullLogger.Instance;

        return new LoomLogger(categoryName, _logStore);
    }

    public void Dispose()
    {
    }
}
