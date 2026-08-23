using System.Threading.Channels;
using Loom.Telemetry;

namespace Loom.Storage;

/// <summary>
/// Centralized log storage. All writes go here, all reads come from here.
/// Parallel sibling to IMetricStore - not related to it. Logs are a single
/// chronological stream (one LogBuffer), where category is a read-time filter,
/// unlike metrics which are bucketed into one buffer per metric name.
/// </summary>
public interface ILogStore
{
    void Write(in LogRecord record);

    LogRecord[] ReadRecent(int count);

    LogRecord[] ReadRecent(string category, int count);

    LogRecord[] ReadSince(long timestampUtcTicks);

    LogReadResult ReadAfter(long afterSequence);

    long CurrentSequence { get; }

    IReadOnlyCollection<string> GetCategories();

    ChannelReader<LogRecord> Subscribe();

    void Unsubscribe(ChannelReader<LogRecord> reader);
}
