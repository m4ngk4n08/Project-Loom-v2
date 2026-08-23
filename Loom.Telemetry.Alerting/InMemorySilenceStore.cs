using System.Collections.Concurrent;
using Loom.Telemetry.Alerting.Interfaces;

namespace Loom.Telemetry.Alerting;

public sealed class InMemorySilenceStore : ISilenceStore
{
    private readonly ConcurrentDictionary<string, DateTime> _silenced = new();

    public void Silence(string alertName, DateTime until)
    {
        _silenced[alertName] = until;
    }

    public bool IsSilenced(string alertName)
    {
        if (!_silenced.TryGetValue(alertName, out var until))
            return false;

        if (DateTime.UtcNow >= until)
        {
            _silenced.TryRemove(alertName, out _);
            return false;
        }

        return true;
    }

    public DateTime? GetSilencedUntil(string alertName)
    {
        return _silenced.TryGetValue(alertName, out var until) ? until : null;
    }
}
