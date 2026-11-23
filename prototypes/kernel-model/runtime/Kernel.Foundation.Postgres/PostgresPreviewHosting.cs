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
    /// The caller stops the returned node before shutting down the host. The caller's endProcess policy
    /// must terminate the process if the watchdog cannot clear a stuck owner call.
    /// </summary>
    public static async Task<RuntimeNode> StartAsync(Func<KernelDb> create, string node, Contract.IActorType actor,
        Action<string> endProcess, LoopOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(endProcess);
        if (node.Contains('\0') || !FoundationMetadataLimits.FitsName(node))
            throw new ArgumentException("Invalid PostgreSQL node name.", nameof(node));
        PostgresRootAdmission.RequireActorType(actor.Type);

        options ??= new LoopOptions();
        var millisecond = TimeSpan.FromMilliseconds(1);
        if (options.BalanceEvery < millisecond || options.BalanceEvery > TimeSpan.FromMilliseconds(int.MaxValue) ||
            options.PollInterval < millisecond || options.Window < options.BalanceEvery * 2 ||
            RuntimeNode.Cadence(Defaults.Lease, options) < millisecond)
            throw new ArgumentException("Configure positive millisecond loop intervals and a membership window of at least two balance rounds.", nameof(options));

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
        var watchdog = new Watchdog(Defaults.Lease, endProcess);
        var watched = watchdog.Watch(store);
        var host = DurableHost.ForNode(node, watched, new ActorAdapter(new RootHeaders(scope), actor), partitions);
        var runtime = new RuntimeNode(node, watched, partitions, options, beside: watchdog.RunAsync);
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
