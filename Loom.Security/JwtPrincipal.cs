namespace Loom.Security;

/// <summary>Authority carried by a validated token. Full is an interactive operator
/// login; Metrics is a Prometheus scrape token restricted to the two scrape routes.</summary>
public enum JwtScope { Full, Metrics }

public readonly record struct JwtPrincipal(string Subject, JwtScope Scope);

public enum JwtFailure
{
    None,
    Malformed,
    BadAlgorithm,
    BadSignature,
    BadScope,
    Expired,
    NotYetValid,
    SessionExpired
}
