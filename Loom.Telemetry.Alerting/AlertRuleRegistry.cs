namespace Loom.Telemetry.Alerting;

/// <summary>Immutable-swap registry: readers take the current array reference with no lock
/// and no copy, so Snapshot() can never observe a torn write or throw from concurrent
/// mutation. Writers rebuild the whole array under a lock and swap the reference - correct
/// under contention because rules are added rarely (startup, occasional HTTP calls)
/// compared to how often the evaluation loop reads.</summary>
public sealed class AlertRuleRegistry : IAlertRuleRegistry
{
    private readonly Lock _lock = new();
    // An array, not a List: Snapshot() hands this reference straight out, and
    // IReadOnlyList<AlertRule> over a List<T> can be cast back to a List<T> that resizes
    // the very collection the evaluation loop is enumerating. An array cannot.
    private volatile AlertRule[] _rules = [];
    private int _version;

    public int Version => Volatile.Read(ref _version);

    public IReadOnlyList<AlertRule> Snapshot() => _rules;

    public IAlertRuleRegistry AddAlert(string name, Action<AlertBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new AlertBuilder(name);
        configure(builder);
        return Add(builder.Build());
    }

    public IAlertRuleRegistry Add(AlertRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        lock (_lock)
        {
            var current = _rules;
            var existing = IndexOf(current, rule.Name);

            AlertRule[] next;
            if (existing >= 0)
            {
                // Replace at the same index. Order is what /api/alerts returns to the UI,
                // so an edited rule must not jump to the bottom of the list.
                next = new AlertRule[current.Length];
                Array.Copy(current, next, current.Length);
                next[existing] = rule;
            }
            else
            {
                next = new AlertRule[current.Length + 1];
                Array.Copy(current, next, current.Length);
                next[current.Length] = rule;
            }

            _rules = next;
            Interlocked.Increment(ref _version);
        }
        return this;
    }

    public bool Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (_lock)
        {
            var current = _rules;
            var index = IndexOf(current, name);
            if (index < 0) return false;

            var next = new AlertRule[current.Length - 1];
            Array.Copy(current, 0, next, 0, index);
            Array.Copy(current, index + 1, next, index, current.Length - index - 1);

            _rules = next;
            Interlocked.Increment(ref _version);
            return true;
        }
    }

    private static int IndexOf(AlertRule[] rules, string name)
    {
        for (var i = 0; i < rules.Length; i++)
        {
            if (rules[i].Name == name) return i;
        }
        return -1;
    }
}
