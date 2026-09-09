using System.Linq;
using System.Text.Json;
using Loom.Storage;
using Loom.Telemetry;
using Xunit;

namespace Loom.Telemetry.Tests.Storage;

// Payload strings below are copied verbatim from a live EventPipe session against
// Loom.TestFixtureApp (System.Runtime provider, EventCounterIntervalSec=1) - see
// BACKLOG.md 6.11's Phase 0 measurement. Per CLAUDE.md, "a negative probe with invalid
// input reads exactly like a clean bill of health": none of these are invented shapes.
public sealed class SystemRuntimeCountersTests
{
    // Real .NET 10 shape: one object under "Payload", not an array (see the class's own
    // doc comment at SystemRuntimeCounters.cs:26-28).
    private const string MeanCounterPayload =
        """{ "Payload":{ "Name":"working-set", "DisplayName":"Working Set", "Mean":34.349056, "StandardDeviation":0, "Count":1, "Min":34.349056, "Max":34.349056, "IntervalSec":1.0026728, "Series":"Interval=1000", "CounterType":"Mean", "Metadata":"", "DisplayUnits":"MB" } }""";

    private const string IncrementCounterPayload =
        """{ "Payload":{ "Name":"alloc-rate", "DisplayName":"Allocation Rate", "DisplayRateTimeScale":"00:00:01", "Increment":16144, "IntervalSec":1.0026728, "Metadata":"", "Series":"Interval=1000", "CounterType":"Sum", "DisplayUnits":"B" } }""";

    [Fact]
    public void Parse_MeanCounter_YieldsGaugeWithMeanValue()
    {
        var records = SystemRuntimeCounters.Parse(MeanCounterPayload).ToArray();

        var record = Assert.Single(records);
        Assert.Equal("working-set", record.Name);
        Assert.Equal(MetricType.Gauge, record.Type);
        Assert.Equal(34.349056, record.Value);
    }

    [Fact]
    public void Parse_IncrementCounter_YieldsCounterWithIncrementValue()
    {
        // This is Bug A's fix: before it, TryReadCounter required "Mean" and rejected
        // every Increment-shaped payload before Classify was ever consulted.
        var records = SystemRuntimeCounters.Parse(IncrementCounterPayload).ToArray();

        var record = Assert.Single(records);
        Assert.Equal("alloc-rate", record.Name);
        Assert.Equal(MetricType.Counter, record.Type);
        Assert.Equal(16144, record.Value);
    }

    [Fact]
    public void Parse_OlderRuntimeArrayShape_YieldsBothCounters()
    {
        // The "older runtimes" shape documented at SystemRuntimeCounters.cs:26-28: a
        // top-level JSON array of counter objects rather than one object under
        // "Payload". The two counter objects themselves are the same real payloads used
        // above, just re-wrapped in an array instead of nested under "Payload".
        var json =
            """
            [
              { "Name":"working-set", "Mean":34.349056, "CounterType":"Mean" },
              { "Name":"alloc-rate", "Increment":16144, "CounterType":"Sum" }
            ]
            """;

        var records = SystemRuntimeCounters.Parse(json).ToArray();

        Assert.Equal(2, records.Length);
        Assert.Contains(records, r => r.Name == "working-set" && r.Type == MetricType.Gauge && r.Value == 34.349056);
        Assert.Contains(records, r => r.Name == "alloc-rate" && r.Type == MetricType.Counter && r.Value == 16144);
    }

    [Fact]
    public void Parse_UnrecognisedName_DefaultsToGauge()
    {
        // Classify's default branch for a name it doesn't recognize. The envelope and
        // field shape are the real, observed "Mean" shape above; only the Name is
        // swapped, since no live counter with an unrecognised name exists to capture.
        var json =
            """{ "Payload":{ "Name":"totally-unrecognised-counter", "Mean":7, "CounterType":"Mean" } }""";

        var records = SystemRuntimeCounters.Parse(json).ToArray();

        var record = Assert.Single(records);
        Assert.Equal("totally-unrecognised-counter", record.Name);
        Assert.Equal(MetricType.Gauge, record.Type);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("""{ "Payload": { "NoNameHere": true, "Mean": 1 } }""")]
    [InlineData("""{ "Payload": { "Name": "cpu-usage" } }""")]
    [InlineData("""{ "Payload": "not an object" }""")]
    [InlineData("""[ { "Name": "cpu-usage" } ]""")]
    [InlineData("[ 1, 2, 3 ]")]
    public void Parse_WellFormedButUnrecognisedPayload_YieldsNoRecords(string json)
    {
        var records = SystemRuntimeCounters.Parse(json).ToArray();

        Assert.Empty(records);
    }

    [Fact]
    public void Parse_NonJsonText_Throws_SoTheCallerCanLogIt()
    {
        // Deliberately NOT swallowed. Both call sites wrap Parse in a try/catch that
        // logs the failure (EventPipeCollector.IngestEventCounters, EventPipeBridge),
        // and neither lets it reach the session. Returning empty here instead would
        // leave those handlers as dead code and turn a logged failure into zero
        // records with no explanation - indistinguishable from a target that simply
        // published nothing.
        Assert.ThrowsAny<JsonException>(() => SystemRuntimeCounters.Parse("not json at all").ToArray());
    }
}
