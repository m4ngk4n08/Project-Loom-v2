using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, LoomJsonSerializerContext.Default);
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 1_048_576; // 1 MB
    options.Limits.MaxConcurrentConnections = 1000;
    options.Limits.MaxRequestLineSize = 8192; // 8 KB
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

if(!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.MapGet("/api/health", () =>
{
    var process = Process.GetCurrentProcess();
    var uptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();

    var response = new HealthCheckResponse
    {
        Status = "Healthy",
        Timestamp = DateTime.UtcNow,
        UptimeSeconds = (long)uptime.TotalSeconds,
        MemoryUsageMb = process.WorkingSet64 / 1_048_576.0 // Convert bytes to MB
    };

    return Results.Json(

        response,
        LoomJsonSerializerContext.Default.HealthCheckResponse,
        statusCode: 200
    );
})
.WithName("GetHealth")
.WithTags("Health")
.Produces<HealthCheckResponse>(200);

app.Run();