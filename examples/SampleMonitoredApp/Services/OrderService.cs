using Loom.Telemetry;

namespace SampleMonitoredApp.Services;

public partial class OrderService
{
    private static readonly Random Random = new();
    private int _orderCounter = 0;

    /// <summary>
    /// Tracks the current order processing rate (orders/minute).
    /// [LoomTrack] automatically emits a gauge metric whenever this property changes.
    /// </summary>
    [LoomTrack]
    public int OrdersPerMinute { get; set; }

    /// <summary>
    /// Tracks the average order value.
    /// [LoomTrack] automatically records this as a gauge metric.
    /// </summary>
    [LoomTrack(Name = "orders.avg_value")]
    public decimal AverageOrderValue { get; private set; }

    [LoomProfile(Name = "OrderService.ProcessOrder")]
    public async Task<bool> ProcessOrderAsync(int orderId)
    {
        // Simulate order validation (CPU-intensive)
        ValidateOrder(orderId);

        // Simulate database lookup (I/O)
        await Task.Delay(Random.Next(10, 50));

        // Record custom metrics
        LoomMetrics.RecordCounter("orders.processed", 1);
        LoomMetrics.RecordHistogram("order.processing.duration", Random.Next(50, 200));

        // Simulate occasional failures (10% failure rate)
        if (Random.Next(100) < 10)
        {
            LoomMetrics.RecordCounter("orders.failed", 1);
            throw new InvalidOperationException($"Order {orderId} validation failed");
        }

        LoomMetrics.RecordCounter("orders.succeeded", 1);
        return true;
    }

    [LoomProfile]
    public void ValidateOrder(int orderId)
    {
        // Simulate CPU-intensive validation
        var result = 0;
        for (int i = 0; i < 10000; i++)
        {
            result += i * orderId;
        }

        // Record validation metric
        LoomMetrics.RecordGauge("orders.pending", Interlocked.Increment(ref _orderCounter));
    }

    [LoomProfile(Name = "OrderService.CalculateTotal")]
    public decimal CalculateOrderTotal(int itemCount, decimal itemPrice)
    {
        // Simulate complex pricing calculation
        var subtotal = itemCount * itemPrice;
        var tax = subtotal * 0.08m;
        var shipping = itemCount > 10 ? 0 : 9.99m;

        var total = subtotal + tax + shipping;

        // Update tracked properties (will auto-emit metrics)
        AverageOrderValue = total;

        // Record pricing metrics
        LoomMetrics.RecordHistogram("order.total", (double)total);
        LoomMetrics.RecordHistogram("order.item_count", itemCount);

        return total;
    }
}
