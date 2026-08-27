namespace Loom.Telemetry.Alerting;

public enum AlertState { Firing, Resolved }

/// <summary>Why a Resolved notification was raised. ConditionCleared means the
/// rule's condition went false. NoData means the metric stopped arriving and the
/// rule's grace period elapsed — the condition was never observed to clear, so
/// this is "we stopped being able to tell", not "it got better".</summary>
public enum AlertResolutionReason { ConditionCleared, NoData }
