using Loom.Telemetry.Alerting;

namespace Loom.Telemetry.Alerting.Interfaces;

public interface IAlertTarget
{
    Task NotifyAsync(AlertNotification notification, CancellationToken ct);
}
