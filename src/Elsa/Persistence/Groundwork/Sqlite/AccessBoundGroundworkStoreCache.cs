using System.Collections.Concurrent;
using Groundwork.Documents.Scoping;

namespace Elsa.Persistence.Groundwork.Sqlite;

/// <summary>
/// Retains a bounded set of immutable access-bound store adapters. Eviction only drops the cache reference;
/// operations already using an entry remain valid because the underlying reusable store owns no open connection.
/// </summary>
public sealed class AccessBoundGroundworkStoreCache<T>(
    int capacity,
    Func<DocumentStoreAccess, T> create)
{
    private readonly ConcurrentDictionary<
        DocumentStoreAccess,
        Lazy<T>> entries = new();
    private readonly ConcurrentQueue<DocumentStoreAccess> insertionOrder = new();

    public T GetOrCreate(DocumentStoreAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (capacity <= 0)
            return create(access);

        var candidate = new Lazy<T>(
            () => create(access),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var entry = entries.GetOrAdd(access, candidate);
        if (ReferenceEquals(entry, candidate))
        {
            insertionOrder.Enqueue(access);
            Trim();
        }

        return entry.Value;
    }

    public int Count => entries.Count;

    private void Trim()
    {
        while (entries.Count > capacity && insertionOrder.TryDequeue(out var oldest))
            entries.TryRemove(oldest, out _);
    }
}
