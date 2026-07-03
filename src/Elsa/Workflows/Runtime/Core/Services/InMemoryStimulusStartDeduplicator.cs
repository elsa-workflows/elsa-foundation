using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Default in-process <see cref="IStimulusStartDeduplicator"/>. It remembers admitted idempotency keys in a
/// process-local set, so a duplicate stimulus delivery within the same process does not double-start a
/// workflow. It is best-effort by design (W7, Condition A): the set is not durable and not shared across
/// processes, and it grows for the process lifetime. Hosts needing stronger guarantees replace this contract.
/// </summary>
public sealed class InMemoryStimulusStartDeduplicator : IStimulusStartDeduplicator
{
    private readonly object _syncRoot = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public bool TryBeginStart(string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        lock (_syncRoot)
        {
            return _seen.Add(idempotencyKey);
        }
    }
}
