using Xunit;

// Loom's telemetry API is deliberately static and process-global: LoomMetrics.Buffers,
// LoomSampling rules, LoomCollectors, and the alerting rule registry are all shared
// singletons (see ADR-5). That is correct for the product - one process, one telemetry
// pipeline - but it means test classes cannot be isolated from one another by any
// per-class means.
//
// Nearly every test class here already resets that state in its constructor/Dispose
// (LoomMetrics.ResetForTesting(), LoomSampling.ClearRules()). Those resets are correct,
// but xUnit runs test *classes* in parallel by default, so the cleanup itself becomes
// the race: one class calling Buffers.Clear() in its constructor wipes the records
// another class wrote and is about to assert on. That produced intermittent failures in
// SamplingTests and the Exporters suite, and two tests were disabled with
// [Fact(Skip = "...test isolation issues")] rather than fixed.
//
// Serialising the assembly makes the existing per-class resets sufficient. The suite is
// small and I/O-light, so the wall-clock cost is negligible next to non-deterministic
// results.
//
// Note: Alerting/AlertTestCollection.cs already declares
// [CollectionDefinition("AlertTests", DisableParallelization = true)]. That remains valid
// but is now redundant - it only ever serialised alert tests against each other, not
// against the other classes mutating the same global buffers.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
