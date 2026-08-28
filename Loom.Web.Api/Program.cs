using Loom.Web.Api.Extensions;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using System.Diagnostics;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, LoomJsonSerializerContext.Default);
});

builder.Services.AddServices();

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

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 1_048_576; // 1 MB
    options.Limits.MaxConcurrentConnections = 1000;
    options.Limits.MaxRequestLineSize = 8192; // 8 KB
});

var app = builder.Build();

// ============================================================================
// Enable WebSockets
// ============================================================================

if (corsOrigins.Length > 0)
{
    app.UseCors();
}

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

// ============================================================================
// Configure Middleware Pipeline
// ============================================================================

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

if(!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}


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

app.MapApiEndpoints();

app.Run();