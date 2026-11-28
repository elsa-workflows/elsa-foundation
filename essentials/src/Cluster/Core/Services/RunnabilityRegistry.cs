using System.Collections.Concurrent;
using Elsa.Cluster.Core.Models;

namespace Elsa.Cluster.Core.Services;

/// <summary>
/// Stores runnability snapshots by shell generation, so stopping one generation does not remove its replacement's entry.
/// </summary>
public sealed class RunnabilityRegistry
{
    private readonly ConcurrentDictionary<string, RunnabilityEntry> _entries = new(StringComparer.Ordinal);

    public IReadOnlyList<RunnabilityEntry> Entries =>
        _entries.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToArray();

    /// <summary>Records the generation's snapshot and returns whether it changed.</summary>
    public bool Record(string shellInstance, RunnabilityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var previous = _entries.TryGetValue(shellInstance, out var existing) ? existing : null;
        _entries[shellInstance] = entry;
        return !entry.Equals(previous);
    }

    /// <summary>Removes the generation's snapshot and returns whether it was present.</summary>
    public bool Remove(string shellInstance) => _entries.TryRemove(shellInstance, out _);
}
