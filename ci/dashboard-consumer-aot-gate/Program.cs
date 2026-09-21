using Loom.Dashboard.Extensions;
using Loom.Security;
using Loom.Web.Contracts;

// Two modes, chosen by the first argument. Consumes exactly the three public setup methods
// (AddLoomDashboard / UseLoomDashboard / MapLoomDashboard) plus the security-headers helper,
// in the call order Loom.Dashboard/Program.cs uses - minus the UI, browser launch, Assist
// wiring and session summary.

if (args is ["hash", var password])
{
    Console.WriteLine(PasswordHasher.Hash(password));
    return 0;
}

if (args is not ["serve", var pidText, var portText]
    || !int.TryParse(pidText, out var targetPid)
    || !int.TryParse(portText, out var port))
{
    Console.Error.WriteLine("usage: DashboardConsumer hash <password> | serve <pid> <port>");
    return 2;
}

var builder = WebApplication.CreateSlimBuilder(Array.Empty<string>());

// MEASURED: without this call every request 500s under AOT, /api/health included - the
// route table cannot be built because the minimal-API body binder finds no JsonTypeInfo for
// Loom.Web.Contracts.Dtos.TokenRequest ("was not provided by TypeInfoResolver of type '[]'").
// The library's three public methods do not register LoomJsonSerializerContext; the host
// must, exactly as Loom.Dashboard/Program.cs does.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, LoomJsonSerializerContext.Default);
});

// A missing key or users file throws InvalidOperationException here. Deliberately not
// caught: an unhandled exception and a non-zero exit is the fail-closed behaviour under test.
builder.Services.AddLoomDashboard(targetPid);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.ListenLocalhost(port);
});

var app = builder.Build();

app.UseLoomDashboardSecurityHeaders();
app.UseWebSockets();
app.UseRouting();
app.UseLoomDashboard();
app.MapLoomDashboard(targetPid);

await app.RunAsync();
return 0;
