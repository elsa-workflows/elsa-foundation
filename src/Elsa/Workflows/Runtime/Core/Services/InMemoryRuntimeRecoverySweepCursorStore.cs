using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Process-local bounded cursor retention for the recurring resumption pump.
/// </summary>
/// <remarks>
/// The protected provider token remains the source of truth. This store only keeps the pump from restarting its
/// current bounded scan on every fresh DI operation scope; a process restart safely begins a new scan cycle.
/// Durable callers that expose paging retain the token outside this helper.
/// </remarks>
public sealed class InMemoryRuntimeRecoverySweepCursorStore : IRuntimeRecoverySweepCursorStore
{
    private const int MaximumEntries = 1024;
    private readonly object _gate = new();
    private readonly Dictionary<string, CursorEntry> _cursors = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _insertionOrder = new();

    public RuntimeRecoverySweepCursor? Get(string scope, string scanner)
    {
        var key = Key(scope, scanner);
        lock (_gate)
            return _cursors.TryGetValue(key, out var entry) ? entry.Cursor : null;
    }

    public void Set(string scope, string scanner, RuntimeRecoverySweepCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        var key = Key(scope, scanner);
        lock (_gate)
        {
            if (_cursors.TryGetValue(key, out var existing))
            {
                // Updating an entry does not create another eviction node. This keeps repeated sweep cycles bounded
                // and means an update can never be evicted by a stale generation of its own key.
                _cursors[key] = existing with { Cursor = cursor };
                return;
            }

            while (_cursors.Count >= MaximumEntries && _insertionOrder.First is { } oldestNode)
            {
                _insertionOrder.RemoveFirst();
                _cursors.Remove(oldestNode.Value);
            }

            _cursors[key] = new CursorEntry(cursor, _insertionOrder.AddLast(key));
        }
    }

    public void Clear(string scope, string scanner)
    {
        var key = Key(scope, scanner);
        lock (_gate)
        {
            if (!_cursors.Remove(key, out var entry))
                return;

            _insertionOrder.Remove(entry.Node);
        }
    }

    private static string Key(string scope, string scanner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(scanner);
        return $"{scope.Length}:{scope}|{scanner.Length}:{scanner}";
    }

    private sealed record CursorEntry(RuntimeRecoverySweepCursor Cursor, LinkedListNode<string> Node);
}
