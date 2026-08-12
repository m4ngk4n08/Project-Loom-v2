using Loom.Web.Api.Extensions;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, LoomJsonSerializerContext.Default);
});

builder.Services.AddServices();

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


app.MapApiEndpoints();

app.Run();