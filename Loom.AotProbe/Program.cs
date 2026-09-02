// Loom.AotProbe exists to prove that referencing Loom.Telemetry (and the source
// generator it pulls in) does not break a consumer's Native AOT publish. Its binary
// size is not a product metric - it exists to fail the build when an AOT constraint
// is broken.
using Loom.Telemetry;

var probe = new Probe();
probe.DoWork();
Console.WriteLine("AOT probe OK");
return 0;

public sealed class Probe
{
    [LoomProfile(Name = "Probe.DoWork")]
    public void DoWork()
    {
        // Deliberately trivial. This exists to make the generator emit a wrapper,
        // not to measure anything.
        Thread.Sleep(1);
    }
}
