using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>
/// Layer 2's side of the seam for one process: its workers, one per partition it may own. Activating a scope takes the
/// partition's fence (a take that bumps the epoch, from empty memory), or declines when the store refuses. It does not
/// trust the runtime: if a runtime activates a scope on two nodes at once, the fence lets one of them write.
/// </summary>
public sealed class DurableHost : IScopeHost
{
    private readonly Dictionary<int, Worker> _workers;

    public DurableHost(IEnumerable<Worker> workers) => _workers = workers.ToDictionary(w => w.Partition);

    public IReadOnlyCollection<Worker> Workers => _workers.Values;

    public Worker this[int scope] => _workers[scope];

    public async ValueTask<IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default)
    {
        if (!_workers.TryGetValue(scope, out var worker)) return null;
        await worker.TakeAsync(cancellationToken);
        return worker.Owns ? worker : null;
    }
}
