using Loom.Telemetry;

namespace SampleMonitoredApp.Workers;

public class InventoryWorker : BackgroundService
{
    private readonly ILogger<InventoryWorker> _logger;
    private static readonly Random Random = new();
    private readonly Dictionary<string, int> _inventory = new();

    public InventoryWorker(ILogger<InventoryWorker> logger)
    {
        _logger = logger;

        // Initialize inventory
        _inventory["Widget-A"] = 100;
        _inventory["Widget-B"] = 200;
        _inventory["Widget-C"] = 150;
        _inventory["Widget-D"] = 50;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InventoryWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Simulate inventory operations
                var operation = Random.Next(3);

                switch (operation)
                {
                    case 0: // Restock
                        await RestockInventoryAsync();
                        break;
                    case 1: // Consume
                        await ConsumeInventoryAsync();
                        break;
                    case 2: // Check levels
                        CheckInventoryLevels();
                        break;
                }

                // Report overall inventory metrics
                var totalItems = _inventory.Values.Sum();
                LoomMetrics.RecordGauge("inventory.total_items", totalItems);

                // Simulate memory allocations (testing GC metrics)
                var largeArray = new byte[Random.Next(1024, 10240)];
                Array.Fill(largeArray, (byte)Random.Next(256));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Inventory operation failed");
            }

            await Task.Delay(Random.Next(1000, 3000), stoppingToken);
        }

        _logger.LogInformation("InventoryWorker stopped");
    }

    private async Task RestockInventoryAsync()
    {
        var item = _inventory.Keys.ElementAt(Random.Next(_inventory.Count));
        var quantity = Random.Next(10, 50);

        _inventory[item] += quantity;

        LoomMetrics.RecordCounter("inventory.restocked", quantity,
            new MetricTag("item", item));
        LoomMetrics.RecordGauge($"inventory.level.{item}", _inventory[item]);

        _logger.LogInformation("Restocked {Quantity} of {Item} (total: {Total})",
            quantity, item, _inventory[item]);

        await Task.Delay(Random.Next(50, 150));
    }

    private async Task ConsumeInventoryAsync()
    {
        var item = _inventory.Keys.ElementAt(Random.Next(_inventory.Count));
        var quantity = Random.Next(1, 10);

        if (_inventory[item] >= quantity)
        {
            _inventory[item] -= quantity;

            LoomMetrics.RecordCounter("inventory.consumed", quantity,
                new MetricTag("item", item));
            LoomMetrics.RecordGauge($"inventory.level.{item}", _inventory[item]);

            _logger.LogInformation("Consumed {Quantity} of {Item} (remaining: {Remaining})",
                quantity, item, _inventory[item]);
        }
        else
        {
            LoomMetrics.RecordCounter("inventory.out_of_stock", 1,
                new MetricTag("item", item));

            _logger.LogWarning("Out of stock: {Item}", item);
        }

        await Task.Delay(Random.Next(20, 100));
    }

    private void CheckInventoryLevels()
    {
        foreach (var kvp in _inventory)
        {
            LoomMetrics.RecordGauge($"inventory.level.{kvp.Key}", kvp.Value);

            // Alert on low inventory
            if (kvp.Value < 20)
            {
                LoomMetrics.RecordCounter("inventory.low_stock", 1,
                    new MetricTag("item", kvp.Key));
            }
        }
    }
}
