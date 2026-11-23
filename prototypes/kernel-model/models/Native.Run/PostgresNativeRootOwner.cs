using Kernel.EntityFramework;
using Kernel.Foundation.InMemory;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

/// <summary>A fixed Embedded activation for one public Native root in the PostgreSQL Core Preview.</summary>
internal static class PostgresNativeRootOwner
{
    internal static async Task<IScopeActivation> ActivateAsync(EfStore store, string node, string tenant, string reference,
        WorkflowRegistry registry, CancellationToken cancellationToken = default)
    {
        var root = await store.BindWorkerRootAsync(tenant, reference, cancellationToken);
        var adapter = new ActorAdapter(new Headers(root), new PublicActor([new NativeExecutionModel(registry)]));
        var worker = new Worker(node, root.Partition, store, adapter, new WorkerOptions { PollLimit = 1 });
        var host = new DurableHost([worker]);
        await host.HeartbeatAsync(TimeSpan.FromSeconds(10), cancellationToken);
        return await host.ActivateAsync(root.Partition, cancellationToken) ?? throw new InvalidOperationException("The partition is already owned.");
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
