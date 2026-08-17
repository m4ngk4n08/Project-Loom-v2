# Loom.Telemetry.Alerting

Phase 11: Alerting and thresholds system for Loom telemetry.

## Features

- **Sliding Window Evaluation**: Aggregates metrics over configurable time windows
- **Flexible Conditions**: Func<MetricAggregate, bool> delegates for alert conditions
- **Multiple Notification Targets**: Webhook, Email, Console
- **Non-blocking Dispatch**: Channel-based decoupling of evaluation and notification
- **Silence Support**: Temporarily silence alerts via API

## Usage Example

### In Program.cs or Startup Configuration:

```csharp
using Loom.Telemetry;
using Loom.Telemetry.Alerting;

// Add alerting services
services.AddLoomAlerting();

// Register alert targets
services.AddAlertTarget<ConsoleAlertTarget>();
services.AddAlertTarget<WebhookAlertTarget>();

// Configure alerts
services.AddLoomTelemetry(options =>
{
    // High error rate alert
    options.AddAlert("HighErrorRate", alert => alert
        .When("PaymentFailures", agg => agg.Count > 100)
        .InWindow(TimeSpan.FromMinutes(5))
        .Notify<WebhookAlertTarget>()
        .Notify<ConsoleAlertTarget>());

    // Slow response time alert  
    options.AddAlert("SlowOrders", alert => alert
        .When("OrderProcessingTime", agg => agg.P99 > 5000)
        .InWindow(TimeSpan.FromMinutes(5))
        .Notify<EmailAlertTarget>());

    // High average latency alert
    options.AddAlert("HighLatency", alert => alert
        .When("ApiLatency", agg => agg.Average > 1000)
        .InWindow(TimeSpan.FromMinutes(10))
        .Notify<ConsoleAlertTarget>());
});
```

## API Endpoints

### List All Alerts
```
GET /api/alerts
```

Returns list of all configured alerts.

### Get Alert Details
```
GET /api/alerts/{name}
```

Returns configuration for a specific alert.

### Test Alert
```
POST /api/alerts/{name}/test
```

Manually triggers an alert (useful for testing notification targets).

### Silence Alert
```
PUT /api/alerts/{name}/silence?duration=00:30:00
```

Silences an alert for the specified duration (format: HH:MM:SS).

## Alert Condition Structure

Alert conditions receive a `MetricAggregate` struct with pre-computed statistics:

```csharp
public readonly record struct MetricAggregate(
    string MetricName, 
    long Count,      // Number of data points in window
    double Average,  // Average value
    double Max,      // Maximum value
    double P99       // 99th percentile
);
```

Example conditions:
```csharp
// Count-based
agg => agg.Count > 100

// Average-based
agg => agg.Average > 1000

// Max-based
agg => agg.Max > 5000

// Percentile-based
agg => agg.P99 > 2000

// Combined conditions
agg => agg.Count > 50 && agg.Average > 500
```

## Creating Custom Alert Targets

Implement `IAlertTarget`:

```csharp
public class CustomAlertTarget : IAlertTarget
{
    public async Task NotifyAsync(AlertNotification notification, CancellationToken ct)
    {
        // Your notification logic here
        await SendToSlack(notification);
    }
}

// Register it
services.AddAlertTarget<CustomAlertTarget>();

// Use it in alert configuration
.Notify<CustomAlertTarget>()
```

## Architecture Notes

- **Evaluation Frequency**: Ticks at (smallest window / 10) to ensure timely detection
- **Cooldown**: Each alert can fire at most once per window duration
- **Backpressure**: Bounded channel with drop-oldest strategy (capacity: 256)
- **AOT-Safe**: Uses plain delegates, not expression trees
- **Non-blocking**: Evaluation and dispatch are separate background services
