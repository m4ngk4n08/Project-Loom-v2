using System;
using System.Linq;
using Loom.Security;
using Xunit;

namespace Loom.Telemetry.Tests;

public class JwtRefreshTests
{
    private readonly byte[] _key = Enumerable.Repeat((byte)0x3C, 32).ToArray();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly JwtIssuer _issuer;
    private readonly JwtValidator _validator;

    public JwtRefreshTests()
    {
        _issuer = new JwtIssuer(_key, _clock);
        _validator = new JwtValidator(_key, _clock);
    }

    [Fact]
    public void SessionStart11HoursOld_Validates_ReturnsNone()
    {
        var sessionStart = _clock.GetUtcNow().AddHours(-11).ToUnixTimeSeconds();
        var token = _issuer.IssueWithSessionStart("alice", sessionStart, TimeSpan.FromHours(1));

        var result = _validator.Validate(token, out _);

        Assert.Equal(JwtFailure.None, result);
    }

    [Fact]
    public void SessionStart13HoursOld_Validates_ReturnsSessionExpired()
    {
        var sessionStart = _clock.GetUtcNow().AddHours(-13).ToUnixTimeSeconds();
        var token = _issuer.IssueWithSessionStart("alice", sessionStart, TimeSpan.FromHours(1));

        var result = _validator.Validate(token, out _);

        Assert.Equal(JwtFailure.SessionExpired, result);
    }

    [Fact]
    public void IssueAndIssueWithSessionStartNow_ProduceSameIat()
    {
        var now = _clock.GetUtcNow().ToUnixTimeSeconds();

        var viaIssue = _issuer.Issue("alice", TimeSpan.FromHours(1));
        var viaSessionStart = _issuer.IssueWithSessionStart("alice", now, TimeSpan.FromHours(1));

        var iatFromIssue = ExtractIat(viaIssue);
        var iatFromSessionStart = ExtractIat(viaSessionStart);

        Assert.Equal(iatFromIssue, iatFromSessionStart);
    }

    private static long ExtractIat(string token)
    {
        var payloadSegment = token.Split('.')[1];
        Span<byte> buffer = stackalloc byte[512];
        System.Buffers.Text.Base64Url.TryDecodeFromChars(payloadSegment, buffer, out var written);
        var claims = System.Text.Json.JsonSerializer.Deserialize(
            buffer[..written], Loom.Web.Contracts.LoomJsonSerializerContext.Default.JwtClaims);
        return claims!.Iat;
    }
}
