using Loom.Telemetry;
using SampleMonitoredApp.Services;

namespace SampleMonitoredApp.Workers;

public class PaymentProcessingWorker : BackgroundService
{
    private readonly PaymentService _paymentService;
    private readonly ILogger<PaymentProcessingWorker> _logger;
    private static readonly Random Random = new();
    private static readonly string[] PaymentMethods = { "CreditCard", "PayPal", "ApplePay", "GooglePay" };

    public PaymentProcessingWorker(PaymentService paymentService, ILogger<PaymentProcessingWorker> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PaymentProcessingWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var amount = Random.Next(10, 500);
                var paymentMethod = PaymentMethods[Random.Next(PaymentMethods.Length)];

                // Validate card (if credit card)
                if (paymentMethod == "CreditCard")
                {
                    var cardNumber = GenerateTestCardNumber();
                    _paymentService.ValidateCardNumber(cardNumber);
                }

                // Process payment
                await _paymentService.ProcessPaymentAsync(amount, paymentMethod);

                _logger.LogInformation("Payment processed: ${Amount:F2} via {Method}",
                    amount, paymentMethod);

                // Simulate occasional refunds (2% of payments)
                if (Random.Next(100) < 2)
                {
                    await _paymentService.RefundPaymentAsync(amount);
                    _logger.LogInformation("Refund issued: ${Amount:F2}", amount);
                }

                // Record active payments metric
                LoomMetrics.RecordGauge("worker.payments.active", 1);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Payment processing failed");
            }

            // Variable processing rate
            await Task.Delay(Random.Next(800, 3000), stoppingToken);
        }

        _logger.LogInformation("PaymentProcessingWorker stopped");
    }

    private static string GenerateTestCardNumber()
    {
        // Generate a valid test card number (passes Luhn check)
        return Random.Next(100) < 80 ? "4532015112830366" : "1234567890123456"; // 80% valid, 20% invalid
    }
}
