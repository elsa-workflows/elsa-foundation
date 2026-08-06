using System.Collections.ObjectModel;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>A provider-neutral, versioned snapshot of opaque workflow execution context.</summary>
public sealed class RuntimeExecutionContextSnapshot : IEquatable<RuntimeExecutionContextSnapshot>
{
    public const int CurrentVersion = 1;
    public const int MaximumEntryCount = 32;
    public const int MaximumKeyLength = 128;
    public const int MaximumValueLength = 4096;

    public RuntimeExecutionContextSnapshot(int version, IReadOnlyDictionary<string, string>? entries = null)
    {
        if (version != CurrentVersion)
            throw new ArgumentOutOfRangeException(nameof(version), version, $"Only runtime execution-context version {CurrentVersion} is supported.");

        entries ??= new Dictionary<string, string>();
        if (entries.Count > MaximumEntryCount)
            throw new ArgumentOutOfRangeException(nameof(entries), entries.Count, $"Execution context cannot contain more than {MaximumEntryCount} entries.");

        var canonical = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);
            if (key.Length > MaximumKeyLength)
                throw new ArgumentOutOfRangeException(nameof(entries), key.Length, $"Execution-context keys cannot exceed {MaximumKeyLength} characters.");
            if (value.Length > MaximumValueLength)
                throw new ArgumentOutOfRangeException(nameof(entries), value.Length, $"Execution-context values cannot exceed {MaximumValueLength} characters.");
            canonical.Add(key, value);
        }

        Version = version;
        Entries = new ReadOnlyDictionary<string, string>(canonical);
    }

    public int Version { get; }
    public IReadOnlyDictionary<string, string> Entries { get; }
    public bool IsEmpty => Entries.Count == 0;

    public static RuntimeExecutionContextSnapshot Empty { get; } = new(CurrentVersion);

    public bool Equals(RuntimeExecutionContextSnapshot? other) =>
        other is not null &&
        Version == other.Version &&
        Entries.Count == other.Entries.Count &&
        Entries.All(entry => other.Entries.TryGetValue(entry.Key, out var value) && StringComparer.Ordinal.Equals(entry.Value, value));

    public override bool Equals(object? obj) => obj is RuntimeExecutionContextSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Version);
        foreach (var (key, value) in Entries)
        {
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(value, StringComparer.Ordinal);
        }
        return hash.ToHashCode();
    }
}
