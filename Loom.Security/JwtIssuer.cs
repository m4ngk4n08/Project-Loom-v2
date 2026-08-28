using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;

namespace Loom.Security;

/// <summary>Mints HS256 tokens with the same key JwtValidator checks. Kept adjacent to
/// the validator on purpose: a drift between issue and validate is the classic source of
/// "works locally, 401 in production".</summary>
public sealed class JwtIssuer(byte[] secret, TimeProvider clock)
{
    public string Issue(string subject, TimeSpan lifetime, JwtScope scope = JwtScope.Full) =>
        IssueWithSessionStart(subject, clock.GetUtcNow().ToUnixTimeSeconds(), lifetime, scope);

    /// <summary>Issues a token whose `iat` is the ORIGINAL session start rather than now.
    /// Refresh uses this so the 12-hour absolute cap in JwtValidator keeps counting from
    /// the real login; otherwise a session could be renewed indefinitely.</summary>
    public string IssueWithSessionStart(string subject, long sessionStartUnix, TimeSpan lifetime, JwtScope scope = JwtScope.Full)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        var now = clock.GetUtcNow().ToUnixTimeSeconds();

        var header = new JwtHeader { Alg = "HS256", Typ = "JWT" };
        var claims = new JwtClaims
        {
            Sub = subject,
            Iss = JwtValidator.Issuer,
            Iat = sessionStartUnix,
            Exp = now + (long)lifetime.TotalSeconds,
            Scope = scope == JwtScope.Metrics ? JwtValidator.MetricsScope : null
        };

        var headerJson = JsonSerializer.SerializeToUtf8Bytes(
            header, LoomJsonSerializerContext.Default.JwtHeader);
        var claimsJson = JsonSerializer.SerializeToUtf8Bytes(
            claims, LoomJsonSerializerContext.Default.JwtClaims);

        var signingInput = $"{Base64Url.EncodeToString(headerJson)}.{Base64Url.EncodeToString(claimsJson)}";

        Span<byte> signature = stackalloc byte[32];
        HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(signingInput), signature);

        return $"{signingInput}.{Base64Url.EncodeToString(signature)}";
    }
}
