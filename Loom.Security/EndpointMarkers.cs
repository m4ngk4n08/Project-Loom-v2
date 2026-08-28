namespace Loom.Security;

/// <summary>Marks an endpoint reachable without a token. Attached explicitly at map time -
/// never inferred from a path, because a path allow-list is how /api/token/../logs
/// becomes a bypass.</summary>
public sealed class LoomAllowAnonymous;

/// <summary>Marks an endpoint a scope-restricted service token may reach. Only the two
/// Prometheus scrape routes carry this.</summary>
public sealed class LoomMetricsScopeAllowed;
