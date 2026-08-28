using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;

namespace Loom.Security;

/// <summary>HS256 validation. No reflection, no TypeDescriptor, no assembly-scanned auth
/// schemes. Signature work is stack-allocated; the single unavoidable heap allocation is
/// the UTF-8 byte copy of the signing input.</summary>
public sealed class JwtValidator(byte[] secret, TimeProvider clock)
{
    public const string Issuer = "loom";
    public const string MetricsScope = "metrics";

    private const int SignatureBytes = 32;                   // HMAC-SHA256 is always 32
    private const int SkewSeconds = 60;
    private const int AbsoluteSessionSeconds = 12 * 60 * 60; // interactive logins only

    public JwtFailure Validate(ReadOnlySpan<char> token, out JwtPrincipal principal)
    {
        principal = default;

        var firstDot = token.IndexOf('.');
        if (firstDot < 0) return JwtFailure.Malformed;
        var lastDot = token.LastIndexOf('.');
        if (lastDot <= firstDot) return JwtFailure.Malformed;

        var signingInput = token[..lastDot];
        var headerSpan = token[..firstDot];
        var payloadSpan = token[(firstDot + 1)..lastDot];
        var signatureSpan = token[(lastDot + 1)..];

        // 1. Header FIRST. An attacker controls this field, so anything that is not
        //    exactly HS256 must be refused BEFORE a signature is computed. This is what
        //    stops `alg: none` and algorithm-confusion forgery.
        Span<byte> headerBytes = stackalloc byte[256];
        if (!Base64Url.TryDecodeFromChars(headerSpan, headerBytes, out var headerWritten))
            return JwtFailure.Malformed;

        JwtHeader? header;
        try
        {
            header = JsonSerializer.Deserialize(
                headerBytes[..headerWritten], LoomJsonSerializerContext.Default.JwtHeader);
        }
        catch (JsonException) { return JwtFailure.Malformed; }

        if (header is null || !string.Equals(header.Alg, "HS256", StringComparison.Ordinal))
            return JwtFailure.BadAlgorithm;

        // `typ` is required by the DTO, so a missing one is already Malformed. This
        // rejects a present-but-wrong value. Malformed rather than a new failure code:
        // the token is structurally not one we issue, and adding an enum member would
        // ripple into 14C's failure-to-status mapping for no gain.
        if (!string.Equals(header.Typ, "JWT", StringComparison.Ordinal))
            return JwtFailure.Malformed;

        // 2. Signature. Base64Url, NOT Convert.TryFromBase64Chars - that call returns
        //    false on any base64url input containing '-' or '_' (measured on .NET 10.0.11).
        Span<byte> provided = stackalloc byte[SignatureBytes];
        if (!Base64Url.TryDecodeFromChars(signatureSpan, provided, out var sigWritten)
            || sigWritten != SignatureBytes)
            return JwtFailure.BadSignature;

        var signingBytes = Encoding.UTF8.GetBytes(signingInput.ToString()); // the one allocation
        Span<byte> computed = stackalloc byte[SignatureBytes];
        HMACSHA256.HashData(secret, signingBytes, computed);
        if (!CryptographicOperations.FixedTimeEquals(computed, provided))
            return JwtFailure.BadSignature;

        // 3. Claims - only after the signature is trusted.
        Span<byte> payloadBytes = stackalloc byte[512];
        if (!Base64Url.TryDecodeFromChars(payloadSpan, payloadBytes, out var payloadWritten))
            return JwtFailure.Malformed;

        JwtClaims? claims;
        try
        {
            claims = JsonSerializer.Deserialize(
                payloadBytes[..payloadWritten], LoomJsonSerializerContext.Default.JwtClaims);
        }
        catch (JsonException) { return JwtFailure.Malformed; }

        if (claims is null || string.IsNullOrEmpty(claims.Sub)) return JwtFailure.Malformed;
        if (!string.Equals(claims.Iss, Issuer, StringComparison.Ordinal))
            return JwtFailure.Malformed;

        // An UNRECOGNISED scope is rejected, never treated as unscoped. Failing open into
        // full operator authority on a value we do not understand is the worst possible
        // default here.
        JwtScope scope;
        if (claims.Scope is null) scope = JwtScope.Full;
        else if (string.Equals(claims.Scope, MetricsScope, StringComparison.Ordinal)) scope = JwtScope.Metrics;
        else return JwtFailure.BadScope;

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        if (now > claims.Exp + SkewSeconds) return JwtFailure.Expired;
        if (claims.Nbf is long nbf && now + SkewSeconds < nbf) return JwtFailure.NotYetValid;

        // The 12-hour absolute cap bounds an interactive session. Service tokens are
        // deliberately exempt: a Prometheus scrape token is minted for 90 days and has no
        // session to cap (methodology 14.7.3).
        if (scope == JwtScope.Full && now > claims.Iat + AbsoluteSessionSeconds)
            return JwtFailure.SessionExpired;

        principal = new JwtPrincipal(claims.Sub, scope);
        return JwtFailure.None;
    }
}
