using System;
using System.Buffers.Text;
using System.Linq;
using System.Text;
using Xunit;
using Loom.Security;

namespace Loom.Telemetry.Tests;

public class JwtIssuerTests
{
    private readonly byte[] _key = Enumerable.Repeat((byte)0x2A, 32).ToArray();
    private readonly FixedTimeProvider _clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly JwtIssuer _issuer;
    private readonly JwtValidator _validator;

    public JwtIssuerTests()
    {
        _issuer = new JwtIssuer(_key, _clock);
        _validator = new JwtValidator(_key, _clock);
    }

    [Fact]
    public void IssuedToken_RoundTripsThroughValidator()
    {
        var token = _issuer.Issue("alice", TimeSpan.FromHours(1));
        var result = _validator.Validate(token, out _);
        Assert.Equal(JwtFailure.None, result);
    }

    [Fact]
    public void IssuedToken_HasThreeSegments()
    {
        var token = _issuer.Issue("alice", TimeSpan.FromHours(1));
        Assert.Equal(3, token.Count(c => c == '.') + 1);
        Assert.Equal(2, token.Count(c => c == '.'));
    }

    [Fact]
    public void ExpMinusIat_EqualsRequestedLifetimeSeconds()
    {
        var lifetime = TimeSpan.FromMinutes(30);
        var token = _issuer.Issue("alice", lifetime);
        var payloadSegment = token.Split('.')[1];
        Span<byte> buffer = stackalloc byte[512];
        Base64Url.TryDecodeFromChars(payloadSegment, buffer, out var written);
        var claims = System.Text.Json.JsonSerializer.Deserialize(
            buffer[..written], Loom.Web.Contracts.LoomJsonSerializerContext.Default.JwtClaims)!;
        Assert.Equal((long)lifetime.TotalSeconds, claims.Exp - claims.Iat);
    }

    [Theory]
    [MemberData(nameof(NonPositiveLifetimes))]
    public void Issue_WithNonPositiveLifetime_ThrowsArgumentOutOfRangeException(TimeSpan lifetime)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _issuer.Issue("alice", lifetime));
    }

    public static TheoryData<TimeSpan> NonPositiveLifetimes() => new()
    {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(-1)
    };

    [Fact]
    public void Issue_WithEmptySubject_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _issuer.Issue("", TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Issue_WithNullSubject_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => _issuer.Issue(null!, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void OperatorToken_PayloadContainsNoScopeProperty()
    {
        var token = _issuer.Issue("alice", TimeSpan.FromHours(1));
        var payloadSegment = token.Split('.')[1];
        Span<byte> buffer = stackalloc byte[512];
        Base64Url.TryDecodeFromChars(payloadSegment, buffer, out var written);
        var json = Encoding.UTF8.GetString(buffer[..written]);
        Assert.DoesNotContain("scope", json);
    }
}
