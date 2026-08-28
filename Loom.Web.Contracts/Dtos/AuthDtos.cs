using System.Text.Json.Serialization;

namespace Loom.Web.Contracts.Dtos;

/// <summary>JWT header. Only HS256 is ever accepted; this type exists so the `alg` value
/// can be checked BEFORE a signature is computed, which is what stops `alg: none` and
/// algorithm-confusion forgery.</summary>
public sealed record JwtHeader
{
    [JsonPropertyName("alg")] public required string Alg { get; init; }
    [JsonPropertyName("typ")] public required string Typ { get; init; }
}

/// <summary>JWT payload claims. Property names are pinned with [JsonPropertyName] rather
/// than left to the context's camelCase policy: this wire format is defined by RFC 7519,
/// not by our naming convention, and must not move if that policy ever changes.</summary>
public sealed record JwtClaims
{
    [JsonPropertyName("sub")] public required string Sub { get; init; }
    [JsonPropertyName("iss")] public required string Iss { get; init; }
    [JsonPropertyName("iat")] public required long Iat { get; init; }
    [JsonPropertyName("exp")] public required long Exp { get; init; }

    /// <summary>Optional. Null means full operator authority. The only other defined
    /// value is "metrics" (Prometheus scrape). Nullable so WhenWritingNull omits it.</summary>
    [JsonPropertyName("scope")] public string? Scope { get; init; }

    /// <summary>Optional not-before. Nullable so WhenWritingNull omits it rather than
    /// emitting a meaningless 0.</summary>
    [JsonPropertyName("nbf")] public long? Nbf { get; init; }
}

public sealed record TokenRequest
{
    public required string Username { get; init; }
    public required string Password { get; init; }
}

public sealed record TokenResponse
{
    public required string Token { get; init; }
    public required int ExpiresIn { get; init; }
}
