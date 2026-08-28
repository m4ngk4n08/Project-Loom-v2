using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Loom.Security;

public static class TokenEndpoints
{
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(60);

    public static WebApplication MapLoomTokenEndpoints(this WebApplication app)
    {
        app.MapPost("/api/token", (
            TokenRequest request,
            UserStore users,
            JwtIssuer issuer,
            LoginThrottle throttle,
            HttpContext context,
            ILoggerFactory loggerFactory) =>
        {
            var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var log = loggerFactory.CreateLogger("Loom.Security.Token");

            if (throttle.IsBlocked(client, out var retryAfter))
            {
                context.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            // Identical body for unknown user and wrong password. UserStore.Verify also
            // takes the same time in both cases - see its comment.
            if (!users.Verify(request.Username, request.Password))
            {
                throttle.RecordFailure(client);
                log.LogWarning("Failed login for {Username} from {Client}", request.Username, client);
                return Results.Json(
                    new QueryErrorResponse { Error = "Invalid credentials" },
                    LoomJsonSerializerContext.Default.QueryErrorResponse,
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            throttle.Reset(client);
            return Results.Json(
                new TokenResponse
                {
                    Token = issuer.Issue(request.Username, AccessTokenLifetime),
                    ExpiresIn = (int)AccessTokenLifetime.TotalSeconds
                },
                LoomJsonSerializerContext.Default.TokenResponse);
        })
        .WithName("IssueToken")
        .WithTags("Auth");

        app.MapPost("/api/token/refresh", (
            HttpContext context,
            JwtValidator validator,
            JwtIssuer issuer) =>
        {
            var header = context.Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
                return Results.StatusCode(StatusCodes.Status401Unauthorized);

            if (validator.Validate(header.AsSpan(7), out var principal) != JwtFailure.None)
                return Results.StatusCode(StatusCodes.Status401Unauthorized);

            // A scoped service token must not be able to renew itself into a longer life.
            if (principal.Scope != JwtScope.Full)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var sessionStart = ReadIssuedAt(header.AsSpan(7));
            return Results.Json(
                new TokenResponse
                {
                    Token = issuer.IssueWithSessionStart(principal.Subject, sessionStart, AccessTokenLifetime),
                    ExpiresIn = (int)AccessTokenLifetime.TotalSeconds
                },
                LoomJsonSerializerContext.Default.TokenResponse);
        })
        .WithName("RefreshToken")
        .WithTags("Auth");

        return app;
    }

    /// <summary>Reads `iat` from an ALREADY-VALIDATED token so refresh can preserve the
    /// original session start. Only ever called after JwtValidator returns None.</summary>
    private static long ReadIssuedAt(ReadOnlySpan<char> token)
    {
        var firstDot = token.IndexOf('.');
        var lastDot = token.LastIndexOf('.');
        var payload = System.Buffers.Text.Base64Url.DecodeFromChars(token[(firstDot + 1)..lastDot]);
        var claims = System.Text.Json.JsonSerializer.Deserialize(
            payload, LoomJsonSerializerContext.Default.JwtClaims);
        return claims!.Iat;
    }
}
