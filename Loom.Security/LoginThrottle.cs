namespace Loom.Security;

/// <summary>Fixed-window failure counter, 5 per IP per 15 minutes.
///
/// Honest limitation: both hosts bind loopback, so in practice every request presents as
/// 127.0.0.1 and this degrades to a global throttle - one attacker can lock out the
/// operator. That is the correct trade for a single-operator diagnostic tool, but it is a
/// trade. The real brute-force control is the ~74 ms PBKDF2 cost.</summary>
public sealed class LoginThrottle(TimeProvider clock)
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private const int MaxTrackedClients = 1024;

    private readonly Dictionary<string, (int Failures, DateTimeOffset WindowStart)> _clients = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public bool IsBlocked(string client, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        var now = clock.GetUtcNow();

        lock (_gate)
        {
            if (!_clients.TryGetValue(client, out var entry)) return false;
            if (now - entry.WindowStart >= Window) { _clients.Remove(client); return false; }
            if (entry.Failures < MaxFailures) return false;

            retryAfter = entry.WindowStart + Window - now;
            return true;
        }
    }

    public void RecordFailure(string client)
    {
        var now = clock.GetUtcNow();

        lock (_gate)
        {
            if (_clients.TryGetValue(client, out var entry) && now - entry.WindowStart < Window)
            {
                _clients[client] = (entry.Failures + 1, entry.WindowStart);
                return;
            }

            // Bounded so spoofed source addresses cannot grow this without limit. Evict
            // the oldest window rather than clearing the table, which would erase live
            // counters and hand an attacker a free reset.
            if (_clients.Count >= MaxTrackedClients)
            {
                var oldest = _clients.OrderBy(kv => kv.Value.WindowStart).First().Key;
                _clients.Remove(oldest);
            }

            _clients[client] = (1, now);
        }
    }

    /// <summary>Live entry count. Exists so the bounding behaviour is testable - without
    /// it the cap is unobservable and its test cannot fail.</summary>
    public int TrackedClients
    {
        get { lock (_gate) { return _clients.Count; } }
    }

    public void Reset(string client)
    {
        lock (_gate) { _clients.Remove(client); }
    }
}
