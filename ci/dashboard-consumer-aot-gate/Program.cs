using Loom.Dashboard.Extensions;
using Loom.Security;

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
