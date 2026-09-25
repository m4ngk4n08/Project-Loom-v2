using System;
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

    /// <summary>
    /// Reads the payload fields via <paramref name="valueAt"/> and builds the record.
    /// Returns false when the logger name or message is missing or the level is out of
    /// range. The closure costs one small allocation per event.
    /// </summary>
    public static bool TryBuildLogRecord(
        LogMessageParser parser,
        string[]? payloadNames,
        Func<int, object?> valueAt,
        long timestampUtcTicks,
        out LogRecord record)
    {
        record = default;
        if (payloadNames == null) return false;

        string? category = null;
        string? formattedMessage = null;
        string? exceptionJson = null;
        string? argumentsJson = null;
        string? activityTraceId = null;
        string? activitySpanId = null;
        int level = -1;
        int eventId = 0;

        for (int i = 0; i < payloadNames.Length; i++)
        {
            switch (payloadNames[i])
            {
                case "LoggerName":
                    category = valueAt(i)?.ToString();
                    break;
                case "Level":
                    level = ToInt32(valueAt(i), -1);
                    break;
                case "EventId":
                    eventId = ToInt32(valueAt(i), 0);
                    break;
                case "FormattedMessage":
                    formattedMessage = valueAt(i)?.ToString();
                    break;
                case "ExceptionJson":
                    exceptionJson = valueAt(i)?.ToString();
                    break;
                case "ArgumentsJson":
                    argumentsJson = valueAt(i)?.ToString();
                    break;
                case "ActivityTraceId":
                    activityTraceId = valueAt(i)?.ToString();
                    break;
                case "ActivitySpanId":
                    activitySpanId = valueAt(i)?.ToString();
                    break;
            }
        }

        if (category == null || formattedMessage == null) return false;
        // Observed range is 0..5 (Trace..Critical), matching LoomLogLevel's ordering.
        if (level < 0 || level > 5) return false;

        record = BuildLogRecord(
            parser, formattedMessage, category, level,
            timestampUtcTicks, eventId,
            exceptionJson, argumentsJson, activityTraceId, activitySpanId);
        return true;
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
