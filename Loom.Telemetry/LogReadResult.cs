namespace Loom.Telemetry;

/// <summary>
/// Result of a sequence-cursor tail read (LogBuffer.ReadAfter / ILogStore.ReadAfter).
/// </summary>
public readonly record struct LogReadResult(
    /// <summary>Records with sequence greater than the requested cursor, oldest first (natural replay order).</summary>
    LogRecord[] Records,
    /// <summary>Sequence to pass as afterSequence on the next poll.</summary>
    long NextSequence,
    /// <summary>How many records were overwritten before the caller could read them, because its cursor fell out of the buffer's live range.</summary>
    int DroppedCount);
