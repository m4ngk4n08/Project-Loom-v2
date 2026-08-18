using SampleMonitoredApp.Services;
using SampleMonitoredApp.Workers;

var builder = Host.CreateApplicationBuilder(args);

// Register services
builder.Services.AddSingleton<OrderService>();
builder.Services.AddSingleton<PaymentService>();

// Register background workers
builder.Services.AddHostedService<OrderProcessingWorker>();
builder.Services.AddHostedService<PaymentProcessingWorker>();
builder.Services.AddHostedService<InventoryWorker>();
builder.Services.AddHostedService<MetricPushWorker>();

var host = builder.Build();

Console.WriteLine("SampleMonitoredApp starting...");
Console.WriteLine("This app simulates e-commerce workloads with Loom telemetry.");
Console.WriteLine("Run 'loom dev' in another terminal to discover this process.");
Console.WriteLine();

await host.RunAsync();
