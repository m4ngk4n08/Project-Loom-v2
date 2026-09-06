using System.Linq;
using Xunit;

namespace Loom.Telemetry.Tests;

public interface IInterfaceDispatchProbe
{
    [LoomProfile(Name = "InterfaceDispatchProbe.Do")]
    int Do(int x);
}

public sealed class InterfaceDispatchProbeImpl : IInterfaceDispatchProbe
{
    public int Do(int x) => x * 2;
}

/// <summary>
/// [LoomProfile] on a concrete class's method only intercepts calls that resolve
/// directly to that method. A call made through an interface-typed reference resolves
/// to the interface's method symbol at compile time, which carries no attribute unless
/// the interface method itself is tagged - tagging the class alone does not reach calls
/// made this way, which is the normal shape of dependency-injected code.
///
/// This test locks in the supported path: tag the interface method, and every call made
/// through that interface - regardless of the concrete instance behind it - gets
/// intercepted. See PACKAGE.md's "Interfaces and dependency injection" section.
/// </summary>
public sealed class LoomProfileInterfaceDispatchTests
{
    [Fact]
    public void TaggingTheInterfaceMethod_InterceptsCallsMadeThroughTheInterface()
    {
        IInterfaceDispatchProbe probe = new InterfaceDispatchProbeImpl();

        var result = probe.Do(21);

        Assert.Equal(42, result);

        var recent = LoomMetrics.GetRecentMetrics(200);
        var found = recent.FirstOrDefault(m => m.Name == "InterfaceDispatchProbe.Do");

        Assert.NotNull(found.Name);
        Assert.Equal(MetricType.Histogram, found.Type);
    }
}
