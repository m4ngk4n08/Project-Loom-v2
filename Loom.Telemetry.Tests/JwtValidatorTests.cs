using System;
using System.Buffers.Text;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using Loom.Security;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;

namespace Loom.Telemetry.Tests;

public class JwtValidatorTests
{
    private readonly byte[] _key = Enumerable.Repeat((byte)0x2A, 32).ToArray();
    private readonly FixedTimeProvider _clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly JwtIssuer _issuer;
    private readonly JwtValidator _validator;

    public JwtValidatorTests()
    {
        _issuer = new JwtIssuer(_key, _clock);
        _validator = new JwtValidator(_key, _clock);
    }

    [Fact]
    public void FreshOperatorToken_Validates()
    {
        var token = _issuer.Issue("alice", TimeSpan.FromHours(1));
        var result = _validator.Validate(token, out var principal);
        Assert.Equal(JwtFailure.None, result);
        Assert.Equal("alice", principal.Subject);
        Assert.Equal(JwtScope.Full, principal.Scope);
    }

    [Fact]
    public void FreshMetricsToken_Validates()
    {
        var token = _issuer.Issue("prometheus", TimeSpan.FromDays(90), JwtScope.Metrics);
        var result = _validator.Validate(token, out var principal);
        Assert.Equal(JwtFailure.None, result);
        Assert.Equal(JwtScope.Metrics, principal.Scope);
    }

    [Fact]
    public void ClockPastExpPlusSkew_ReturnsExpired()
    {
        var token = _issuer.Issue("alice", TimeSpan.FromMinutes(5));
        _clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(61));
        var result = _validator.Validate(token, out _);
        Assert.Equal(JwtFailure.Expired, result);
    }

    [Fact]
    public void ClockPastExpWithinSkew_ReturnsNone()
    {
        var token = _issuer.Issue("alice", TimeSpan.FromMinutes(5));
        _clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(30));
        var result = _validator.Validate(token, out _);
        Assert.Equal(JwtFailure.None, result);
    }

    [Fact]
    public void FullToken_AdvancedPastAbsoluteSession_ReturnsSessionExpired()
    {
        var token = _issuer.Issue("alice", TimeSpan.FromHours(24));
        _clock.Advance(TimeSpan.FromHours(13));
        var result = _validator.Validate(token, out _);
        Assert.Equal(JwtFailure.SessionExpired, result);
    }

    [Fact]
    public void MetricsToken_AdvancedPastAbsoluteSession_StillValid()
    {
        var token = _issuer.Issue("prometheus", TimeSpan.FromDays(90), JwtScope.Metrics);
        _clock.Advance(TimeSpan.FromHours(13));
        var result = _validator.Validate(token, out _);
        Assert.Equal(JwtFailure.None, result);
    }

    [Fact]
    public void PayloadSegmentAltered_ReturnsBadSignature()
    {
        var token = _issuer.Issue("alice", TimeSpan.FromHours(1));
        var parts = token.Split('.');
        var payload = parts[1].ToCharArray();
        payload[0] = payload[0] == 'A' ? 'B' : 'A';
        parts[1] = new string(payload);
        var tampered = string.Join('.', parts);
        var result = _validator.Validate(tampered, out _);
        Assert.Equal(JwtFailure.BadSignature, result);
    }

    [Fact]
    public void SignatureSegmentAltered_ReturnsBadSignature()
    {
        var token = _issuer.Issue("alice", TimeSpan.FromHours(1));
        var parts = token.Split('.');
        var sig = parts[2].ToCharArray();
        sig[0] = sig[0] == 'A' ? 'B' : 'A';
        parts[2] = new string(sig);
        var tampered = string.Join('.', parts);
        var result = _validator.Validate(tampered, out _);
        Assert.Equal(JwtFailure.BadSignature, result);
    }

    [Fact]
    public void TokenSignedWithDifferentKey_ReturnsBadSignature()
    {
        var otherKey = Enumerable.Repeat((byte)0x7F, 32).ToArray();
        var otherIssuer = new JwtIssuer(otherKey, _clock);
        var token = otherIssuer.Issue("alice", TimeSpan.FromHours(1));
        var result = _validator.Validate(token, out _);
        Assert.Equal(JwtFailure.BadSignature, result);
    }

    [Fact]
    public void HeaderAlgNone_ReturnsBadAlgorithm()
    {
        var token = _issuer.Issue("alice", TimeSpan.FromHours(1));
        var tampered = ReplaceHeader(token, "{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var result = _validator.Validate(tampered, out _);
        Assert.Equal(JwtFailure.BadAlgorithm, result);
    }

    [Fact]
    public void HeaderAlgRS256_ReturnsBadAlgorithm()
    {
        var token = _issuer.Issue("alice", TimeSpan.FromHours(1));
        var tampered = ReplaceHeader(token, "{\"alg\":\"RS256\",\"typ\":\"JWT\"}");
        var result = _validator.Validate(tampered, out _);
        Assert.Equal(JwtFailure.BadAlgorithm, result);
    }

    [Fact]
    public void HeaderTypBanana_ReturnsMalformed()
    {
        var token = _issuer.Issue("alice", TimeSpan.FromHours(1));
        var tampered = ReplaceHeader(token, "{\"alg\":\"HS256\",\"typ\":\"banana\"}");
        var result = _validator.Validate(tampered, out _);
        Assert.Equal(JwtFailure.Malformed, result);
    }

    [Fact]
    public void PayloadWithAdminScope_ReturnsBadScope()
    {
        var token = _issuer.Issue("alice", TimeSpan.FromHours(1));
        var claims = ExtractClaims(token);
        var json = $"{{\"sub\":\"{claims.Sub}\",\"iss\":\"{claims.Iss}\",\"iat\":{claims.Iat},\"exp\":{claims.Exp},\"scope\":\"admin\"}}";
        var tampered = ReplacePayload(token, json);
        var result = _validator.Validate(tampered, out _);
        Assert.Equal(JwtFailure.BadScope, result);
    }

    [Fact]
    public void PayloadWithWrongIssuer_ReturnsMalformed()
    {
        var token = _issuer.Issue("alice", TimeSpan.FromHours(1));
        var claims = ExtractClaims(token);
        var json = $"{{\"sub\":\"{claims.Sub}\",\"iss\":\"not-loom\",\"iat\":{claims.Iat},\"exp\":{claims.Exp}}}";
        var tampered = ReplacePayload(token, json);
        var result = _validator.Validate(tampered, out _);
        Assert.Equal(JwtFailure.Malformed, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("a.b")]
    public void MalformedTokenShapes_ReturnMalformed(string token)
    {
        var result = _validator.Validate(token, out _);
        Assert.Equal(JwtFailure.Malformed, result);
    }

    [Theory]
    [InlineData("not.a.jwt")]
    [InlineData("aa.bb.cc")]
    [InlineData("a.b.c")]
    [InlineData("x.y.z")]
    [InlineData("....")]
    [InlineData("..")]
    public void ThreeSegmentMalformedShapes_ReturnFailureWithoutThrowing(string token)
    {
        var result = _validator.Validate(token, out _);
        Assert.True(result is JwtFailure.Malformed or JwtFailure.BadSignature);
    }

    [Fact]
    public void SignatureWithUrlSafeCharacters_Validates()
    {
        for (var i = 0; i < 200; i++)
        {
            var token = _issuer.Issue($"user{i}", TimeSpan.FromHours(1));
            var sigSegment = token.Split('.')[2];
            if (sigSegment.Contains('-') || sigSegment.Contains('_'))
            {
                var result = _validator.Validate(token, out _);
                Assert.Equal(JwtFailure.None, result);
                return;
            }
        }

        Assert.Fail("No signature segment with '-' or '_' found in 200 iterations.");
    }

    private JwtClaims ExtractClaims(string token)
    {
        var payloadSegment = token.Split('.')[1];
        Span<byte> buffer = stackalloc byte[512];
        Base64Url.TryDecodeFromChars(payloadSegment, buffer, out var written);
        return JsonSerializer.Deserialize(buffer[..written], LoomJsonSerializerContext.Default.JwtClaims)!;
    }

    private string ReplaceHeader(string token, string json)
    {
        var parts = token.Split('.');
        parts[0] = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));
        return Resign(parts);
    }

    private string ReplacePayload(string token, string json)
    {
        var parts = token.Split('.');
        parts[1] = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));
        return Resign(parts);
    }

    private string Resign(string[] parts)
    {
        var signingInput = $"{parts[0]}.{parts[1]}";
        Span<byte> signature = stackalloc byte[32];
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(signingInput), signature);
        parts[2] = Base64Url.EncodeToString(signature);
        return $"{signingInput}.{parts[2]}";
    }
}
