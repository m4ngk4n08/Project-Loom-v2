using System;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Loom.Dashboard.Extensions;
using Loom.Security;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Loom.Telemetry.Tests.Dashboard;

// BACKLOG.md 6.34: under Native AOT reflection-based JSON is off, so a host that never
// registered LoomJsonSerializerContext returned 500 on every request.
public class DashboardJsonContextTests
{
    private static JsonOptions ResolveJsonOptions(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<JsonOptions>>().Value;
    }

    [Fact]
    public void AddLoomDashboardJsonContext_PutsTheContextInTheResolverChain()
    {
        var services = new ServiceCollection();

        services.AddLoomDashboardJsonContext();

        var chain = ResolveJsonOptions(services).SerializerOptions.TypeInfoResolverChain;
        Assert.Contains(LoomJsonSerializerContext.Default, chain);
    }

    [Fact]
    public void AddLoomDashboardJsonContext_ResolvesTokenRequestWithoutReflection()
    {
        var services = new ServiceCollection();

        services.AddLoomDashboardJsonContext();

        // Only the source-generated contexts from the chain. The chain also holds the
        // default reflection-based resolver, which would satisfy the lookup in a JIT test
        // process whether or not the helper ran - so it is deliberately left out.
        var contexts = ResolveJsonOptions(services).SerializerOptions.TypeInfoResolverChain
            .OfType<JsonSerializerContext>()
            .Cast<IJsonTypeInfoResolver>()
            .ToArray();
        var options = new System.Text.Json.JsonSerializerOptions
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(contexts)
        };

        Assert.NotNull(options.GetTypeInfo(typeof(TokenRequest)));
    }

    [Fact]
    public void WithoutTheHelper_TokenRequestCannotBeResolvedWithoutReflection()
    {
        // The control for the test above: proves its assertion can fail.
        var services = new ServiceCollection();
        services.ConfigureHttpJsonOptions(_ => { });

        var contexts = ResolveJsonOptions(services).SerializerOptions.TypeInfoResolverChain
            .OfType<JsonSerializerContext>()
            .Cast<IJsonTypeInfoResolver>()
            .ToArray();
        var options = new System.Text.Json.JsonSerializerOptions
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(contexts)
        };

        Assert.Throws<NotSupportedException>(() => options.GetTypeInfo(typeof(TokenRequest)));
    }

    [Fact]
    public void AddLoomDashboard_CallsTheHelper_EvenWhenSecurityThrows()
    {
        // AddLoomDashboard needs real key material; a missing key file makes AddLoomSecurity
        // throw. The helper runs first, so its registration must already be in the collection.
        var saved = Environment.GetEnvironmentVariable(KeyMaterial.KeyFileVariable);
        Environment.SetEnvironmentVariable(KeyMaterial.KeyFileVariable,
            Path.Combine(Path.GetTempPath(), "loom-does-not-exist-" + Guid.NewGuid().ToString("N")));
        try
        {
            var services = new ServiceCollection();

            Assert.Throws<InvalidOperationException>(() => services.AddLoomDashboard(targetPid: 1));

            var chain = ResolveJsonOptions(services).SerializerOptions.TypeInfoResolverChain;
            Assert.Contains(LoomJsonSerializerContext.Default, chain);
        }
        finally
        {
            Environment.SetEnvironmentVariable(KeyMaterial.KeyFileVariable, saved);
        }
    }
}
