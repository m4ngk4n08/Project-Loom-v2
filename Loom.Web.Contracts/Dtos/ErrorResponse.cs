namespace Loom.Web.Contracts.Dtos;

/// <summary>Minimal-API error body for endpoints that need a JSON error result but aren't
/// query endpoints (see QueryErrorResponse in QueryDtos.cs). Anonymous types can't be
/// serialized under PublishAot=true - there is no reflection resolver to fall back on -
/// so any Results.Json/Results.BadRequest(object) call site needs a registered DTO like
/// this one instead.</summary>
public sealed record ErrorResponse { public required string Error { get; init; } }
