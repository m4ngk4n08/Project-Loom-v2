using Loom.Dashboard;
using Loom.Dashboard.Extensions;
using Loom.Security;
using Loom.Storage;
using Loom.Web.Contracts;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

const int PreferredPort = 5209;

// Parse arguments
if (args.Length == 0 || !int.TryParse(args[0], out var targetPid))
{
    if (args is ["--version"])
    {
        Console.WriteLine("loom-dashboard 1.0.0");
        return 0;
    }
    Console.WriteLine("Usage: loom-dashboard <pid> [--port <n>]");
    Console.WriteLine("  Starts the Loom dashboard, pulling metrics from the specified process.");
    Console.WriteLine("  Listen port precedence: --port <n>  >  LOOM_DASHBOARD_PORT env var  >  5209 (falls back to a free port if taken).");
    Console.WriteLine("  --port and LOOM_DASHBOARD_PORT are explicit: if that port is taken, the dashboard fails instead of picking another one.");
    return 0;
}

// --port <n> takes precedence over LOOM_DASHBOARD_PORT, which takes precedence
// over the default probe-and-fallback behavior. Either explicit source disables
// the fallback below — determinism is the point of setting it explicitly.
string? portArg = null;
for (var i = 1; i < args.Length; i++)
{
    if (args[i] != "--port") continue;

    if (i + 1 >= args.Length)
    {
        Console.WriteLine("Error: --port requires a value.");
        return 1;
    }
    portArg = args[i + 1];
    break;
}

var portSource = portArg is not null ? "--port" : null;
if (portArg is null)
{
    var envPort = Environment.GetEnvironmentVariable("LOOM_DASHBOARD_PORT");
    if (!string.IsNullOrEmpty(envPort))
    {
        portArg = envPort;
        portSource = "LOOM_DASHBOARD_PORT";
    }
}

int? explicitPort = null;
if (portArg is not null)
{
    if (!int.TryParse(portArg, out var parsedPort) || parsedPort is < 1 or > 65535)
    {
        Console.WriteLine($"Error: Invalid {portSource} value '{portArg}'. Must be an integer between 1 and 65535.");
        return 1;
    }
    explicitPort = parsedPort;
}

// Verify target process exists
try
{
    using var proc = Process.GetProcessById(targetPid);
    Console.WriteLine($"Connecting to: {proc.ProcessName} (pid {targetPid})");
}
catch
{
    Console.WriteLine($"Error: Process {targetPid} not found.");
    return 1;
}

var sessionStartedAtUtc = DateTime.UtcNow;

// Pick the dashboard's listen port. Defaults to PreferredPort so the common
// single-instance case is unchanged; falls back to an OS-assigned free port
// when it's taken (e.g. a second dashboard watching another PID), so multiple
// instances can run side by side without a manual --port flag. An explicit
// --port/LOOM_DASHBOARD_PORT skips the fallback entirely — see the RunAsync
// catch below for the loud failure when that explicit port is unavailable.
var port = explicitPort ?? ResolvePort(PreferredPort);

// Build the web application
var builder = WebApplication.CreateBuilder(Array.Empty<string>());

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, LoomJsonSerializerContext.Default);
});

builder.Services.AddDashboardServices(targetPid);
builder.Services.AddSingleton<MetricsResponseBuilder>();

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

// Kestrel config
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.ListenLocalhost(port);
});

builder.Logging.SetMinimumLevel(LogLevel.Information);

var app = builder.Build();

// WebSocket support
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

// Serve embedded Angular assets (if available)
IFileProvider? embeddedProvider = null;
try
{
    embeddedProvider = new ManifestEmbeddedFileProvider(Assembly.GetExecutingAssembly(), "wwwroot");
    app.UseStaticFiles(new StaticFileOptions { FileProvider = embeddedProvider });
}
catch (InvalidOperationException)
{
    Console.WriteLine("  Warning: No dashboard assets embedded. API-only mode.");
    Console.WriteLine("  Build Angular first: cd Loom.Web.Frontend && ng build");
    Console.WriteLine("  Then repack: dotnet pack Loom.Dashboard -c Release\n");
}

// Routing before authentication so the middleware can read endpoint metadata. Static
// files are registered ABOVE this point and short-circuit before it, which is what keeps
// the Angular bundle loadable before a token exists.
app.UseRouting();
app.UseLoomAuthentication();

var metricsBuilder = app.Services.GetRequiredService<MetricsResponseBuilder>();
app.MapLoomTokenEndpoints();
app.MapDashboardEndpoints(targetPid, sessionStartedAtUtc, embeddedProvider, metricsBuilder);

var dashboardUrl = $"http://localhost:{port}";
Console.WriteLine($"\n  Loom Dashboard running at {dashboardUrl}");
if (portSource is not null)
{
    Console.WriteLine($"  (Port {port} set explicitly via {portSource}.)");
}
else if (port != PreferredPort)
{
    Console.WriteLine($"  (Port {PreferredPort} was in use — picked a free port instead.)");
}
Console.WriteLine($"  Pulling metrics from PID {targetPid}");
Console.WriteLine($"  Press Ctrl+C to stop.\n");

// Auto-open browser
try
{
    Process.Start(new ProcessStartInfo(dashboardUrl) { UseShellExecute = true });
}
catch { }

try
{
    await app.RunAsync();
}
catch (IOException) when (portSource is not null)
{
    Console.WriteLine($"\nError: Could not bind to port {port} — it's already in use.");
    Console.WriteLine($"  ({portSource} was set explicitly, so the automatic fallback is disabled.)");
    return 1;
}

// Session summary — emitted on clean shutdown so external consumers
// (logs collectors, AI analysis) get a digest of the whole observation window.
var summaryStore = app.Services.GetRequiredService<IMetricStore>();
var summaryLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Loom.SessionSummary");
var metricNames = summaryStore.GetMetricNames();
long totalRecords = 0;
foreach (var name in metricNames)
{
    totalRecords += summaryStore.ReadRecent(name, 8192).Length;
}
summaryLogger.LogInformation(
    "Session summary: targetPid={TargetPid} uptime={UptimeSeconds}s metrics={MetricCount} totalRecords={TotalRecords}",
    targetPid,
    (long)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds,
    metricNames.Count,
    totalRecords);

return 0;

// Probes loopback binding for the preferred port; falls back to an OS-assigned
// free port (TcpListener bound to port 0) if it's taken. Cross-platform: both
// Windows and Linux/macOS surface a taken port as SocketException on bind.
static int ResolvePort(int preferredPort)
{
    try
    {
        using var probe = new TcpListener(IPAddress.Loopback, preferredPort);
        probe.Start();
        return preferredPort;
    }
    catch (SocketException)
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
