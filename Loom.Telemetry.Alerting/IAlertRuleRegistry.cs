namespace Loom.Telemetry.Alerting;

public interface IAlertRuleRegistry
{
    /// <summary>The rules as of this call. The returned collection never changes once
    /// handed out - a mutation swaps in a new one - so it is safe to enumerate without a
    /// lock while other threads mutate the registry.</summary>
    IReadOnlyList<AlertRule> Snapshot();

    /// <summary>Replaces any existing rule with the same Name.</summary>
    IAlertRuleRegistry AddAlert(string name, Action<AlertBuilder> configure);

    /// <summary>Adds a pre-built rule, replacing any existing rule with the same Name.</summary>
    IAlertRuleRegistry Add(AlertRule rule);

    /// <summary>True if a rule was found and removed.</summary>
    bool Remove(string name);

    /// <summary>Increments on every mutation. Lets a reader detect change without polling
    /// the whole snapshot.</summary>
    int Version { get; }
}
