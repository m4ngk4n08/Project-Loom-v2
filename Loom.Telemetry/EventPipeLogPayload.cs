using System.Text.Json;

namespace Loom.Telemetry;

/// <summary>
/// Turns the plain values of a Microsoft-Extensions-Logging EventPipe payload into a
/// LogRecord. Deliberately takes strings and ints rather than a TraceEvent: TraceEvent
/// cannot be constructed in a unit test, so every decision worth testing has to live
/// behind a plain-value seam. Callers read the payload; this decides what it means.
/// </summary>
public static class EventPipeLogPayload
{
    // PayloadValue boxes Level and EventId as Int32 - verified against a live
    // Microsoft-Extensions-Logging session. Unbox directly; ToString() +
    // int.TryParse allocates a throwaway string per field on every log event.
    // The string branch keeps the read working if a future runtime widens the
    // payload type rather than silently dropping the record.
    public static int ToInt32(object? value, int fallback) => value switch
    {
        int i => i,
        null => fallback,
        _ => int.TryParse(value.ToString(), out var parsed) ? parsed : fallback,
    };

    public static LogRecord BuildLogRecord(
        LogMessageParser parser,
        string formattedMessage,
        string category,
        int level,
        long timestampUtcTicks,
        int eventId,
        string? exceptionJson,
        string? argumentsJson,
        string? activityTraceId,
        string? activitySpanId)
    {
        var (exceptionType, exceptionMessage) = ParseExceptionJson(exceptionJson);
        var (template, args) = parser.ExtractTemplateAndArgs(argumentsJson);

        ulong traceHi = 0, traceLo = 0, spanId = 0;
        if (activityTraceId != null)
            W3CTraceId.TryParseTraceId(activityTraceId, out traceHi, out traceLo);
        if (activitySpanId != null)
            W3CTraceId.TryParseSpanId(activitySpanId, out spanId);

        return new LogRecord(
            // Message keeps the fully rendered text even though Template and
            // ArgumentsJson are stored alongside it. Re-rendering the template per row
            // on every page render costs more, forever, than the bytes saved once in a
            // bounded ring buffer.
            formattedMessage,
            category,
            (LoomLogLevel)level,
            timestampUtcTicks,
            eventId,
            exceptionType,
            exceptionMessage,
            template,
            args,
            traceHi,
            traceLo,
            spanId);
    }

    public static (string? Type, string? Message) ParseExceptionJson(string? exceptionJson)
    {
        if (string.IsNullOrEmpty(exceptionJson) || exceptionJson == "{}")
            return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(exceptionJson);
            var root = doc.RootElement;
            var type = root.TryGetProperty("TypeName", out var typeProp) ? typeProp.GetString() : null;
            var message = root.TryGetProperty("Message", out var messageProp) ? messageProp.GetString() : null;
            return (type, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
