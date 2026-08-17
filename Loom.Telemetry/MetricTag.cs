using System;

namespace Loom.Telemetry;

/// <summary>
/// A key-value tag/dimension for a metric. Immutable value type.
/// </summary>
public readonly struct MetricTag : IEquatable<MetricTag>
{
    public string Key { get; }
    public string Value { get; }

    public MetricTag(string key, string value)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public bool Equals(MetricTag other) => Key == other.Key && Value == other.Value;
    public override bool Equals(object? obj) => obj is MetricTag tag && Equals(tag);
    public override int GetHashCode() => HashCode.Combine(Key, Value);
    public override string ToString() => $"{Key}={Value}";

    public static bool operator ==(MetricTag left, MetricTag right) => left.Equals(right);
    public static bool operator !=(MetricTag left, MetricTag right) => !left.Equals(right);
}
