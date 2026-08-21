# SampleMonitoredApp

A demonstration e-commerce backend that showcases Loom telemetry capabilities.

## What This App Does

Simulates a realistic e-commerce system with:
- **Order Processing** - CPU-intensive validation, variable throughput
- **Payment Processing** - Simulated gateway calls, multiple payment methods
- **Inventory Management** - Stock tracking, memory allocations

## Telemetry Features Demonstrated

### 1. Automatic Method Instrumentation via `[LoomProfile]`
```csharp
[LoomProfile(Name = "OrderService.ProcessOrder")]
public async Task<bool> ProcessOrderAsync(int orderId)
{
    // Method automatically tracked with timing metrics
}
```

### 2. Automatic Property Tracking via `[LoomTrack]`
```csharp
[LoomTrack]
public int OrdersPerMinute { get; set; }

[LoomTrack(Name = "orders.avg_value")]
public decimal AverageOrderValue { get; private set; }

[LoomTrack]
public double SuccessRatePercent { get; private set; }

[LoomTrack(Name = "payments.active_count")]
public int ActivePayments { get; private set; }
```
Every time these properties change, a gauge metric is automatically emitted.

### 3. Custom Metrics
```csharp
LoomMetrics.RecordCounter("orders.processed", 1);
LoomMetrics.RecordHistogram("order.processing.duration", duration);
LoomMetrics.RecordGauge("inventory.total_items", totalItems);
```

### 4. Tagged Metrics
```csharp
LoomMetrics.RecordCounter("payments.attempted", 1, 
    new MetricTag("method", paymentMethod));
```

### 5. Error Tracking
- 10% order failure rate
- 5% payment decline rate
- Out-of-stock conditions

### 6. Realistic Load Patterns
- Variable processing rates (500-3000ms intervals)
- Memory allocations (tests GC metrics)
- CPU-intensive operations (validation logic)

## How to Run

### 1. Build and Run the App
```bash
cd examples/SampleMonitoredApp
dotnet run
```

### 2. Discover with Loom DevTools
In another terminal:
```bash
loom dev
```

You should see:
```
Loom dev — 1 process(es) — 3:00:00 PM
Showing only Loom-instrumented processes
Press Ctrl+C to stop.

  ✓ SampleMonitoredApp (pid 12345) — Loom.Telemetry active
```

### 3. Watch Live Metrics
```bash
loom watch 12345
```

### 4. Query Metrics (via Loom.Web.Api)
If you have Loom.Web.Api running:
```bash
# Get recent orders (note: LoomQL string literals use single quotes)
curl http://localhost:5209/api/query -H "Content-Type: application/json" -d "{\"query\": \"SELECT * FROM telemetry WHERE method = 'orders.processed' LIMIT 10\"}"

# Get payment success rate
curl http://localhost:5209/api/query -H "Content-Type: application/json" -d "{\"query\": \"SELECT COUNT(*) FROM telemetry WHERE method = 'payments.succeeded'\"}"
```

## Expected Metrics

You should see metrics being generated for:

**Counters:**
- `orders.processed` - Total orders processed
- `orders.succeeded` - Successful orders
- `orders.failed` - Failed orders
- `payments.attempted` - Payment attempts (tagged by method)
- `payments.succeeded` - Successful payments
- `payments.failed` - Payment failures
- `inventory.restocked` - Inventory restocking events
- `inventory.consumed` - Inventory consumption
- `inventory.out_of_stock` - Out-of-stock events

**Gauges:**
- `orders.pending` - Current pending order count
- `inventory.total_items` - Total inventory across all items
- `inventory.level.*` - Per-item inventory levels

**Histograms:**
- `order.processing.duration` - Order processing latency
- `order.total` - Order total amounts
- `payment.gateway.latency` - Payment gateway response time
- `payment.amount` - Payment amounts
- `refund.amount` - Refund amounts

**Automatic (from [LoomProfile]):**
- `OrderService.ProcessOrder` - Method execution time
- `OrderService.ValidateOrder` - Validation time
- `OrderService.CalculateTotal` - Calculation time
- `PaymentService.ProcessPayment` - Payment processing time
- `PaymentService.ValidateCard` - Card validation time

**Automatic (from [LoomTrack]):**
- `OrderService.OrdersPerMinute` - Current order processing rate
- `orders.avg_value` - Average order value (custom name)
- `PaymentService.SuccessRatePercent` - Payment success rate percentage
- `payments.active_count` - Active payment processing count (custom name)

## Testing Scenarios

### Test Alert System
Configure an alert for high failure rates:
```json
{
  "name": "High Order Failure Rate",
  "condition": "COUNT(orders.failed) > 10 OVER 1m"
}
```

### Test Exporters
Run the app and verify metrics appear in:
- Prometheus scrape endpoint
- Grafana Cloud (if configured)
- Elasticsearch (if configured)
- Console logs

### Test Sampling
Configure sampling rules to reduce volume:
```json
{
  "Loom": {
    "Sampling": {
      "Default": 0.5,
      "Rules": [
        { "Name": "orders.*", "Rate": 1.0 },
        { "Name": "inventory.*", "Rate": 0.1 }
      ]
    }
  }
}
```

## Architecture

```
SampleMonitoredApp
├── Program.cs                  # Host configuration
├── Services/
│   ├── OrderService.cs        # Order processing logic
│   └── PaymentService.cs      # Payment processing logic
└── Workers/
    ├── OrderProcessingWorker.cs    # Background order processor
    ├── PaymentProcessingWorker.cs  # Background payment processor
    └── InventoryWorker.cs          # Background inventory manager
```

## What This Validates

✅ Phase 5: Source Generator (generates wrappers for `[LoomProfile]`)
✅ Phase 6: Custom Metrics API (`LoomMetrics.Record*()`)
✅ Phase 7: Attribute-Based Instrumentation (`[LoomProfile]` attributes)
✅ Phase 8: Custom Collectors (via service instrumentation)
✅ Phase 9: Sampling (configurable via appsettings.json)
✅ Phase 10: Query Language (via Loom.Web.Api queries)
✅ Phase 11: Alerting (configurable thresholds)
✅ Phase 12: Exporters (Prometheus, Grafana, Elasticsearch, Console)
✅ Phase 13: Local Dev Mode (`loom dev` discovery)

This is the **end-to-end validation** of the entire Loom telemetry platform!
