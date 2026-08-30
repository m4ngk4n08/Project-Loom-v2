using Loom.Web.Api.Extensions;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using System.Diagnostics;
using Loom.Security;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, LoomJsonSerializerContext.Default);
});

builder.Services.AddServices();

try
{
    builder.Services.AddLoomSecurity();
}
catch (InvalidOperationException ex)
{
    // Missing or malformed key/users file. An operator-fixable configuration error, not a
    // defect: the message from KeyMaterial already says exactly what to do. A stack trace
    // here buries that under frames nobody can act on.
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine("Loom fails closed: there is no generated-on-the-fly key in any environment.");
    Console.Error.WriteLine("  Windows dev setup:  loom auth init");
    Console.Error.WriteLine($"  Then set {KeyMaterial.KeyFileVariable} and {KeyMaterial.UsersFileVariable}.");
    return 1;
}

// Origins come from LOOM_CORS_ORIGINS as a comma-separated list. When it is unset no
// policy is registered at all, so the browser's same-origin default applies - that is
// the safe default, and it is why there is no wildcard branch here. AllowAnyOrigin is
// never correct for this service.
var corsOrigins = (Environment.GetEnvironmentVariable("LOOM_CORS_ORIGINS") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod()));
}

// Port is configurable; the loopback bind is not. An explicit Listen* call overrides
// ASPNETCORE_URLS entirely, which is the point: the service cannot be published to a
// network interface by changing an environment variable or a launch profile. Remote
// access is an SSH tunnel, or a reverse proxy in front of this port.
var httpPort = int.TryParse(Environment.GetEnvironmentVariable("LOOM_HTTP_PORT"), out var configuredPort)
    ? configuredPort
    : 5080;

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 1_048_576; // 1 MB
    options.Limits.MaxConcurrentConnections = 1000;
    options.Limits.MaxRequestLineSize = 8192; // 8 KB
    options.ListenLocalhost(httpPort);
});

var app = builder.Build();

// ============================================================================
// Enable WebSockets
// ============================================================================

// ============================================================================
// Configure Middleware Pipeline
// ============================================================================

// MOVED to the front of the pipeline. It used to sit after UseHttpsRedirection, which
// short-circuits with a 307 and never calls next() - so every Production redirect went
// out with no CSP, no nosniff, and no framing protection. (BACKLOG 4.7.)
//
// Static, allocation-free response headers. Loom.Web.Api serves JSON and WebSockets
// only - it has no HTML surface and no wwwroot - so a maximally restrictive CSP costs
// nothing here and blocks the browser from treating any response as a document.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// No UseHsts / UseHttpsRedirection here. This process serves plain HTTP on loopback and
// never terminates TLS - see BACKLOG.md 3.3. Both calls were inert without an HTTPS
// listener, and leaving them in place implied a protection that did not exist.

if (corsOrigins.Length > 0)
{
    app.UseCors();
}

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

// Routing must be explicit so authentication can run AFTER it and read endpoint
// metadata, and BEFORE the endpoints themselves.
app.UseRouting();
app.UseLoomAuthentication();

app.MapLoomTokenEndpoints();
app.MapApiEndpoints();

app.Run();
return 0;