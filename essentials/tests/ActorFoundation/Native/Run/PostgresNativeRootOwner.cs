using Kernel.EntityFramework;
using Kernel.Foundation.InMemory;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

/// <summary>A fixed Embedded activation for public Native roots on one partition in the PostgreSQL Core Preview.</summary>
internal sealed class PostgresNativeRootOwner
{
    private readonly DurableHost _host;
    internal int Partition { get; }

    private PostgresNativeRootOwner(DurableHost host, int partition) => (_host, Partition) = (host, partition);

    internal static Task<PostgresNativeRootOwner> PrepareAsync(EfStore store, string node, string tenant, string reference,
        WorkflowRegistry registry, CancellationToken cancellationToken, Action<InstanceRef, IReadOnlyList<Message>, long>? onTurn = null)
        => PrepareAsync(store, node, [(tenant, reference)], registry, cancellationToken, onTurn);

    internal static async Task<PostgresNativeRootOwner> PrepareAsync(EfStore store, string node,
        IReadOnlyList<(string Tenant, string Reference)> requested, WorkflowRegistry registry, CancellationToken cancellationToken,
        Action<InstanceRef, IReadOnlyList<Message>, long>? onTurn = null)
    {
        var root = await store.BindWorkerRootsAsync(requested, cancellationToken);
        return await PrepareAsync(store, node, root, registry, requested.Count, cancellationToken, onTurn, 1);
    }

    internal static DurableHost PrepareNode(EfStore store, string node, int partitions, WorkflowRegistry registry, WorkerOptions options)
    {
        var roots = store.BindWorkerNode();
        return DurableHost.ForNode(node, store, Adapter(store, roots, registry), partitions, options);
    }

    internal static ActorAdapter Adapter(EfStore store, EfStore.WorkerRoots roots, WorkflowRegistry registry) =>
        new(new EfStore.RootHeaders(roots), new PublicActor([new NativeExecutionModel(registry)]), store.RootSelfIssuer, store.RootJobHandle,
            store.RootTimedJobIssuanceEnabled, store.RootTimedJobSettlementEnabled ? store.ReadRootTimedJobPublicationTimeAsync : null);

    internal static async Task<PostgresNativeRootOwner> PreparePartitionAsync(EfStore store, string node, int partition,
        WorkflowRegistry registry, int pollLimit, CancellationToken cancellationToken, Action<InstanceRef, IReadOnlyList<Message>, long>? onTurn = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pollLimit);
        var (scope, count) = await store.BindWorkerPartitionAsync(partition, cancellationToken);
        return await PrepareAsync(store, node, scope, registry, pollLimit, cancellationToken, onTurn, count);
    }

    private static async Task<PostgresNativeRootOwner> PrepareAsync(EfStore store, string node, EfStore.WorkerRoots root,
        WorkflowRegistry registry, int pollLimit, CancellationToken cancellationToken, Action<InstanceRef, IReadOnlyList<Message>, long>? onTurn, int partitions)
    {
        var adapter = Adapter(store, root, registry);
        var worker = new Worker(node, root.Partition, store, adapter, new WorkerOptions { PollLimit = pollLimit, OnTurn = onTurn, Partitions = partitions });
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
}
