using Loom.Telemetry;
using Microsoft.Extensions.DependencyInjection;

// Consume exactly what a stranger would: one PackageReference, the attribute, and the
// DI registration the package README advertises.
var services = new ServiceCollection();
services.AddLoomTelemetry();
using var provider = services.BuildServiceProvider();

var svc = new OrderService();
svc.Submit();

Console.WriteLine("package consumer OK");
return 0;

// `partial` is load-bearing: the generator emits the timing wrapper into a second part of
// this class. Without it the consumer fails with CS0260.
public partial class OrderService
{
    [LoomProfile(Name = "Order.Submit")]
    public void Submit() => Thread.Sleep(1);

    [LoomTrack(Name = "Order.Pending")]
    public int Pending { get; set; }
}
