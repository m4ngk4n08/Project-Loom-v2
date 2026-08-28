using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Loom.Security;

public static class AuthenticationMiddleware
{
    public const string WebSocketSubprotocol = "loom.v1";
    public const string WebSocketTokenPrefix = "loom.token.";

    /// <summary>Must be registered AFTER UseRouting - it reads endpoint metadata.</summary>
    public static IApplicationBuilder UseLoomAuthentication(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var endpoint = context.GetEndpoint();

            // No matched endpoint: let it fall through to the 404 it was always going to
            // get. Returning 401 here would turn every typo into an auth prompt and would
            // leak nothing useful in exchange.
            if (endpoint is null) { await next(context); return; }

            if (endpoint.Metadata.GetMetadata<LoomAllowAnonymous>() is not null)
            {
                await next(context);
                return;
            }

            var validator = context.RequestServices.GetRequiredService<JwtValidator>();

            if (!TryReadToken(context, out var token))
            {
                await Reject(context, "invalid_token");
                return;
            }

            var failure = validator.Validate(token, out var principal);
            if (failure != JwtFailure.None)
            {
                await Reject(context, failure == JwtFailure.Expired ? "expired_token" : "invalid_token");
                return;
            }

            // A scoped service token reaches only what it was minted for. 403 not 401:
            // the credential is valid and only the authority is wrong, and a 401 would
            // send a correctly configured scraper into a re-authentication loop.
            if (principal.Scope == JwtScope.Metrics
                && endpoint.Metadata.GetMetadata<LoomMetricsScopeAllowed>() is null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            // No ClaimsPrincipal and no HttpContext.User - populating those pulls in the
            // ASP.NET Core authentication stack that ADR-3 exists to avoid.
            context.Items["loom.sub"] = principal.Subject;
            context.Items["loom.scope"] = principal.Scope;
            await next(context);
        });

    /// <summary>Browsers cannot set an Authorization header on a WebSocket handshake, so
    /// the token rides the subprotocol list instead. Never a query string: that lands the
    /// credential in Kestrel's access log and any proxy log in front of it.</summary>
    private static bool TryReadToken(HttpContext context, out string token)
    {
        token = string.Empty;

        if (context.WebSockets.IsWebSocketRequest)
        {
            foreach (var requested in context.WebSockets.WebSocketRequestedProtocols)
            {
                if (requested.StartsWith(WebSocketTokenPrefix, StringComparison.Ordinal))
                {
                    token = requested[WebSocketTokenPrefix.Length..];
                    return token.Length > 0;
                }
            }
            return false;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        token = header[7..];
        return token.Length > 0;
    }

    private static Task Reject(HttpContext context, string error)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = $"Bearer error=\"{error}\"";
        return Task.CompletedTask;
    }
}
