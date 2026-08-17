using Loom.Telemetry;

namespace Loom.Telemetry.Alerting;

public static class LoomTelemetryOptionsAlertingExtensions
{
    public static readonly List<AlertRule> Rules = [];

    public static LoomTelemetryOptions AddAlert(this LoomTelemetryOptions options, string name, Action<AlertBuilder> configure)
    {
        var builder = new AlertBuilder(name);
        configure(builder);
        Rules.Add(builder.Build());
        return options;
    }
}

public class AlertBuilder(string name)
{
    private string _metricName = "";
    private TimeSpan _window = TimeSpan.FromMinutes(5);
    private Func<MetricAggregate, bool> _condition = static _ => false;
    private readonly List<Type> _targetTypes = [];

    /// <summary>metrics => metrics.ErrorCount > 100 from the original design becomes
    /// agg => agg.Count > 100 here — same delegate shape, now scoped to one metric's
    /// pre-aggregated window instead of an ambient "metrics" object.</summary>
    public AlertBuilder When(string metricName, Func<MetricAggregate, bool> condition)
    {
        _metricName = metricName;
        _condition = condition;
        return this;
    }

    public AlertBuilder InWindow(TimeSpan window) { _window = window; return this; }

    public AlertBuilder Notify<T>() where T : class, IAlertTarget
    {
        _targetTypes.Add(typeof(T));
        return this;
    }

    public AlertRule Build()
    {
        var rule = new AlertRule(name, _metricName, _window) { Condition = _condition };
        rule.TargetTypes.AddRange(_targetTypes);
        return rule;
    }
}
