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

    /// <summary>Selects a dedicated PostgreSQL preview format with one outstanding timed Native job per generated root.</summary>
    public static KernelDb CreateTimedNativeJobContext(DbContextOptions<KernelDb> options) => KernelDb.ForPostgresRootTimedJobPreview(options);

    /// <summary>
    /// Opens the Foundation's ingress and reads on an installed PostgreSQL Core Preview store, for the configured actor type.
    /// With <paramref name="replyNode"/>, on a store with held replies (<c>ReplyProtocol</c>), its requests start a root or
    /// send to one and hold the caller (<see cref="PostgresPreviewFoundation.Requests"/>); the node that owns the roots
    /// answers through <see cref="PostgresPreviewFoundation.Requests"/> as its <c>replies</c>.
    /// </summary>
    public static async Task<PostgresPreviewFoundation> OpenFoundationAsync(Func<KernelDb> create, string actorType,
        string? replyNode = null, TimeSpan? deduplicationWindow = null, int pageSize = 64, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        PostgresRootAdmission.RequireActorType(actorType);
        var window = deduplicationWindow ?? TimeSpan.FromHours(1);
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        await using (var db = create())
        {
            if (!db.Database.IsNpgsql() || !(db.SupportsCorePreview || db.UsesRootReplyProtocol))
                throw new NotSupportedException("The Foundation requires a PostgreSQL Core Preview format.");
            if (replyNode is not null && !db.UsesRootReplyProtocol)
                throw new NotSupportedException("Held requests require a PostgreSQL store with held replies.");
            await EfStore.RequireStoreVersionsAsync(db, cancellationToken);
            await PostgresRootAdmission.RequireIngressIssuerAsync(db, cancellationToken);
        }
        var admission = new PostgresRootAdmission(create, window, lockOrdinaryWrites: true);
        var store = new EfStore(create, postgres: true);
        var requests = replyNode is null ? null : new PostgresDirectRequests(admission, store, replyNode, actorType);
        return new PostgresPreviewFoundation(admission, new(store, await admission.OpenJournalCursorAsync(cancellationToken), pageSize),
            actorType, directRequests: requests);
    }

    /// <summary>
    /// Starts one runtime node for a public actor type. Each node opens its own store and uses the installed
    /// partition count. Configure the same actor type and definitions on every node sharing the store.
    /// The caller stops the returned node before shutting down the host. The caller's endProcess policy
    /// must terminate the process if the watchdog cannot clear a stuck owner call.
    /// </summary>
    /// <param name="replies">
    /// How the node answers held callers, required on a store with held replies (<c>ReplyProtocol</c>) and refused on any
    /// other: <see cref="PostgresPreviewFoundation.Requests"/> when the callers wait in this process.
    /// </param>
    public static async Task<RuntimeNode> StartAsync(Func<KernelDb> create, string node, Contract.IActorType actor,
        Action<string> endProcess, LoopOptions? options = null, CancellationToken cancellationToken = default,
        Action<Exception>? onFault = null, IReplyTransport? replies = null)
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
            nativeJobs = db.UsesRootJobProtocol;
            if (!db.Database.IsNpgsql() || !(db.SupportsCorePreview || db.UsesRootReplyProtocol))
                throw new NotSupportedException("Hosting requires a PostgreSQL Core Preview format.");
            if (db.UsesRootReplyProtocol != (replies is not null))
                throw new NotSupportedException(replies is null
                    ? "A store with held replies needs the reply transport its callers wait on."
                    : "Only a store with held replies answers held callers.");
            await EfStore.RequireStoreVersionsAsync(db, cancellationToken);
            partitions = await PostgresRootAdmission.ReadInstalledPartitionCountAsync(db, cancellationToken);
        }

        var store = new EfStore(create, postgres: true);
        if (nativeJobs) await store.EnableRootJobsAsync(cancellationToken);
        var scope = store.BindWorkerNode();
        var watchdog = new Watchdog(Defaults.Lease, endProcess);
        var watched = watchdog.Watch(store);
        var adapter = new ActorAdapter(new EfStore.RootHeaders(scope), actor, rootJobHandle: store.RootJobHandle,
            rootTimedJobClock: store.RootTimedJobSettlementEnabled ? store.ReadRootTimedJobPublicationTimeAsync : null,
            rootHeldReplies: replies is not null);
        var host = DurableHost.ForNode(node, watched, adapter, partitions, replies is null ? null : new WorkerOptions { Transport = replies, Partitions = partitions });
        var runtime = new RuntimeNode(node, watched, partitions, options, beside: watchdog.RunAsync,
            beforeRelease: host.DrainEnteredTurnsAsync, onFault: onFault);
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
