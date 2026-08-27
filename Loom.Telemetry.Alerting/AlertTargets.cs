using Loom.Telemetry.Alerting.Interfaces;
using Loom.Web.Contracts;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace Loom.Telemetry.Alerting;

public sealed class WebhookAlertTarget(IHttpClientFactory httpClientFactory, IOptions<WebhookAlertOptions> options) : IAlertTarget
{
    // Not [ThreadStatic]-guarded like LoomLogger's hot-path pattern: notifications are
    // low-frequency (alert fire/resolve events), so a plain instance bool is sufficient
    // to log the "unconfigured" warning once instead of per-notification.
    private bool _warnedUnconfigured;

    public async Task NotifyAsync(AlertNotification notification, CancellationToken ct)
    {
        var webhookUrl = options.Value.Url;
        if (string.IsNullOrEmpty(webhookUrl))
        {
            if (!_warnedUnconfigured)
            {
                _warnedUnconfigured = true;
                Console.WriteLine("[WARN] WebhookAlertTarget: no URL configured (LOOM_ALERT_WEBHOOK_URL unset). Webhook notifications are disabled.");
            }
            return;
        }

        var payload = new Loom.Web.Contracts.Dtos.AlertWebhookPayload
        {
            Alert = notification.Rule.Name,
            Metric = notification.Rule.MetricName,
            ObservedCount = notification.Observed.Count,
            ObservedAverage = notification.Observed.Average,
            FiredAt = notification.FiredAt,
            Status = notification.State == AlertState.Resolved ? "resolved" : "firing",
            ResolvedAt = notification.ResolvedAt
        };
        var httpClient = httpClientFactory.CreateClient(nameof(WebhookAlertTarget));
        await httpClient.PostAsJsonAsync(webhookUrl, payload, LoomJsonSerializerContext.Default.AlertWebhookPayload, ct);
    }
}

public sealed class EmailAlertTarget(IEmailSender sender, string toAddress) : IAlertTarget
{
    public Task NotifyAsync(AlertNotification notification, CancellationToken ct)
    {
        var resolved = notification.State == AlertState.Resolved;
        var subject = resolved ? $"RESOLVED: Loom alert: {notification.Rule.Name}" : $"Loom alert: {notification.Rule.Name}";
        var body = resolved
            ? $"{notification.Rule.MetricName}: count={notification.Observed.Count}, avg={notification.Observed.Average:F1} — fired at {notification.FiredAt:O}, resolved at {notification.ResolvedAt:O}"
            : $"{notification.Rule.MetricName}: count={notification.Observed.Count}, avg={notification.Observed.Average:F1} at {notification.FiredAt:O}";
        return sender.SendAsync(toAddress, subject, body, ct);
    }
}

public sealed class ConsoleAlertTarget : IAlertTarget
{
    public Task NotifyAsync(AlertNotification notification, CancellationToken ct)
    {
        if (notification.State == AlertState.Resolved)
        {
            var duration = notification.ResolvedAt!.Value - notification.FiredAt;
            var suffix = notification.ResolutionReason == AlertResolutionReason.NoData ? " (no data)" : "";
            Console.WriteLine($"[RESOLVED] {notification.Rule.Name} resolved at {notification.ResolvedAt:O} (fired at {notification.FiredAt:O}, active for {duration}){suffix}");
        }
        else
        {
            Console.WriteLine($"[ALERT] {notification.Rule.Name} fired at {notification.FiredAt:O}");
        }
        Console.WriteLine($"  Metric: {notification.Rule.MetricName}");
        Console.WriteLine($"  Count: {notification.Observed.Count}, Avg: {notification.Observed.Average:F2}, Max: {notification.Observed.Max:F2}, P99: {notification.Observed.P99:F2}");
        return Task.CompletedTask;
    }
}
