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
    /// Selects the shipped PostgreSQL format for a dedicated database: operators (terminate, delete, retention, held operator
    /// replies), Native jobs with timed settlement, direct held replies, Park opening and inspection, and workflow history.
    /// A node on it answers held callers (<see cref="StartAsync"/>, its <c>requests</c>).
    /// </summary>
    public static KernelDb CreateShippedContext(DbContextOptions<KernelDb> options) => KernelDb.ForPostgresRootShipped(options);

    /// <summary>
    /// Readies a dedicated database for the shipped format (<see cref="CreateShippedContext"/>), for a single-process host
    /// such as the Workbench to call before its node starts. An empty database is installed. One whose store marker is the
    /// shipped format is installed again, which completes a first install that was cut short and changes nothing in a whole
    /// one. Any other database (another kernel store format, or tables of something else) is refused, naming what it holds.
    /// Objects of PostgreSQL extensions do not count. Hosts starting at once on one database install one after the other. A
    /// multi-node production host keeps installation an explicit setup step, run while no node is.
    /// </summary>
    /// <exception cref="InvalidOperationException">The database holds something other than the shipped format.</exception>
    public static async Task InstallShippedAsync(DbContextOptions<KernelDb> options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        KernelDb Open() => CreateShippedContext(options);
        await using var guard = Open();
        if (!guard.Database.IsNpgsql())
            throw new NotSupportedException("The shipped Core Preview format installs on PostgreSQL.");
        // One install at a time: a session lock held on its own connection around the whole check and install. Its key is
        // apart from the transaction lock the install steps take themselves.
        await guard.Database.OpenConnectionAsync(cancellationToken);
        await guard.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(48602345, 2)", cancellationToken);
        try
        {
            await using (var db = Open())
            {
                if (await db.Database.SqlQueryRaw<bool>("""SELECT pg_catalog.current_schema() IS NULL AS "Value" """).SingleAsync(cancellationToken))
                    throw new InvalidOperationException(
                        "The Core Preview database has no schema on its search path to install the shipped format into. " +
                        "Recreate this database empty, or point ConnectionStrings:KernelCorePreview at an empty one.");
                var found = await ObjectsAsync(db, cancellationToken);
                if (found.Count > 0)
                {
                    if (!await db.Database.SqlQueryRaw<bool>("""SELECT to_regclass('"KernelStoreVersions"') IS NOT NULL AS "Value" """).SingleAsync(cancellationToken))
                        throw new InvalidOperationException(
                            $"The Core Preview database is not empty: it holds {string.Join(", ", found.Take(5))}{(found.Count > 5 ? " and more" : "")}, " +
                            "and no kernel store. Recreate this database empty, or point ConnectionStrings:KernelCorePreview at an empty one: the Workbench installs the shipped format there.");
                    try { await EfStore.RequireStoreVersionsAsync(db, cancellationToken); }
                    catch (StoreFormatException refused)
                    {
                        throw new InvalidOperationException(
                            $"The Core Preview database holds another kernel store format ({refused.Message}). " +
                            "Recreate this database empty, or point ConnectionStrings:KernelCorePreview at an empty one: the Workbench installs the shipped format there.",
                            refused);
                    }
                }
            }
            // Both steps are idempotent: on a store already whole they change nothing, and on one a cut-short first install
            // left (its marker seeded, the rest not yet) they complete it.
            var store = new EfStore(Open, postgres: true);
            await store.CreateAsync();
            await store.InstallPreviewRootSchemaAsync(cancellationToken);
        }
        finally
        {
            await guard.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(48602345, 2)", CancellationToken.None);
            await guard.Database.CloseConnectionAsync();
        }
    }

    // The objects in the database's own schemas, described, apart from PostgreSQL extensions and what they own: none means
    // the database is empty, so nothing is refused.
    private static Task<List<string>> ObjectsAsync(KernelDb database, CancellationToken cancellationToken) =>
        database.Database.SqlQueryRaw<string>("""
            WITH user_schema AS (
                SELECT ns.oid FROM pg_catalog.pg_namespace ns
                WHERE ns.nspname <> 'information_schema' AND ns.nspname !~ '^pg_' AND NOT EXISTS (
                    SELECT 1 FROM pg_catalog.pg_depend owned
                    WHERE owned.classid = 'pg_catalog.pg_namespace'::pg_catalog.regclass AND owned.objid = ns.oid AND owned.deptype = 'e')
            )
            SELECT pg_catalog.pg_describe_object(dependency.classid, dependency.objid, 0) AS "Value"
            FROM pg_catalog.pg_depend dependency
            JOIN user_schema ns ON ns.oid = dependency.refobjid
            WHERE dependency.refclassid = 'pg_catalog.pg_namespace'::pg_catalog.regclass
                AND dependency.classid <> 'pg_catalog.pg_extension'::pg_catalog.regclass
                AND NOT EXISTS (
                    SELECT 1 FROM pg_catalog.pg_depend owned
                    WHERE owned.classid = dependency.classid AND owned.objid = dependency.objid AND owned.deptype = 'e')
            ORDER BY 1 LIMIT 6
            """).ToListAsync(cancellationToken);

    /// <summary>
    /// Whether a node on the format <paramref name="create"/> selects takes an actor's self-sends. A host gives the answer to
    /// its model (for the Native model, <c>NativeExecutionOptions.SelfSends</c>), so that a model which continues a long
    /// burst in a self-send on a format with them runs it in one turn on a format without them, the shipped format among them.
    /// </summary>
    public static bool TakesSelfSends(Func<KernelDb> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        using var db = create();
        return db.UsesRootSelfProtocol;
    }

    /// <summary>
    /// Opens the Foundation's ingress and reads on an installed PostgreSQL Core Preview store, for the configured actor type.
    /// With <paramref name="requests"/> (<see cref="CreateRequests"/>), on a store with held replies (<c>ReplyProtocol</c>),
    /// its requests start a root or send to one and hold the caller; the node that owns the roots answers those callers when
    /// it is given the same requests. The deduplication window is the requests' when they are given.
    /// </summary>
    public static async Task<PostgresPreviewFoundation> OpenFoundationAsync(Func<KernelDb> create, string actorType,
        PostgresDirectRequests? requests = null, TimeSpan? deduplicationWindow = null, int pageSize = 64, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        PostgresRootAdmission.RequireActorType(actorType);
        if (requests is not null && deduplicationWindow is { } asked && asked != requests.DeduplicationWindow)
            throw new ArgumentException("Held requests carry their own deduplication window.", nameof(deduplicationWindow));
        var window = requests?.DeduplicationWindow ?? deduplicationWindow ?? TimeSpan.FromHours(1);
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        await using (var db = create())
        {
            if (!db.Database.IsNpgsql() || !(db.SupportsCorePreview || db.UsesRootReplyProtocol))
                throw new NotSupportedException("The Foundation requires a PostgreSQL Core Preview format.");
            if (requests is not null && !db.UsesRootReplyProtocol)
                throw new NotSupportedException("Held requests require a PostgreSQL store with held replies.");
            await EfStore.RequireStoreVersionsAsync(db, cancellationToken);
            await PostgresRootAdmission.RequireIngressIssuerAsync(db, cancellationToken);
        }
        var admission = new PostgresRootAdmission(create, window, lockOrdinaryWrites: true);
        var store = new EfStore(create, postgres: true);
        return new PostgresPreviewFoundation(admission, new(store, await admission.OpenJournalCursorAsync(cancellationToken), pageSize),
            actorType, directRequests: requests);
    }

    /// <summary>
    /// The held callers of a node on a store with held replies, created before the store is opened: the host gives them to
    /// the node (<see cref="StartAsync"/>, its <c>requests</c>) and to the Foundation its edge requests through
    /// (<see cref="OpenFoundationAsync"/>).
    /// </summary>
    public static PostgresDirectRequests CreateRequests(Func<KernelDb> create, string node, string actorType, TimeSpan? deduplicationWindow = null)
    {
        ArgumentNullException.ThrowIfNull(create);
        PostgresRootAdmission.RequireActorType(actorType);
        var window = deduplicationWindow ?? TimeSpan.FromHours(1);
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        return new PostgresDirectRequests(new PostgresRootAdmission(create, window, lockOrdinaryWrites: true), new EfStore(create, postgres: true),
            node, actorType) { DeduplicationWindow = window };
    }

    /// <summary>
    /// Starts one runtime node for a public actor type. Each node opens its own store and uses the installed
    /// partition count. Configure the same actor type and definitions on every node sharing the store.
    /// The caller stops the returned node before shutting down the host. The caller's endProcess policy
    /// must terminate the process if the watchdog cannot clear a stuck owner call.
    /// </summary>
    /// <param name="requests">
    /// The held callers this node answers, required on a store with held replies (<c>ReplyProtocol</c>) and refused on any
    /// other: <see cref="PostgresPreviewFoundation.Requests"/> of a Foundation opened with this node as its reply node.
    /// </param>
    public static Task<RuntimeNode> StartAsync(Func<KernelDb> create, string node, Contract.IActorType actor,
        Action<string> endProcess, LoopOptions? options = null, CancellationToken cancellationToken = default,
        Action<Exception>? onFault = null, PostgresDirectRequests? requests = null)
    {
        if (requests is not null && requests.Node != node)
            throw new ArgumentException($"The held callers wait for node '{requests.Node}', and this node is '{node}'.", nameof(requests));
        return StartAsync(create, node, actor, endProcess, options, cancellationToken, onFault, (IReplyTransport?)requests);
    }

    // The node with the transport its owner answers held callers through (a check may observe it on the way).
    internal static async Task<RuntimeNode> StartAsync(Func<KernelDb> create, string node, Contract.IActorType actor,
        Action<string> endProcess, LoopOptions? options, CancellationToken cancellationToken,
        Action<Exception>? onFault, IReplyTransport? replies)
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
