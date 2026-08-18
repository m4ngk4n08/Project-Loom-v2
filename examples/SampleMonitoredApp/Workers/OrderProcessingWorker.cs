using Loom.Telemetry;
using SampleMonitoredApp.Services;

namespace SampleMonitoredApp.Workers;

public class OrderProcessingWorker : BackgroundService
{
    private readonly OrderService _orderService;
    private readonly ILogger<OrderProcessingWorker> _logger;
    private static readonly Random Random = new();
    private int _orderId = 1000;
    private int _ordersThisMinute = 0;
    private DateTime _lastMinuteReset = DateTime.UtcNow;

    public OrderProcessingWorker(OrderService orderService, ILogger<OrderProcessingWorker> logger)
    {
        _orderService = orderService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OrderProcessingWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Reset orders per minute counter
                if ((DateTime.UtcNow - _lastMinuteReset).TotalMinutes >= 1)
                {
                    _orderService.OrdersPerMinute = _ordersThisMinute;
                    _ordersThisMinute = 0;
                    _lastMinuteReset = DateTime.UtcNow;
                }

                var orderId = Interlocked.Increment(ref _orderId);

                // Process order
                await _orderService.ProcessOrderAsync(orderId);
                _ordersThisMinute++;

                // Calculate order total
                var itemCount = Random.Next(1, 20);
                var itemPrice = Random.Next(10, 100);
                var total = _orderService.CalculateOrderTotal(itemCount, itemPrice);

                _logger.LogInformation("Order {OrderId} processed: {ItemCount} items, ${Total:F2}",
                    orderId, itemCount, total);

                // Record throughput metric
                LoomMetrics.RecordGauge("worker.orders.active", 1);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Order processing failed");
            }

            // Variable processing rate (simulate realistic traffic)
            await Task.Delay(Random.Next(500, 2000), stoppingToken);
        }

        _logger.LogInformation("OrderProcessingWorker stopped");
    }
}
