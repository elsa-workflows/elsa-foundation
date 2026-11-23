using Kernel.EntityFramework;
using Kernel.Foundation.InMemory;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

/// <summary>A fixed Embedded activation for one public Native root in the PostgreSQL Core Preview.</summary>
internal sealed class PostgresNativeRootOwner
{
    private readonly DurableHost _host;
    internal int Partition { get; }

    private PostgresNativeRootOwner(DurableHost host, int partition) => (_host, Partition) = (host, partition);

    internal static async Task<PostgresNativeRootOwner> PrepareAsync(EfStore store, string node, string tenant, string reference,
        WorkflowRegistry registry, CancellationToken cancellationToken, Action<InstanceRef, IReadOnlyList<Message>, long>? onTurn = null)
    {
        var root = await store.BindWorkerRootAsync(tenant, reference, cancellationToken);
        var adapter = new ActorAdapter(new Headers(root), new PublicActor([new NativeExecutionModel(registry)]));
        var worker = new Worker(node, root.Partition, store, adapter, new WorkerOptions { PollLimit = 1, OnTurn = onTurn });
        var host = new DurableHost([worker]);
        var prepared = new PostgresNativeRootOwner(host, root.Partition);
        await prepared.HeartbeatAsync(cancellationToken);
        return prepared;
    }

    internal ValueTask<Cluster?> HeartbeatAsync(CancellationToken stop) => _host.HeartbeatAsync(Defaults.Lease, stop);
    internal ValueTask<IScopeActivation?> TryActivateAsync(CancellationToken stop) => _host.ActivateAsync(Partition, stop);

    internal static async Task<IScopeActivation> ActivateAsync(EfStore store, string node, string tenant, string reference,
        WorkflowRegistry registry, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(store, node, tenant, reference, registry, cancellationToken);
        return await prepared.TryActivateAsync(cancellationToken) ?? throw new InvalidOperationException("The partition is already owned.");
    }

    private sealed class Headers(EfStore.WorkerRoot root) : IRootDispatchHeaders
    {
        public (string Tenant, string Reference) Creator(Message creator) =>
            creator.Target.Id == root.Actor && root.Owns(creator.InboxKey)
                ? (root.Tenant, root.Reference) : throw new InvalidOperationException("Unexpected root creator.");

        public FoundationDispatchHeader Checkpoint(InstanceRow row) =>
            throw new InvalidOperationException("PostgreSQL must load the root's public header with its checkpoint.");
    }
}
