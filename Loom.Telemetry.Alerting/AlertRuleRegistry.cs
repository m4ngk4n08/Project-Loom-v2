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

    public int Version => _version;

    public IReadOnlyList<AlertRule> Snapshot() => _rules;

    public IAlertRuleRegistry AddAlert(string name, Action<AlertBuilder> configure)
    {
        var builder = new AlertBuilder(name);
        configure(builder);
        return Add(builder.Build());
    }

    public IAlertRuleRegistry Add(AlertRule rule)
    {
        lock (_lock)
        {
            var current = _rules;
            var kept = 0;
            var next = new AlertRule[current.Length + 1];
            foreach (var existing in current)
            {
                if (existing.Name != rule.Name) next[kept++] = existing;
            }
            next[kept++] = rule;
            if (kept != next.Length) Array.Resize(ref next, kept);

            _rules = next;
            _version++;
        }
        return this;
    }

    public bool Remove(string name)
    {
        lock (_lock)
        {
            var current = _rules;
            var index = IndexOf(current, name);
            if (index < 0) return false;

            var next = new AlertRule[current.Length - 1];
            Array.Copy(current, 0, next, 0, index);
            Array.Copy(current, index + 1, next, index, current.Length - index - 1);

            _rules = next;
            _version++;
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
