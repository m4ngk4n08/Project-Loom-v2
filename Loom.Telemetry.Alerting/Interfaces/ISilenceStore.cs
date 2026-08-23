namespace Loom.Telemetry.Alerting.Interfaces;

public interface ISilenceStore
{
    void Silence(string alertName, DateTime until);
    bool IsSilenced(string alertName);
    DateTime? GetSilencedUntil(string alertName);
}
