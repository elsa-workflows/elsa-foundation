using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Runtime;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Postgres;

/// <summary>Starts the Built-in Actor Foundation on an installed PostgreSQL Core Preview store.</summary>
public static partial class PostgresPreviewHosting
{
    /// <summary>Selects the preview format for a dedicated PostgreSQL database with one outstanding untimed Native job per generated root.</summary>
    public static KernelDb CreateNativeJobContext(DbContextOptions<KernelDb> options) => KernelDb.ForPostgresRootJobs(options);

    /// <summary>Selects a dedicated PostgreSQL preview format with one outstanding timed Native job per generated root.</summary>
    public static KernelDb CreateTimedNativeJobContext(DbContextOptions<KernelDb> options) => KernelDb.ForPostgresRootTimedJobPreview(options);

    /// <summary>
    /// Selects the shipped PostgreSQL format for a dedicated database: operators (terminate, delete, retention, held operator
    /// replies), Native jobs with timed settlement, direct held replies, Park opening, inspection, arrivals and Recover,
    /// and workflow history.
    /// A node on it answers held callers (<see cref="StartAsync"/>, its <c>requests</c>).
    /// </summary>
    public static KernelDb CreateShippedContext(DbContextOptions<KernelDb> options) => KernelDb.ForPostgresRootShipped(options);

    /// <summary>Selects the dedicated preview format for qualifying indexed playback.</summary>
    public static KernelDb CreatePlaybackQualificationContext(DbContextOptions<KernelDb> options) =>
        KernelDb.ForPostgresRootPlaybackQualification(options);

    /// <summary>Selects the dedicated preview format for qualifying descendant playback.</summary>
    public static KernelDb CreatePlaybackDescendantsQualificationContext(DbContextOptions<KernelDb> options) =>
        KernelDb.ForPostgresRootPlaybackDescendantsQualification(options);

    /// <summary>Selects the dedicated preview format for qualifying trusted schedule controls.</summary>
    public static KernelDb CreateScheduleControlsQualificationContext(DbContextOptions<KernelDb> options) =>
        KernelDb.ForPostgresRootScheduleControlsQualification(options);

    /// <summary>Installs or verifies a dedicated schedule-control qualification database.</summary>
    public static Task InstallScheduleControlsQualificationAsync(DbContextOptions<KernelDb> options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return InstallPreviewFormatAsync(() => CreateScheduleControlsQualificationContext(options), cancellationToken);
    }

    /// <summary>Selects the private profile for qualifying managed schedules with the complete shipped workflow runtime.</summary>
    public static KernelDb CreateManagedSchedulesQualificationContext(DbContextOptions<KernelDb> options) =>
        KernelDb.ForPostgresRootManagedSchedulesQualification(options);

    /// <summary>Installs or verifies a dedicated managed schedule qualification database.</summary>
    public static Task InstallManagedSchedulesQualificationAsync(DbContextOptions<KernelDb> options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return InstallPreviewFormatAsync(() => CreateManagedSchedulesQualificationContext(options), cancellationToken);
    }

    /// <summary>Binds the trusted control transport to the host's chosen auxiliary schedule actor.</summary>
    public static Contract.IScheduleRootControls BindScheduleControls(Func<KernelDb> create, string scheduleActorType)
    {
        ArgumentNullException.ThrowIfNull(create);
        RootScheduleInventory.RequireActorType(scheduleActorType);
        return new ScheduleControls(new PostgresRootAdmission(create, TimeSpan.Zero, scheduleActorType: scheduleActorType));
    }

    private sealed class ScheduleControls(PostgresRootAdmission admission) : Contract.IScheduleRootControls
    {
        public async ValueTask<Contract.ScheduleControlResult> ApplyScheduleControlAsync(Contract.BoundScope scope,
            string definition, string scheduleKey, long revision, Contract.ScheduleControlKind kind, string? setHeader,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(scope);
            return await admission.ApplyScheduleControlAsync(scope.Tenant, new(new(definition, scheduleKey), revision, kind, setHeader), cancellationToken);
        }
    }

    /// <summary>Installs or verifies a dedicated descendant playback qualification database.</summary>
    public static Task InstallPlaybackDescendantsQualificationAsync(DbContextOptions<KernelDb> options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return InstallPreviewFormatAsync(() => CreatePlaybackDescendantsQualificationContext(options), cancellationToken);
    }

    /// <summary>Installs or verifies a dedicated playback qualification database.</summary>
    public static Task InstallPlaybackQualificationAsync(DbContextOptions<KernelDb> options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return InstallPreviewFormatAsync(() => CreatePlaybackQualificationContext(options), cancellationToken);
    }

    /// <summary>Uses the dedicated preview store format for qualifying retained terminal output.</summary>
    public static KernelDb CreateTerminalOutputQualificationContext(DbContextOptions<KernelDb> options, bool withTimers = false) =>
        KernelDb.ForRootTerminalOutputQualification(options, format: withTimers
            ? StoreFormat.RootS6TerminalOutputQualification : StoreFormat.RootTerminalOutputQualification);

    /// <summary>Installs or verifies a database dedicated to terminal output qualification.</summary>
    public static Task InstallTerminalOutputQualificationAsync(DbContextOptions<KernelDb> options, bool withTimers = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return InstallPreviewFormatAsync(() => CreateTerminalOutputQualificationContext(options, withTimers), cancellationToken);
    }

    /// <summary>
    /// Readies a dedicated database for the shipped format (<see cref="CreateShippedContext"/>), for a single-process host
    /// such as the Workbench to call before its node starts. An empty database is installed. One whose store marker is the
    /// shipped format is installed again, which completes a first install that was cut short and changes nothing in a whole
    /// one. Any other database (another kernel store format, or tables of something else) is refused, naming what it holds.
    /// Objects of PostgreSQL extensions do not count. Hosts starting at once on one database install one after the other. A
    /// multi-node production host keeps installation an explicit setup step, run while no node is.
    /// </summary>
    /// <exception cref="InvalidOperationException">The database holds something other than the shipped format.</exception>
    public static Task InstallShippedAsync(DbContextOptions<KernelDb> options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return InstallPreviewFormatAsync(() => CreateShippedContext(options), cancellationToken);
    }

    private static async Task InstallPreviewFormatAsync(Func<KernelDb> open, CancellationToken cancellationToken)
    {
        await using var guard = open();
        if (!guard.Database.IsNpgsql())
            throw new NotSupportedException("The shipped Core Preview format installs on PostgreSQL.");
        // One install at a time: a session lock held on its own connection around the whole check and install. Its key is
        // apart from the transaction lock the install steps take themselves.
        await guard.Database.OpenConnectionAsync(cancellationToken);
        await guard.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(48602345, 2)", cancellationToken);
        try
        {
            await using (var db = open())
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
            var store = new EfStore(open, postgres: true);
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
    /// Whether a node on the format <paramref name="create"/> takes an actor's self-sends. A host gives the answer to
    /// its model (for the Native model, <c>NativeExecutionOptions.SelfSends</c>), so that a model which continues a long
    /// burst in a self-send on a format with them runs it in one turn on a format without them.
    /// </summary>
    public static bool TakesSelfSends(Func<KernelDb> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        using var db = create();
        return db.UsesRootSelfProtocol;
    }

    /// <summary>Whether the selected format takes durable actor-owned timers, as used by the Native Delay activity.</summary>
    public static bool TakesTimers(Func<KernelDb> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        using var db = create();
        return db.UsesRootTimers;
    }

    /// <summary>Whether the selected format takes attached and detached workflow starts from an owned turn.</summary>
    public static bool TakesChildren(Func<KernelDb> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        using var db = create();
        return db.UsesRootChildren;
    }

    /// <summary>Whether the selected format records a sequenced workflow journal.</summary>
    public static bool TakesSequencedJournal(Func<KernelDb> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        using var db = create();
        return db.UsesRootSequencedJournal;
    }

    /// <summary>Whether the selected profile can register and deliver live HTTP waits.</summary>
    public static bool TakesHttpWaits(Func<KernelDb> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        using var db = create();
        return db.UsesRootLiveEventWaits;
    }

    /// <summary>
    /// Opens the Foundation's ingress and reads on an installed PostgreSQL Core Preview store, for the configured actor type.
    /// With <paramref name="requests"/> (<see cref="CreateRequests"/>), on a store with held replies (<c>ReplyProtocol</c>),
    /// its requests start a root or send to one and hold the caller; the node that owns the roots answers those callers when
    /// it is given the same requests. The deduplication window is the requests' when they are given.
    /// </summary>
    public static Task<PostgresPreviewFoundation> OpenFoundationAsync(Func<KernelDb> create, string actorType,
        PostgresDirectRequests? requests = null, TimeSpan? deduplicationWindow = null, int pageSize = 64, CancellationToken cancellationToken = default) =>
        OpenFoundationCoreAsync(create, actorType, requests, deduplicationWindow, pageSize, cancellationToken, null);

    /// <summary>
    /// Opens ingress and reads with one-way operator commands. The host authorizes each command against its bound scope
    /// and exact actor reference. The installed store determines which commands it accepts.
    /// </summary>
    public static Task<PostgresPreviewFoundation> OpenFoundationWithOperatorsAsync(Func<KernelDb> create, string actorType,
        Func<Contract.BoundScope, Contract.ActorRef, string, bool> authorizeOperator, PostgresDirectRequests? requests = null,
        TimeSpan? deduplicationWindow = null, int pageSize = 64, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorizeOperator);
        return OpenFoundationCoreAsync(create, actorType, requests, deduplicationWindow, pageSize, cancellationToken, authorizeOperator);
    }

    private static async Task<PostgresPreviewFoundation> OpenFoundationCoreAsync(Func<KernelDb> create, string actorType,
        PostgresDirectRequests? requests, TimeSpan? deduplicationWindow, int pageSize, CancellationToken cancellationToken,
        Func<Contract.BoundScope, Contract.ActorRef, string, bool>? authorizeOperator)
    {
        ArgumentNullException.ThrowIfNull(create);
        PostgresRootAdmission.RequireActorType(actorType);
        if (requests is not null && deduplicationWindow is { } asked && asked != requests.DeduplicationWindow)
            throw new ArgumentException("Held requests carry their own deduplication window.", nameof(deduplicationWindow));
        var window = requests?.DeduplicationWindow ?? deduplicationWindow ?? TimeSpan.FromHours(1);
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        var pressure = requests?.Pressure;
        bool usesPressure;
        await using (var db = create())
        {
            if (!db.Database.IsNpgsql() || !(db.SupportsCorePreview || db.UsesRootReplyProtocol))
                throw new NotSupportedException("The Foundation requires a PostgreSQL Core Preview format.");
            if (requests is not null && !db.UsesRootReplyProtocol)
                throw new NotSupportedException("Held requests require a PostgreSQL store with held replies.");
            await EfStore.RequireStoreVersionsAsync(db, cancellationToken);
            await PostgresRootAdmission.RequireIngressIssuerAsync(db, cancellationToken);
            usesPressure = db.UsesRootDueBacklog;
        }
        if (usesPressure && pressure is null)
            pressure = await EfStore.OpenStandalonePostgresPressureAsync(create, cancellationToken);
        var admission = new PostgresRootAdmission(create, window, lockOrdinaryWrites: true, pressure: pressure);
        var store = new EfStore(create, postgres: true);
        return new PostgresPreviewFoundation(admission, new(store, await admission.OpenJournalCursorAsync(cancellationToken), pageSize),
            actorType, directRequests: requests, authorizeExactOperator: authorizeOperator);
    }

    /// <summary>
    /// The held callers of a node on a store with held replies, created before the store is opened: the host gives them to
    /// the node (<see cref="StartAsync"/>, its <c>requests</c>) and to the Foundation its edge requests through
    /// (<see cref="OpenFoundationAsync"/>).
    /// </summary>
    public static PostgresDirectRequests CreateRequests(Func<KernelDb> create, string node, string actorType, TimeSpan? deduplicationWindow = null)
        => CreateRequests(create, node, actorType, deduplicationWindow, null);

    internal static PostgresDirectRequests CreateRequests(Func<KernelDb> create, string node, string actorType,
        TimeSpan? deduplicationWindow, RootAdmissionPressure? pressure)
    {
        ArgumentNullException.ThrowIfNull(create);
        PostgresRootAdmission.RequireActorType(actorType);
        var window = deduplicationWindow ?? TimeSpan.FromHours(1);
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        using var db = create();
        if (db.UsesRootDueBacklog) pressure ??= RootAdmissionPressure.CreateDefault();
        return new PostgresDirectRequests(new PostgresRootAdmission(create, window, lockOrdinaryWrites: true, pressure: pressure), new EfStore(create, postgres: true),
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
        Action<Exception>? onFault, IReplyTransport? replies, bool? fenceInBatch = null,
        Func<Contract.ActorHeader, TimeSpan?>? retentionWindow = null, Contract.IActorType? auxiliaryActor = null)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(endProcess);
        if (node.Contains('\0') || !FoundationMetadataLimits.FitsName(node))
            throw new ArgumentException("Invalid PostgreSQL node name.", nameof(node));
        PostgresRootAdmission.RequireActorType(actor.Type);
        if (auxiliaryActor is not null) ActorAdapter.RequireAuxiliaryActor(actor, auxiliaryActor);

        options ??= new LoopOptions();
        var millisecond = TimeSpan.FromMilliseconds(1);
        if (options.BalanceEvery < millisecond || options.BalanceEvery > TimeSpan.FromMilliseconds(int.MaxValue) ||
            options.PollInterval < millisecond || options.Window < options.BalanceEvery * 2 ||
            RuntimeNode.Cadence(Defaults.Lease, options) < millisecond)
            throw new ArgumentException("Configure positive millisecond loop intervals and a membership window of at least two balance rounds.", nameof(options));

        int partitions;
        bool nativeJobs, selfSends, liveReplyRelay;
        int terminalOutputLimit;
        await using (var db = create())
        {
            nativeJobs = db.UsesRootJobProtocol;
            liveReplyRelay = db.RequiredFormat == StoreFormat.RootShipped || db.UsesRootPlaybackDescendants;
            selfSends = db.UsesRootSelfProtocol;
            if (auxiliaryActor is not null && (!db.UsesRootScheduleInventory || !db.UsesRootTimedJobSettlement))
                throw new NotSupportedException("This PostgreSQL root profile does not host an auxiliary actor.");
            if (db.UsesRootScheduleControls && auxiliaryActor is null)
                throw new NotSupportedException("This PostgreSQL root profile requires a schedule owner.");
            terminalOutputLimit = db.UsesRootTerminalOutput ? EfStore.RootContentBytes : 0;
            if (!db.Database.IsNpgsql() || !(db.SupportsCorePreview || db.UsesRootReplyProtocol))
                throw new NotSupportedException("Hosting requires a PostgreSQL Core Preview format.");
            if (db.UsesRootReplyProtocol != (replies is not null))
                throw new NotSupportedException(replies is null
                    ? "A store with held replies needs the reply transport its callers wait on."
                    : "Only a store with held replies answers held callers.");
            await EfStore.RequireStoreVersionsAsync(db, cancellationToken);
            if (db.UsesRootSequencedJournal) await EfStore.RequirePostgresSequencedJournalAsync(db, cancellationToken);
            if (db.UsesRootPlaybackDescendants) await EfStore.RequirePostgresPlaybackAsync(db, cancellationToken);
            if (db.UsesRootScheduleControls) await RootScheduleControls.RequirePostgresDefinitionsAsync(db, cancellationToken);
            partitions = await PostgresRootAdmission.ReadInstalledPartitionCountAsync(db, cancellationToken);
        }

        var pressure = (replies as PostgresDirectRequests)?.Pressure;
        var store = new EfStore(create, postgres: true, fenceInBatch: fenceInBatch) { AdmissionPressure = pressure };
        if (selfSends) await store.EnableRootSelfSendsAsync(cancellationToken);
        if (nativeJobs) await store.EnableRootJobsAsync(cancellationToken);
        await store.EnableRootChildrenAsync(cancellationToken);
        var scope = store.BindWorkerNode(ActorAdapter.RootWithholding(actor, auxiliaryActor));
        await store.PrimeAdmissionPressureAsync(cancellationToken);
        var watchdog = new Watchdog(Defaults.Lease, endProcess);
        var watched = watchdog.Watch(store);
        var adapter = new ActorAdapter(store.WorkerHeaders(scope), actor, store.RootSelfIssuer, rootJobHandle: store.RootJobHandle,
            rootTimedJobClock: store.RootTimedJobSettlementEnabled ? store.ReadRootTimedJobPublicationTimeAsync : null,
            rootHeldReplies: replies is not null, retentionWindow: retentionWindow, rootTerminalOutputLimit: terminalOutputLimit, auxiliaryActor: auxiliaryActor);
        // A store error a drain retried is reported as a loop error of the node made below: counted, and reported once per
        // distinct error per window, so a drain the deadline kills shows why it made no progress.
        RuntimeNode? reporter = null;
        var edge = liveReplyRelay ? replies as PostgresDirectRequests : null;
        var relay = edge is null ? null : await edge.OpenLiveRepliesAsync(create,
            error => Volatile.Read(ref reporter)?.RecordLoopError(error), cancellationToken);
        var workers = new WorkerOptions
        {
            Partitions = partitions, Backlog = pressure?.Backlog,
            OnDrainStoreError = error => Volatile.Read(ref reporter)?.RecordLoopError(error)
        };
        var host = DurableHost.ForNode(node, watched, adapter, partitions, replies is null ? workers : workers with { Transport = replies });
        var relayStop = relay is null ? null : new CancellationTokenSource();
        Task? relaying = null;
        var relayStopping = 0;
        var runtime = new RuntimeNode(node, watched, partitions, options, beside: RunBesideAsync,
            beforeRelease: host.DrainEnteredTurnsAsync, onFault: onFault)
        {
            AfterReleaseAsync = relay is null ? null : relay.FlushAsync
        };
        Volatile.Write(ref reporter, runtime);
        try
        {
            if (relay is not null)
            {
                edge!.AttachLiveReplies(relay);
                relaying = Task.Run(() => relay.RunAsync(relayStop!.Token), CancellationToken.None);
                await relay.ReadyAsync(cancellationToken);
            }
            await runtime.StartAsync(host, cancellationToken);
            return runtime;
        }
        catch
        {
            StopRelay();
            await runtime.KillAsync();
            throw;
        }

        async Task RunBesideAsync(CancellationToken stop)
        {
            try { await watchdog.RunAsync(stop); }
            finally { StopRelay(); }
        }

        void StopRelay()
        {
            if (relay is null || Interlocked.Exchange(ref relayStopping, 1) != 0) return;
            edge!.DetachLiveReplies(relay);
            // The relay does not own durable work. Its connection cancellation must never hold up the watchdog.
            if (relaying is { } running) _ = StopRelayAsync(relayStop!, running);
            else relayStop!.Dispose();
        }

        async Task StopRelayAsync(CancellationTokenSource stop, Task relaying)
        {
            using (stop)
            {
                try { await Task.WhenAll(stop.CancelAsync(), relaying); }
                catch (Exception error) { Volatile.Read(ref reporter)?.RecordLoopError(error); }
            }
        }
    }
}
