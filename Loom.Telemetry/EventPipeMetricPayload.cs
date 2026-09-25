using System;
using System.Globalization;

namespace Loom.Telemetry;

/// <summary>
/// Decides what a System.Diagnostics.Metrics *ValuePublished EventPipe event means and
/// builds the MetricRecord for it. Takes plain values (event name, payload names, a
/// value-reader) rather than a TraceEvent - see EventPipeLogPayload for why.
/// </summary>
public static class EventPipeMetricPayload
{
    /// <summary>
    /// Returns false when the event is not a value-publish event, carries no payload
    /// names, or has no instrument name. <paramref name="valueAt"/> reads the payload
    /// value at an index; it is a closure, so this costs one small allocation per event
    /// on a path that fires about once per second per instrument.
    /// </summary>
    public static bool TryBuildRecord(
        string eventName,
        string[]? payloadNames,
        Func<int, object?> valueAt,
        long timestampUtcTicks,
        out MetricRecord record)
    {
        record = default;

        // Only ingest actual value-publish events; BeginInstrumentReporting is metadata only
        if (!eventName.Contains("ValuePublished"))
            return false;

        if (payloadNames == null) return false;

        // The value field is chosen BY EVENT TYPE, not by a flat set of alternative
        // names: a single incoming "value" field means something different per event
        // shape (CounterRateValuePublished's "rate" is a per-interval delta;
        // GaugeValuePublished's "lastValue" is a point-in-time sample;
        // HistogramValuePublished's "sum" is a summary total), and a name that
        // happened to match more than one of those shapes would let whichever case
        // came later in a flat switch silently win - the exact bug this rewrite
        // replaces (see PROMPT-counter-rate-ingest.md). "rate"/"value"/"lastValue"/
        // "sum" are the only four fields that carry a number on these events -
        // verified against a live session - so nothing else could be captured here.
        //
        // Trade-off: summing per-interval "rate" deltas means a missed or dropped
        // publish undercounts the total permanently, whereas re-reading the
        // cumulative "value" would self-correct on the next publish. That is still
        // the right choice here because InMemoryMetricStore's accumulator contract
        // is increments, and it is monotonic - accepting occasional undercount on a
        // dropped event beats guaranteed, unbounded overcount on every event.
        //
        // UpDownCounterRateValuePublished (not "UpDownCounterValuePublished" - that
        // name was never emitted by the runtime and the mapping that used it never
        // matched) carries the same rate/value shape as CounterRateValuePublished -
        // measured against a live session with the fixture's up-down counter. It
        // stays a Gauge (an up-down counter isn't monotonic, so this store's
        // Counter-only accumulator is the wrong home for it) and reads "value",
        // NOT "rate": a gauge is exported as its newest sample, and once a level
        // stops moving it publishes rate=0 every interval while value holds
        // (measured: rate=0 value=10 against a pool held at 10). Reading "rate"
        // would report a steady pool of 10 connections as 0.
        //
        // Two measured limitations of the runtime, NOT of this code, that no
        // choice of field can fix. Both were observed directly:
        //   - "value" is cumulative WITHIN THE SESSION, not since the instrument
        //     was created. A session that attaches after the level settles sees
        //     the change since it attached, not the absolute level.
        //   - an up-down counter with no activity during a session is not
        //     reported by that session at all: attaching 15s after the fixture
        //     reached its plateau ingested zero records for it.
        // Loom attaches for the life of the command (loom) or of the dashboard
        // (loom-dashboard), so the common case tracks the true level; a late attach
        // to an idle instrument cannot.
        var (metricType, valueField) = eventName switch
        {
            "CounterRateValuePublished" => (MetricType.Counter, "rate"),
            "GaugeValuePublished" => (MetricType.Gauge, "lastValue"),
            "HistogramValuePublished" => (MetricType.Histogram, "sum"),
            "UpDownCounterRateValuePublished" => (MetricType.Gauge, "value"),
            _ => (MetricType.Gauge, (string?)null)
        };

        string? metricName = null;
        double value = 0;
        string? tagPayload = null;

        for (int i = 0; i < payloadNames.Length; i++)
        {
            var payloadName = payloadNames[i];
            if (payloadName is "Name" or "instrumentName")
            {
                metricName = valueAt(i)?.ToString();
            }
            else if (payloadName == "tags")
            {
                tagPayload = valueAt(i)?.ToString();
            }
            else if (valueField != null && payloadName == valueField)
            {
                if (double.TryParse(valueAt(i)?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    value = v;
            }
        }

        if (metricName == null) return false;

        record = new MetricRecord(metricName, metricType, value, timestampUtcTicks, EventPipeTagPayload.Parse(tagPayload));
        return true;
    }
}
