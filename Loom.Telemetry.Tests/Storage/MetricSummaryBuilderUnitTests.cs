using System;
using System.Linq;
using Loom.Storage;
using Xunit;

namespace Loom.Telemetry.Tests.Storage;

// The dashboard's metric list gets its Unit from here. It used to be guessed from the
// name, which labelled a [LoomProfile] timing "count" and "order.total" as dollars.
public sealed class MetricSummaryBuilderUnitTests
{
    [Fact]
    public void BuildAll_UsesTheUnitTheSourceDeclared()
    {
        using var store = new InMemoryMetricStore();
        store.SetUnit("OrderService.PlaceOrder", MetricUnit.Milliseconds);
        store.Write(new MetricRecord("OrderService.PlaceOrder", MetricType.Histogram, 8.8, DateTime.UtcNow.Ticks));

        var summary = Assert.Single(MetricSummaryBuilder.BuildAll(store));

        Assert.Equal("ms", summary.Unit);
    }

    [Fact]
    public void BuildAll_NoDeclaredUnit_IsEmpty_NotGuessedFromTheName()
    {
        using var store = new InMemoryMetricStore();
        store.Write(new MetricRecord("order.total", MetricType.Histogram, 129.9, DateTime.UtcNow.Ticks));

        var summary = Assert.Single(MetricSummaryBuilder.BuildAll(store));

        Assert.Equal(string.Empty, summary.Unit);
    }
}
