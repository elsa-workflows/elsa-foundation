using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.InMemory;
using Kernel.Runtime;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Postgres;

/// <summary>Starts the Built-in Actor Foundation on an installed ordinary PostgreSQL preview store.</summary>
public static class PostgresPreviewHosting
{
    /// <summary>
    /// Starts one runtime node for a public actor type. Each node opens its own store and uses the installed
    /// partition count. Configure the same actor type and definitions on every node sharing the store.
    /// The caller owns the returned node and stops it before shutting down the host.
    /// </summary>
    public static async Task<RuntimeNode> StartAsync(Func<KernelDb> create, string node, Contract.IActorType actor,
        LoopOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        ArgumentNullException.ThrowIfNull(actor);
        if (!FoundationMetadataLimits.FitsName(actor.Type))
            throw new ArgumentException("Invalid root actor type.", nameof(actor));

        int partitions;
        await using (var db = create())
        {
            if (!db.Database.IsNpgsql() || db.RequiredFormat != StoreFormat.Current)
                throw new NotSupportedException("Hosting requires an ordinary PostgreSQL generated-root preview.");
            await EfStore.RequireStoreVersionsAsync(db, cancellationToken);
            partitions = await PostgresRootAdmission.ReadInstalledPartitionCountAsync(db, cancellationToken);
        }

        var store = new EfStore(create, postgres: true);
        var scope = store.BindWorkerNode();
        var host = DurableHost.ForNode(node, store, new ActorAdapter(new RootHeaders(scope), actor), partitions);
        var runtime = new RuntimeNode(node, store, partitions, options);
        try
        {
            await runtime.StartAsync(host, cancellationToken);
            return runtime;
        }
        catch
        {
            await runtime.KillAsync();
            throw;
        }
    }

    internal sealed class RootHeaders(EfStore.WorkerRoots roots) : IRootDispatchHeaders
    {
        public (string Tenant, string Reference) Creator(Message creator)
        {
            if (roots.Discover)
            {
                if (creator.FoundationRootBinding is not { } binding || binding.Actor != creator.Target.Id || !roots.Includes(creator.Target.Partition) ||
                    creator.TargetCreation != 1 || creator.InboxKey != new InboxKey("root:" + binding.Actor))
                    throw new InvalidOperationException("The polled creator needs its admitted root identity.");
                return (binding.Tenant, binding.Reference);
            }
            var root = roots.ForActor(creator.Target.Id);
            return root.Owns(creator.InboxKey) ? (root.Tenant, root.Reference) : throw new InvalidOperationException("Unexpected root creator.");
        }

        public FoundationDispatchHeader Checkpoint(InstanceRow row) =>
            throw new InvalidOperationException("PostgreSQL must load the root's public header with its checkpoint.");
    }
}
