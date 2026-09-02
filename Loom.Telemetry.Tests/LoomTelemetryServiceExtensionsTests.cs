using System;
using Loom.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Loom.Telemetry.Tests;

/// <summary>
/// Covers the DI entry point a package consumer calls. Until the parameterless overload
/// existed the only correct call was AddLoomTelemetry(options => { }), which is what the
/// packaged-consumer AOT gate in ci/consumer-aot-gate had to write.
/// </summary>
public class LoomTelemetryServiceExtensionsTests
{
    [Fact]
    public void AddLoomTelemetry_NoArguments_RegistersOptions()
    {
        var services = new ServiceCollection();

        services.AddLoomTelemetry();

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<LoomTelemetryOptions>());
    }

    [Fact]
    public void AddLoomTelemetry_NoArguments_ReturnsSameCollectionForChaining()
    {
        var services = new ServiceCollection();

        var returned = services.AddLoomTelemetry();

        Assert.Same(services, returned);
    }

    [Fact]
    public void AddLoomTelemetry_WithCallback_InvokesItWithTheRegisteredOptions()
    {
        var services = new ServiceCollection();
        LoomTelemetryOptions? seen = null;

        services.AddLoomTelemetry(o => seen = o);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(seen);
        // The instance handed to the callback is the one registered, not a copy - the
        // other Loom packages configure through this object.
        Assert.Same(seen, provider.GetService<LoomTelemetryOptions>());
    }

    [Fact]
    public void AddLoomTelemetry_RegistersOptionsAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddLoomTelemetry();

        using var provider = services.BuildServiceProvider();
        Assert.Same(
            provider.GetService<LoomTelemetryOptions>(),
            provider.GetService<LoomTelemetryOptions>());
    }

    [Fact]
    public void AddLoomTelemetry_NullConfigure_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        // Previously this dereferenced the delegate and threw NullReferenceException from
        // inside the method, which told the caller nothing about which argument was wrong.
        var ex = Assert.Throws<ArgumentNullException>(
            () => services.AddLoomTelemetry((Action<LoomTelemetryOptions>)null!));
        Assert.Equal("configure", ex.ParamName);
    }

    [Fact]
    public void AddLoomTelemetry_NullServices_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => ((IServiceCollection)null!).AddLoomTelemetry(static _ => { }));
        Assert.Equal("services", ex.ParamName);
    }
}
