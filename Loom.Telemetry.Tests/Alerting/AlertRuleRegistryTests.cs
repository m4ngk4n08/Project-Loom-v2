using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Loom.Telemetry.Alerting;
using Xunit;

namespace Loom.Telemetry.Tests.Alerting;

/// <summary>
/// Covers the guarantees that let the evaluation loop read rules from another thread
/// without a lock, and that let a test own its own rules instead of sharing a
/// process-global list.
/// </summary>
public class AlertRuleRegistryTests
{
    private static AlertRule Rule(string name, string metricName = "metric") =>
        new(name, metricName, TimeSpan.FromMinutes(1));

    [Fact]
    public void Snapshot_TakenBeforeALaterAdd_IsUnaffectedByIt()
    {
        var registry = new AlertRuleRegistry();
        registry.Add(Rule("First"));

        // The immutability guarantee the evaluation loop depends on: it snapshots once
        // per tick and enumerates without a lock, so a concurrent Add must not mutate
        // the list it is already walking.
        var before = registry.Snapshot();
        registry.Add(Rule("Second"));

        Assert.Single(before);
        Assert.Equal("First", before[0].Name);
        Assert.Equal(2, registry.Snapshot().Count);
    }

    [Fact]
    public void AddAlert_NameAlreadyPresent_ReplacesRatherThanDuplicates()
    {
        var registry = new AlertRuleRegistry();

        registry.AddAlert("Dupe", alert => alert.When("metric-a", agg => agg.Count > 1));
        registry.AddAlert("Dupe", alert => alert.When("metric-b", agg => agg.Count > 2));

        var rule = Assert.Single(registry.Snapshot());
        Assert.Equal("Dupe", rule.Name);
        // Last write wins: the evaluation loop keys _activeAlerts/_lastNotified/_lastData
        // by rule name, so two rules under one name corrupted each other's state.
        Assert.Equal("metric-b", rule.MetricName);
    }

    [Fact]
    public void Add_NameAlreadyPresent_ReplacesInPlaceKeepingOrderStable()
    {
        var registry = new AlertRuleRegistry();
        registry.Add(Rule("A"));
        registry.Add(Rule("B"));

        registry.Add(Rule("A", "replaced"));

        // Order is what /api/alerts returns to the UI: an edited rule keeps its position
        // rather than jumping to the bottom of the list.
        var names = registry.Snapshot().Select(r => r.Name).ToArray();
        Assert.Equal(new[] { "A", "B" }, names);
        Assert.Equal("replaced", registry.Snapshot().Single(r => r.Name == "A").MetricName);
    }

    [Fact]
    public void Remove_PresentRule_ReturnsTrueAndDropsIt()
    {
        var registry = new AlertRuleRegistry();
        registry.Add(Rule("Present"));

        Assert.True(registry.Remove("Present"));
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public void Remove_AbsentRule_ReturnsFalse()
    {
        var registry = new AlertRuleRegistry();
        registry.Add(Rule("Present"));

        Assert.False(registry.Remove("Absent"));
        Assert.Single(registry.Snapshot());
    }

    [Fact]
    public void Version_IncreasesOnAddAndOnSuccessfulRemove()
    {
        var registry = new AlertRuleRegistry();
        var initial = registry.Version;

        registry.Add(Rule("Versioned"));
        var afterAdd = registry.Version;

        Assert.True(afterAdd > initial, $"Version did not increase on Add: {initial} -> {afterAdd}");

        Assert.True(registry.Remove("Versioned"));
        Assert.True(registry.Version > afterAdd,
            $"Version did not increase on Remove: {afterAdd} -> {registry.Version}");
    }

    [Fact]
    public void Version_UnchangedByAFailedRemove()
    {
        var registry = new AlertRuleRegistry();
        registry.Add(Rule("Present"));
        var before = registry.Version;

        Assert.False(registry.Remove("Absent"));
        Assert.Equal(before, registry.Version);
    }

    // Public-surface guards, not internal assertions: IAlertRuleRegistry is the API an
    // alert-management endpoint calls, so a null argument must name itself rather than
    // surfacing as a NullReferenceException from inside the lock.
    [Fact]
    public void AddAlert_NullName_ThrowsArgumentNullException()
    {
        var registry = new AlertRuleRegistry();

        var ex = Assert.Throws<ArgumentNullException>(
            () => registry.AddAlert(null!, alert => alert.When("metric", agg => agg.Count > 0)));
        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    public void AddAlert_NullConfigure_ThrowsArgumentNullException()
    {
        var registry = new AlertRuleRegistry();

        var ex = Assert.Throws<ArgumentNullException>(
            () => registry.AddAlert("Named", (Action<AlertBuilder>)null!));
        Assert.Equal("configure", ex.ParamName);
    }

    [Fact]
    public void Add_NullRule_ThrowsArgumentNullException()
    {
        var registry = new AlertRuleRegistry();

        var ex = Assert.Throws<ArgumentNullException>(() => registry.Add(null!));
        Assert.Equal("rule", ex.ParamName);
    }

    [Fact]
    public void Remove_NullName_ThrowsArgumentNullException()
    {
        var registry = new AlertRuleRegistry();

        var ex = Assert.Throws<ArgumentNullException>(() => registry.Remove(null!));
        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    public async Task Add_FromSeveralThreadsConcurrently_KeepsEveryRuleAndNeverThrows()
    {
        var registry = new AlertRuleRegistry();
        const int writers = 4;
        const int perWriter = 25; // 100 adds total

        var tasks = Enumerable.Range(0, writers).Select(writer => Task.Run(() =>
        {
            for (var i = 0; i < perWriter; i++)
            {
                registry.Add(Rule($"rule-{writer}-{i}"));
                // Read while other threads write - Snapshot must never throw from
                // concurrent mutation, which a plain List<T> would.
                _ = registry.Snapshot().Count;
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        var final = registry.Snapshot();
        Assert.Equal(writers * perWriter, final.Count);
        Assert.Equal(writers * perWriter, final.Select(r => r.Name).Distinct().Count());
        Assert.Equal(writers * perWriter, registry.Version);
    }
}
