using Loom.Telemetry;

namespace SampleMonitoredApp.Services;

public partial class PaymentService
{
    private static readonly Random Random = new();

    /// <summary>
    /// Tracks the current success rate percentage.
    /// [LoomTrack] automatically emits a gauge metric on changes.
    /// </summary>
    [LoomTrack]
    public double SuccessRatePercent { get; private set; } = 95.0;

    /// <summary>
    /// Tracks active payment processing count.
    /// </summary>
    [LoomTrack(Name = "payments.active_count")]
    public int ActivePayments { get; private set; }

    [LoomProfile(Name = "PaymentService.ProcessPayment")]
    public async Task<bool> ProcessPaymentAsync(decimal amount, string paymentMethod)
    {
        // Track active payment processing
        ActivePayments++;

        try
        {
            // Record payment attempt
            LoomMetrics.RecordCounter("payments.attempted", 1,
                new MetricTag("method", paymentMethod));

            // Simulate payment gateway call (variable latency)
            var latency = Random.Next(50, 500);
            await Task.Delay(latency);

            LoomMetrics.RecordHistogram("payment.gateway.latency", latency,
                new MetricTag("method", paymentMethod));

            // Simulate payment failures (5% failure rate)
            if (Random.Next(100) < 5)
            {
                // Update success rate (tracked property)
                SuccessRatePercent = Math.Max(0, SuccessRatePercent - 0.1);

                LoomMetrics.RecordCounter("payments.failed", 1,
                    new MetricTag("method", paymentMethod));
                throw new InvalidOperationException($"Payment of ${amount:F2} via {paymentMethod} declined");
            }

            // Update success rate (tracked property)
            SuccessRatePercent = Math.Min(100, SuccessRatePercent + 0.05);

            // Record successful payment
            LoomMetrics.RecordCounter("payments.succeeded", 1,
                new MetricTag("method", paymentMethod));
            LoomMetrics.RecordHistogram("payment.amount", (double)amount);

            return true;
        }
        finally
        {
            // Decrement active payments
            ActivePayments--;
        }
    }

    [LoomProfile]
    public async Task<bool> RefundPaymentAsync(decimal amount)
    {
        // Simulate refund processing
        await Task.Delay(Random.Next(100, 300));

        LoomMetrics.RecordCounter("payments.refunded", 1);
        LoomMetrics.RecordHistogram("refund.amount", (double)amount);

        return true;
    }

    [LoomProfile(Name = "PaymentService.ValidateCard")]
    public bool ValidateCardNumber(string cardNumber)
    {
        // Simulate Luhn algorithm validation (CPU work)
        int sum = 0;
        bool alternate = false;

        for (int i = cardNumber.Length - 1; i >= 0; i--)
        {
            if (!char.IsDigit(cardNumber[i]))
                continue;

            int digit = cardNumber[i] - '0';

            if (alternate)
            {
                digit *= 2;
                if (digit > 9)
                    digit -= 9;
            }

            sum += digit;
            alternate = !alternate;
        }

        var isValid = sum % 10 == 0;
        LoomMetrics.RecordCounter(isValid ? "card.validation.passed" : "card.validation.failed", 1);

        return isValid;
    }
}
