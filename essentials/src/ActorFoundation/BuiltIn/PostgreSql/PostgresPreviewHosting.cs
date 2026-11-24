using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Runtime;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Postgres;

/// <summary>Starts the Built-in Actor Foundation on an installed PostgreSQL Core Preview store.</summary>
public static class PostgresPreviewHosting
{
    /// <summary>Selects the preview format for a dedicated PostgreSQL database with one outstanding untimed Native job per generated root.</summary>
    public static KernelDb CreateNativeJobContext(DbContextOptions<KernelDb> options) => KernelDb.ForPostgresRootJobs(options);

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
        bool nativeJobs;
        await using (var db = create())
        {
            nativeJobs = db.UsesRootJobPreview;
            if (!db.Database.IsNpgsql() || (db.RequiredFormat != StoreFormat.Current && !nativeJobs))
                throw new NotSupportedException("Hosting requires the ordinary or Native job PostgreSQL Core Preview format.");
            await EfStore.RequireStoreVersionsAsync(db, cancellationToken);
            partitions = await PostgresRootAdmission.ReadInstalledPartitionCountAsync(db, cancellationToken);
        }

        var store = new EfStore(create, postgres: true);
        if (nativeJobs) await store.EnableRootJobsAsync(cancellationToken);
        var scope = store.BindWorkerNode();
        var watchdog = new Watchdog(Defaults.Lease, endProcess);
        var watched = watchdog.Watch(store);
        var adapter = new ActorAdapter(new EfStore.RootHeaders(scope), actor, rootJobHandle: store.RootJobHandle);
        var host = DurableHost.ForNode(node, watched, adapter, partitions);
        var runtime = new RuntimeNode(node, watched, partitions, options, beside: watchdog.RunAsync,
            beforeRelease: host.DrainEnteredTurnsAsync);
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
}
