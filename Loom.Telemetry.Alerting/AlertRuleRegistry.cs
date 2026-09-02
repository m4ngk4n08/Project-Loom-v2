namespace Loom.Telemetry.Alerting;

/// <summary>Immutable-swap registry: readers take the current list reference with no lock
/// and no copy, so Snapshot() can never observe a torn write or throw from concurrent
/// mutation. Writers rebuild the whole list under a lock and swap the reference - correct
/// under contention because rules are added rarely (startup, occasional HTTP calls)
/// compared to how often the evaluation loop reads.</summary>
public sealed class AlertRuleRegistry : IAlertRuleRegistry
{
    private readonly Lock _lock = new();
    private volatile IReadOnlyList<AlertRule> _rules = [];
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
            var next = new List<AlertRule>(_rules.Count + 1);
            foreach (var existing in _rules)
            {
                if (existing.Name != rule.Name)
                {
                    next.Add(existing);
                }
            }
            next.Add(rule);
            _rules = next;
            _version++;
        }
        return this;
    }

    public bool Remove(string name)
    {
        lock (_lock)
        {
            var index = -1;
            for (var i = 0; i < _rules.Count; i++)
            {
                if (_rules[i].Name == name)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0) return false;

            var next = new List<AlertRule>(_rules.Count - 1);
            for (var i = 0; i < _rules.Count; i++)
            {
                if (i != index) next.Add(_rules[i]);
            }
            _rules = next;
            _version++;
            return true;
        }
    }
}
