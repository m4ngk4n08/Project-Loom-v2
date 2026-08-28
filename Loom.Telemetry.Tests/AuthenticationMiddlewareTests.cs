using System;
using System.Linq;
using System.Threading.Tasks;
using Loom.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Loom.Telemetry.Tests;

public class AuthenticationMiddlewareTests
{
    private readonly byte[] _key = Enumerable.Repeat((byte)0x5A, 32).ToArray();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly JwtIssuer _issuer;
    private readonly IServiceProvider _services;

    public AuthenticationMiddlewareTests()
    {
        _issuer = new JwtIssuer(_key, _clock);
        var validator = new JwtValidator(_key, _clock);
        var collection = new ServiceCollection();
        collection.AddSingleton(validator);
        _services = collection.BuildServiceProvider();
    }

    private static DefaultHttpContext ContextFor(Endpoint? endpoint, IServiceProvider services, string? bearer = null)
    {
        var ctx = new DefaultHttpContext { RequestServices = services };
        if (endpoint is not null) ctx.SetEndpoint(endpoint);
        if (bearer is not null) ctx.Request.Headers.Authorization = $"Bearer {bearer}";
        return ctx;
    }

    private static Endpoint EndpointWith(params object[] metadata) =>
        new(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "test");

    private async Task<(bool NextRan, HttpContext Context)> InvokeAsync(HttpContext context)
    {
        var nextRan = false;
        var builder = new ApplicationBuilder(_services).UseLoomAuthentication();
        builder.Run(_ => { nextRan = true; return Task.CompletedTask; });
        var pipeline = builder.Build();

        await pipeline(context);
        return (nextRan, context);
    }

    [Fact]
    public async Task AnonymousMarkedEndpoint_NoHeader_NextRan()
    {
        var endpoint = EndpointWith(new LoomAllowAnonymous());
        var ctx = ContextFor(endpoint, _services);

        var (nextRan, _) = await InvokeAsync(ctx);

        Assert.True(nextRan);
    }

    [Fact]
    public async Task PlainEndpoint_NoHeader_Returns401WithInvalidToken()
    {
        var endpoint = EndpointWith();
        var ctx = ContextFor(endpoint, _services);

        var (nextRan, context) = await InvokeAsync(ctx);

        Assert.False(nextRan);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Contains("invalid_token", context.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task PlainEndpoint_ValidOperatorBearer_NextRanAndSubjectSet()
    {
        var token = _issuer.Issue("alice", TimeSpan.FromHours(1));
        var endpoint = EndpointWith();
        var ctx = ContextFor(endpoint, _services, token);

        var (nextRan, context) = await InvokeAsync(ctx);

        Assert.True(nextRan);
        Assert.Equal("alice", context.Items["loom.sub"]);
    }

    [Fact]
    public async Task PlainEndpoint_ExpiredBearer_Returns401WithExpiredToken()
    {
        var token = _issuer.Issue("alice", TimeSpan.FromMinutes(5));
        _clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(61));
        var endpoint = EndpointWith();
        var ctx = ContextFor(endpoint, _services, token);

        var (nextRan, context) = await InvokeAsync(ctx);

        Assert.False(nextRan);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Contains("expired_token", context.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task PlainEndpoint_GarbageBearer_Returns401()
    {
        var endpoint = EndpointWith();
        var ctx = ContextFor(endpoint, _services, "not-a-real-token");

        var (nextRan, context) = await InvokeAsync(ctx);

        Assert.False(nextRan);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task HeaderPresentButNotBearerPrefixed_Returns401()
    {
        var endpoint = EndpointWith();
        var ctx = ContextFor(endpoint, _services);
        ctx.Request.Headers.Authorization = "Basic dXNlcjpwYXNz";

        var (nextRan, context) = await InvokeAsync(ctx);

        Assert.False(nextRan);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task BearerWithEmptyToken_Returns401()
    {
        var endpoint = EndpointWith();
        var ctx = ContextFor(endpoint, _services);
        ctx.Request.Headers.Authorization = "Bearer ";

        var (nextRan, context) = await InvokeAsync(ctx);

        Assert.False(nextRan);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task MetricsScopeToken_EndpointWithMarker_NextRan()
    {
        var token = _issuer.Issue("prometheus", TimeSpan.FromDays(90), JwtScope.Metrics);
        var endpoint = EndpointWith(new LoomMetricsScopeAllowed());
        var ctx = ContextFor(endpoint, _services, token);

        var (nextRan, _) = await InvokeAsync(ctx);

        Assert.True(nextRan);
    }

    [Fact]
    public async Task MetricsScopeToken_EndpointWithoutMarker_Returns403NotUnauthorized()
    {
        var token = _issuer.Issue("prometheus", TimeSpan.FromDays(90), JwtScope.Metrics);
        var endpoint = EndpointWith();
        var ctx = ContextFor(endpoint, _services, token);

        var (nextRan, context) = await InvokeAsync(ctx);

        Assert.False(nextRan);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task OperatorToken_EndpointWithMetricsScopeMarker_NextRan()
    {
        var token = _issuer.Issue("alice", TimeSpan.FromHours(1));
        var endpoint = EndpointWith(new LoomMetricsScopeAllowed());
        var ctx = ContextFor(endpoint, _services, token);

        var (nextRan, _) = await InvokeAsync(ctx);

        Assert.True(nextRan);
    }

    [Fact]
    public async Task NullEndpoint_NextRan()
    {
        var ctx = ContextFor(null, _services);

        var (nextRan, _) = await InvokeAsync(ctx);

        Assert.True(nextRan);
    }
}
