using Xunit;

namespace Loom.Telemetry.Tests.Alerting;

/// <summary>
/// Collection definition to ensure alert tests run sequentially.
/// Alert tests share global state (LoomTelemetryOptionsAlertingExtensions.Rules)
/// and need to be isolated to prevent race conditions.
/// </summary>
[CollectionDefinition("AlertTests", DisableParallelization = true)]
public class AlertTestCollection
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
