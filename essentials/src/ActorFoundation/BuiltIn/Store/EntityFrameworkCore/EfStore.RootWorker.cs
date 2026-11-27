using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private WorkerRoots? _ordinaryRoots;
    private readonly object _workerBinding = new();
    private readonly object _creatorWitnessIssuer = new();

    // An Embedded owner serves generated roots in one partition, optionally restricted to an explicit set.
    internal sealed record WorkerRoot(string Tenant, string Reference, string Actor, int Partition, string Fingerprint)
    {
        internal bool Owns(InboxKey key) => key.Match is null && key.Scope switch
        {
            0 => key.Id == "root:" + Actor,
            -1 => SelfActor(key) == Actor || DeadlineActor(key) == Actor,
            InboxKey.FoundationOccurrenceScope => key.Id.StartsWith(Reference + "/", StringComparison.Ordinal) ||
                key.Id.StartsWith(PostgresRootAdmission.OperatorOccurrencePrefix + Reference + "/", StringComparison.Ordinal),
            > 0 => JobActor(key) == Actor,
            _ => false
        };
    }

    private sealed record PostgresCreatorWitness(object Issuer, WorkerRoot Root, Message Creator, long BirthTableOid, long Sequence)
        : RootCreatorWitness(Creator);

    internal sealed class WorkerRoots(int? partition, IReadOnlyList<WorkerRoot> roots, bool discover = false)
    {
        internal int? FixedPartition { get; } = partition;
        internal int Partition => FixedPartition ?? throw new InvalidOperationException("This node serves a runtime-owned set.");
        internal bool Includes(int home) => FixedPartition is null || FixedPartition == home;
        internal bool Discover { get; } = discover;
        internal bool RequiresCommittedTerminal { get; set; }
        internal bool RequiresDurableRetentionPolicy { get; set; }
        internal bool KeepsParkInputs { get; set; }
        internal bool KeepsParkArrivals { get; set; }
        internal bool KeepsPrivateParkInputs { get; set; }
        internal bool RecoversPark { get; set; }
        internal bool TakesLiveEventWaits { get; set; }
        internal bool KeepsEventWaits { get; set; }
        internal bool SupportsPublicEventRequests { get; set; }
        internal bool PagesPublicEvents { get; set; }
        internal bool PublishesAuthoredEvents { get; set; }
        internal bool MeasuresDueBacklog { get; set; }
        internal bool RefusesParkedOperators { get; set; }
        internal Func<StartSpec, object, bool>? ValidateInlineMaterial { get; set; }
        internal Func<string, Contract.ActorStatus, bool>? AllowsWriteFreeInlineCompletion { get; set; }
        internal object? InlineIssuer { get; set; }
        internal bool RecoversInlineInitialRoots { get; set; }
        internal bool SettlesEndedMailbox { get; init; }
        internal Func<Contract.ActorHeader, bool>? AuthoredWithholds { get; init; }
        internal string[] Actors { get; } = roots.Select(r => r.Actor).ToArray();
        private readonly Dictionary<string, WorkerRoot> _actors = roots.ToDictionary(r => r.Actor, StringComparer.Ordinal);
        private readonly Dictionary<string, WorkerRoot> _references = roots.ToDictionary(r => r.Reference, StringComparer.Ordinal);
        internal WorkerRoot ForActor(string actor) => _actors.TryGetValue(actor, out var root) ? root : throw RootShape();
        internal WorkerRoot ForInput(InboxKey key)
        {
            var (name, creator) = InputBinding(key);
            var index = creator ? _actors : _references;
            return index.TryGetValue(name, out var root) && root.Owns(key) ? root : throw RootShape();
        }
    }

    internal sealed class RootHeaders(WorkerRoots roots, Func<string, CreationAddress, string>? childReference = null,
        Func<string, string>? childMessageHandle = null) : IRootDispatchHeaders
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

        public bool SupportsChildren => childReference is not null;
        public Func<string, string>? ChildMessageHandle => childMessageHandle;
        public string? ChildReference(string tenant, CreationAddress address) => childReference?.Invoke(tenant, address);

        public bool RequiresCommittedTerminal => roots.RequiresCommittedTerminal;
        public bool RequiresDurableRetentionPolicy => roots.RequiresDurableRetentionPolicy;
        public bool KeepsParkInputs => roots.KeepsParkInputs;
        public bool KeepsParkArrivals => roots.KeepsParkArrivals;
        public bool KeepsPrivateParkInputs => roots.KeepsPrivateParkInputs;
        public bool RecoversPark => roots.RecoversPark;
        public bool TakesLiveEventWaits => roots.TakesLiveEventWaits;
        public bool KeepsEventWaits => roots.KeepsEventWaits;
        public bool SupportsPublicEventRequests => roots.SupportsPublicEventRequests;
        public bool PublishesAuthoredEvents => roots.PublishesAuthoredEvents;
        public bool RefusesParkedOperators => roots.RefusesParkedOperators;
        public bool SettlesEndedMailbox => roots.SettlesEndedMailbox;
        public Func<StartSpec, object, bool>? ValidateInlineMaterial => roots.ValidateInlineMaterial;
        public bool AllowsWriteFreeInlineCompletion(string tenant, Contract.ActorStatus status) =>
            roots.AllowsWriteFreeInlineCompletion?.Invoke(tenant, status) != false;
        public object? InlineIssuer => roots.InlineIssuer;
        public bool RecoversInlineInitialRoots => roots.RecoversInlineInitialRoots;

        public FoundationDispatchHeader Checkpoint(InstanceRow row) =>
            throw new InvalidOperationException("PostgreSQL must load the root's public header with its checkpoint.");
    }

    private static (string Name, bool Creator) InputBinding(InboxKey key)
    {
        if (key.Match is null && key.Scope == 0 && key.Id.StartsWith("root:", StringComparison.Ordinal)) return (key.Id[5..], true);
        if (SelfActor(key) is { } self) return (self, true);
        if (DeadlineActor(key) is { } deadline) return (deadline, true);
        if (JobActor(key) is { } job) return (job, true);
        if (key.Match is null && key.Scope == InboxKey.FoundationOccurrenceScope)
        {
            var start = PostgresRootAdmission.IsOperatorOccurrence(key) ? PostgresRootAdmission.OperatorOccurrencePrefix.Length : 0;
            var split = key.Id.IndexOf('/', start);
            if (split > start) return (key.Id[start..split], false);
        }
        throw RootShape();
    }

    internal async Task<(WorkerRoots Scope, int Count)> BindWorkerPartitionAsync(int partition, CancellationToken token)
    {
        if (!postgres || Engine is not null || partition < 0 || await PartitionCountAsync(token) is not { } count || partition >= count)
            throw new InvalidOperationException("A generated-root owner requires an existing PostgreSQL partition.");
        return (InstallWorkerScope(new(partition, [], discover: true)), count);
    }

    internal WorkerRoots BindWorkerNode(Func<Contract.ActorHeader, bool>? withholds = null)
    {
        if (!postgres || Engine is not null || !OwnedSetsAvailable)
            throw new NotSupportedException("A public root node requires PostgreSQL owned-set commits.");
        return InstallWorkerScope(new(null, [], discover: true) { AuthoredWithholds = withholds, SettlesEndedMailbox = withholds is not null });
    }

    internal RootHeaders WorkerHeaders(WorkerRoots roots) => new(roots, _rootChildReference, _rootChildMessageHandle);

    private bool AuthoredWithholding(WorkerRoot root, RootImage current) =>
        AuthoredWithholding(new(new(root.Reference), current.Model, root.Tenant, (Contract.ActorStatus)current.Status,
            current.Fault, null, current.TerminalAt, current.TypeData, current.Labels));

    private bool AuthoredWithholding(Contract.ActorHeader header) => _ordinaryRoots?.AuthoredWithholds?.Invoke(header) == true;

    private WorkerRoots InstallWorkerScope(WorkerRoots scope)
    {
        using var db = create();
        lock (_workerBinding)
        {
            if (_ordinaryRoots is not null)
                throw new InvalidOperationException("The PostgreSQL owner is already bound.");
            if (db.UsesRootJobProtocol != RootJobsEnabled || db.UsesRootTimedJobIssuance != RootTimedJobIssuanceEnabled ||
                db.UsesRootTimedJobSettlement != RootTimedJobSettlementEnabled || db.UsesRootTimedJobPreview != RootTimedJobPreviewEnabled)
                throw new StoreFormatException("The root owner must initialize its job capability before binding to the job profile.");
            if (RootTimedJobs && !RootTimedJobPreviewEnabled && (scope.Discover || scope.Actors.Length != 1))
                throw new NotSupportedException("The timed job fixture binds exactly one generated root.");
            if (db.UsesRootValueProtocol != RootValuesEnabled)
                throw new StoreFormatException("The PostgreSQL owner must initialize its value capability before binding to the value qualification profile.");
            scope.RequiresCommittedTerminal = db.UsesRootRetirement;
            scope.RequiresDurableRetentionPolicy = db.UsesRootRetentionPolicy;
            scope.KeepsParkInputs = db.UsesRootParkOpening;
            scope.KeepsParkArrivals = db.UsesRootParkArrivals;
            scope.KeepsPrivateParkInputs = db.UsesRootPrivateParkInputs;
            scope.RecoversPark = db.UsesRootParkRecovery;
            scope.RecoversInlineInitialRoots = db.UsesRootInlinePromotion;
            scope.TakesLiveEventWaits = db.UsesRootLiveEventWaits;
            scope.KeepsEventWaits = db.UsesRootPublicEventStarts;
            scope.SupportsPublicEventRequests = db.UsesRootPublicEventStarts;
            scope.PagesPublicEvents = db.UsesRootPagedPublicEvents;
            scope.PublishesAuthoredEvents = db.UsesRootAuthoredEvents;
            if (scope.PublishesAuthoredEvents && RootSelfIssuer is null)
                throw new StoreFormatException("Authored event publication requires the bound root issuer.");
            scope.MeasuresDueBacklog = db.UsesRootDueBacklog;
            if (db.UsesRootChildren != (_rootChildReference is not null))
                throw new StoreFormatException("The root owner must initialize the child capability before binding.");
            scope.RefusesParkedOperators = db.UsesRootParkedOperators;
            if (_inlineStarts is { } inline && scope.Discover)
            {
                scope.ValidateInlineMaterial = inline.Validate;
                scope.AllowsWriteFreeInlineCompletion = inline.AllowsWriteFreeCompletion;
                scope.InlineIssuer = inline;
            }
            Volatile.Write(ref _ordinaryRoots, scope);
            return scope;
        }
    }

    // Every lookup is bounded by the requested actor IDs and input keys. The host retains no index of the partition's birth history.
    private async Task<WorkerRoots> ResolveWorkerRootsAsync(WorkerRoots scope, IEnumerable<string> actors, IEnumerable<InboxKey> inputs,
        CancellationToken token, KernelDb? pinnedDb = null)
    {
        if (!scope.Discover) return scope;
        var actorIds = actors.ToHashSet(StringComparer.Ordinal);
        HashSet<string> references = new(StringComparer.Ordinal);
        foreach (var key in inputs)
        {
            var (name, creator) = InputBinding(key);
            (creator ? actorIds : references).Add(name);
        }
        if (actorIds.Count + references.Count == 0) return new(scope.FixedPartition, []);
        var ids = actorIds.ToArray();
        var refs = references.ToArray();
        if (pinnedDb is not null) return await ReadWorkerRootsAsync(pinnedDb, scope, ids, refs, token);
        await using var db = create();
        await using var pinned = await BeginPinnedRootOperationAsync(db, token);
        return await ReadWorkerRootsAsync(db, scope, ids, refs, token);
    }

    private static async Task<WorkerRoots> ReadWorkerRootsAsync(KernelDb db, WorkerRoots scope, string[] ids, string[] refs,
        CancellationToken token)
    {
        var found = db.UsesSqliteRootRuntime
            ? await ResolveSqliteWorkerRootsAsync(db, scope.FixedPartition, ids, refs, token)
            : await db.Database.SqlQuery<WorkerRoot>($"""
                SELECT "Tenant", "Reference", "Actor", "Partition", "Fingerprint" FROM "FoundationRootBirths"
                WHERE ({scope.FixedPartition}::integer IS NULL OR "Partition" = {scope.FixedPartition}) AND "Creation" = 1 AND ("Actor" = ANY({ids}) OR "Reference" = ANY({refs}))
                """).ToListAsync(token);
        if (found.Any(root => !scope.Includes(root.Partition) || new InstanceRef(root.Actor).Partition != root.Partition)) throw RootShape();
        return new(scope.FixedPartition, found);
    }

    public bool CreationIdentityAvailable => _ordinaryRoots is not null;

    internal async Task<WorkerRoot> BindWorkerRootAsync(string tenant, string reference, CancellationToken cancellationToken = default)
    {
        var roots = await BindWorkerRootsAsync([(tenant, reference)], cancellationToken);
        return roots.ForActor(roots.Actors[0]);
    }

    internal async Task<WorkerRoots> BindWorkerRootsAsync(IReadOnlyList<(string Tenant, string Reference)> requested, CancellationToken cancellationToken)
    {
        if (!postgres || Engine is not null || _ordinaryRoots is not null || requested.Count == 0)
            throw new InvalidOperationException("Bind a new PostgreSQL client to generated roots in one partition.");
        var references = requested.Select(r => r.Reference).ToArray();
        var tenants = requested.Select(r => r.Tenant).ToArray();
        await using var db = create();
        await using var pinned = await BeginPinnedRootOperationAsync(db, cancellationToken);
        var bindings = await db.Database.SqlQuery<WorkerRoot>($"""
            SELECT "Tenant", "Reference", "Actor", "Partition", "Fingerprint" FROM "FoundationRootBirths"
            WHERE ("Tenant", "Reference") IN (SELECT * FROM unnest({tenants}::text[], {references}::text[])) AND "Creation" = 1
            """).ToListAsync(cancellationToken);
        if (bindings.Count != requested.Count || !requested.ToHashSet().SetEquals(bindings.Select(r => (r.Tenant, r.Reference))) ||
            bindings.Any(r => r.Partition != bindings[0].Partition || new InstanceRef(r.Actor).Partition != r.Partition))
            throw new InvalidOperationException("The supplied roots must resolve exactly in their tenant and share one partition.");
        return InstallWorkerScope(new(bindings[0].Partition, bindings));
    }

    private sealed record WorkerRootLoad(string Tenant, string Reference, string Actor, int Partition, string Fingerprint,
        string? Id, long Creation, string? State, byte[]? Wire, int? StateVersion, long? Revision, string? Header,
        History? History, InstanceStatus? Status, bool? Suspended, long? TerminalRevision, long? TerminalAt, long AsOf,
        string? Jobs, long? NextJobGeneration, string? Fault, long? ParkAt, string? Parent, string? Children);

    private IReadOnlyList<InstanceRow> MaterializeWorkerRoots(KernelDb db, WorkerRoots scope,
        IReadOnlyList<WorkerRootLoad> rows, int requestedCount)
    {
        if (rows.Count != requestedCount) throw RootShape();
        var result = new List<InstanceRow>(rows.Count);
        foreach (var row in rows)
        {
            if (!scope.Includes(row.Partition) || new InstanceRef(row.Actor).Partition != row.Partition) throw RootShape();
            if (row.Id is not { } id) continue;
            if (row is not { State: { } state, StateVersion: { } version, Revision: { } revision, Header: { } header,
                    History: { } history, Status: { } status, Suspended: { } suspended }) throw RootShape();
            var binding = new WorkerRoot(row.Tenant, row.Reference, row.Actor, row.Partition, row.Fingerprint);
            var physical = new PhysicalRoot(id, row.Creation, state, row.Wire, version, revision, header, history, status,
                suspended, row.TerminalRevision, row.TerminalAt, row.AsOf, row.Jobs, row.NextJobGeneration, row.Fault, row.ParkAt,
                row.Parent, row.Children);
            result.Add(WorkerRow(binding, DecodePhysicalRoot(db, physical)));
        }
        return result;
    }

    private InstanceRow WorkerRow(WorkerRoot root, RootImage image)
    {
        if (RootJobsEnabled)
        {
            var jobs = JobRegistry.Read(image.Jobs);
            // The initial staged SQLite worker persisted no generation on roots without jobs.
            var generation = !postgres && image.Jobs is null ? image.NextJobGeneration ?? 0 : image.NextJobGeneration;
            var parked = image is { Status: InstanceStatus.Faulted, ParkAt: > 0, TerminalAt: null, TerminalRevision: null };
            if (generation is not >= 0 || jobs.Count > 1 ||
                jobs is [var job] && (job.Generation < 0 || job.Generation != generation - 1 ||
                    image.Status != InstanceStatus.Running && !parked)) throw RootShape();
        }
        else if (image.Jobs is not null || image.NextJobGeneration is > 0) throw RootShape();
        return new InstanceRow(image.Actor.Id, image.Model, image.Definition, image.Status, "null", image.Revision, image.Parent,
            Fault: image.Fault, History: image.History, Suspended: image.Suspended, StateVersion: image.StateVersion, Creation: image.Actor.Creation,
            StartIntent: root.Fingerprint, Jobs: image.Jobs, Children: image.Children,
            ParentCreation: image.Parent is not null ? 1 : 0).WithFoundationState(image.State)
            with
            {
                NextJobGeneration = image.NextJobGeneration ?? 0,
                FoundationDispatchHeader = new(root.Tenant, root.Reference, new(image.TypeData, image.Labels))
                {
                    Terminal = image.TerminalRevision is { } revision && image.TerminalAt is { } at ? new(revision, at) : null
                }
            };
    }

    private sealed record ScopedMarks(WorkerRoot Root, long Epoch, IReadOnlyList<InboxKey> Seen, IReadOnlyList<InboxKey> Trial,
        PostgresRootAdmission.PinnedMarkSource? PostgresSource) : QualifiedRootCheckpoint(0)
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken)
        {
            return await Mark(Seen, false) && await Mark(Trial, true);

            async Task<bool> Mark(IReadOnlyList<InboxKey> keys, bool trial)
            {
                foreach (var group in keys.GroupBy(key => key.Scope switch
                         {
                             -1 when db.UsesRootTimedJobSettlement && DeadlineActor(key) is not null => RootTimedJobScope,
                             -1 => RootSelfScope,
                             > 0 => db.UsesRootTimedJobSettlement ? RootTimedJobScope : RootJobScope,
                             _ => key.Scope
                         }))
                {
                    var scope = group.Key;
                    var ids = group.Select(key => scope is RootJobScope or RootTimedJobScope ? StoredJobId(key) : key.Id).ToArray();
                    var count = db.UsesSqliteRootRuntime
                        ? await MarkSqliteRootAsync(db, Root, Epoch, trial, scope, ids, cancellationToken)
                        : await MarkPostgresRootAsync(db, trial, scope, ids, cancellationToken);
                    if (count != ids.Length) return false;
                }
                return true;
            }
        }

        private Task<int> MarkPostgresRootAsync(KernelDb db, bool trial, long scope, string[] ids, CancellationToken token)
        {
            FormattableString mark = $"""
                UPDATE "Messages" SET "Seen" = {Epoch}, "Trial" = CASE WHEN {trial} THEN {Epoch} ELSE "Trial" END
                WHERE "Target" = {Root.Actor} AND "Partition" = {Root.Partition} AND "InboxScope" = {scope}
                    AND "Id" = ANY({ids}) AND "EventMatch" IS NULL AND NOT "Done"
                """;
            if (PostgresSource is { } source)
            {
                // A name can resolve to a replacement schema even with the original relations locked.
                // The actual writes must use the inbox anchored to the pinned physical source.
                var guarded = mark.Format + "\n" + """
                    AND tableoid::bigint = {7} AND EXISTS (
                        SELECT 1 FROM "KernelStoreVersions" v JOIN pg_catalog.pg_class c ON c.oid = v.tableoid
                        WHERE v."Id" = {8} AND c.oid::bigint = {9} AND c.relnamespace::bigint = {10}
                            AND c.oid = pg_catalog.to_regclass('"KernelStoreVersions"')
                    )
                    """;
                mark = FormattableStringFactory.Create(guarded,
                    [.. mark.GetArguments(), (long)source.Messages, StoreVersionEntity.MarkerId, (long)source.Marker, (long)source.Namespace]);
            }
            return db.Database.ExecuteSqlInterpolatedAsync(mark, token);
        }
    }

    private sealed record TerminalRootInputs(WorkerRoot Root, IReadOnlyList<InboxKey> Keys, FoundationSelfIssuer? Issue,
        Message? HeldOperator, string[] SettledJobs, Func<string, CreationAddress, string>? ChildReference,
        Func<string, string>? ChildHandle) : QualifiedRootCheckpoint(0)
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken)
        {
            var terminal = db.UsesSqliteRootRuntime
                ? await ReadSqliteTerminalRevisionAsync(db, Root, cancellationToken)
                : await LockQualifiedTerminalAsync(db, Root, cancellationToken);
            if (terminal.Count != 1) return false;
            if (SettledJobs.Length > 0 && await db.Messages.AnyAsync(m => m.Target == Root.Actor && m.Partition == Root.Partition &&
                    m.InboxScope == RootTimedJobScope && SettledJobs.Contains(m.Id) && !m.Done, cancellationToken)) return false;
            if (db.UsesSqliteRootRuntime && db.UsesRootChildren)
                return await SettleSqliteRootInputsAsync(db, Root.Reference, new(Root.Actor, 1), Keys,
                    HeldOperator is null ? null : [HeldOperator], false, cancellationToken, issue: Issue, ended: Root) &&
                    await DrainRootChildCascadeAsync(db, Root, Keys, ChildReference, ChildHandle, cancellationToken);
            return (!db.UsesRootChildren || await CloseEndedChildRegistryAsync(db, Root, Keys, cancellationToken)) &&
                await SettleRootInputsAsync(db, Root.Reference, new(Root.Actor, 1), Keys,
                    HeldOperator is null ? null : [HeldOperator], Issue, false, cancellationToken) &&
                (!db.UsesRootChildren || await DrainRootChildCascadeAsync(db, Root, Keys, ChildReference, ChildHandle, cancellationToken));
        }
    }

    // The terminal revision of the exact unretired root, share-locked until the transaction ends: Finished, or a status
    // that its state family ends under the family's prefix. S5b-3b: in a parked-operator set a protected Delete ends a
    // parked root Faulted under a terminal mark, so a Faulted root with its terminal mark has ended there too.
    internal static Task<List<long>> LockQualifiedTerminalAsync(KernelDb db, WorkerRoot root, CancellationToken cancellationToken)
    {
        var family = db.RootStateFamily;
        var endsFaulted = family.EndsFaulted || db.UsesRootParkedOperators && db.UsesRootRetirement;
        return db.Database.SqlQuery<long>($"""
            SELECT i."FoundationTerminalRevision" AS "Value" FROM "Instances" i
            JOIN "FoundationRootBirths" b ON b."Actor" = i."Id" AND b."Partition" = i."Partition"
                AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference"
            WHERE b."Tenant" = {root.Tenant} AND b."Reference" = {root.Reference} AND b."Actor" = {root.Actor}
                AND b."Creation" = 1 AND i."Partition" = {root.Partition} AND NOT i."Retired"
                AND (i."Status" = {(int)InstanceStatus.Finished} OR
                    ({family.EndsCancelled} AND i."Status" = {(int)InstanceStatus.Cancelled}
                        AND starts_with(i."State", {family.Prefix})) OR
                    ({endsFaulted} AND i."Status" = {(int)InstanceStatus.Faulted}
                        AND starts_with(i."State", {family.Prefix})))
                AND i."FoundationTerminalAt" IS NOT NULL AND i."FoundationTerminalRevision" = i."Revision" FOR SHARE OF i
            """).ToListAsync(cancellationToken);
    }

    private sealed record PreparedRoot(TurnCommit? JournalTurn, QualifiedRootCheckpoint Checkpoint);

    private async ValueTask<Committed> CommitPreparedRootAsync(PartitionFence fence, TimeSpan lease, PreparedRoot prepared, CancellationToken token)
    {
        var sentAt = System.Diagnostics.Stopwatch.GetTimestamp();
        await using var db = create();
        await RequireStoreVersionsAsync(db, token);
        if (db.UsesRootJobProtocol)
            throw new NotSupportedException("The PostgreSQL job profile requires the qualified Worker checkpoint writer.");
        if (db.UsesRootValueProtocol)
            throw new NotSupportedException("The PostgreSQL value qualification requires the qualified Worker checkpoint writer.");
        if (db.UsesRootStableBody)
            throw new NotSupportedException("The PostgreSQL stable-body profile requires the qualified Worker checkpoint writer.");
        return await CommitAsync(new CommitBatch(fence.Partition, fence.Epoch, fence.Position, lease, false, [prepared.JournalTurn!], [], []),
            prepared.Checkpoint, sentAt, token);
    }

    private sealed record RootCheckpoints(IReadOnlyList<QualifiedRootCheckpoint> Commands, IReadOnlyDictionary<string, long> Revisions,
        bool JobProfile, bool TimedJobIssuance, bool TimedJobSettlement, bool TimedJobPreview, bool ValueProfile, IReadOnlySet<string> DeferredJournal,
        bool SqliteRuntime)
        : QualifiedRootCheckpoint(0)
    {
        internal IReadOnlyList<EventRow> Publications { get; init; } = [];
        internal override long RevisionFor(string actor) => Revisions[actor];

        internal RootCheckpoints BindRefusalSource(uint? marker) => Commands.Any(command => command is RefusedRootInput)
            ? this with
            {
                Commands = Commands.Select(command => command is RefusedRootInput refusal
                    ? refusal with { PostgresMarker = marker } : command).ToArray()
            }
            : this;
        internal override bool WritesJournal(string actor) => DeferredJournal.Contains(actor);
        internal override async ValueTask CheckBeforeWriteAsync(KernelDb db, CancellationToken cancellationToken)
        {
            foreach (var command in Commands) await command.CheckBeforeWriteAsync(db, cancellationToken);
        }

        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken)
        {
            if (db.UsesSqliteRootRuntime != SqliteRuntime) return false;
            if (db.UsesRootJobProtocol != JobProfile || db.UsesRootTimedJobIssuance != TimedJobIssuance ||
                db.UsesRootTimedJobSettlement != TimedJobSettlement || db.UsesRootTimedJobPreview != TimedJobPreview ||
                db.UsesRootValueProtocol != ValueProfile)
                return false;
            foreach (var command in Commands)
                if (!await command.BindCheckpointAsync(db, cancellationToken)) return false;
            return true;
        }
    }

    private static NotSupportedException RootShape() => new("The PostgreSQL root owner cannot persist this batch without dropping effects.");

    private sealed record PostgresMarkPreparation(EfStore Store, WorkerRoots Scope, CommitBatch Batch) : QualifiedRootCheckpoint(0)
    {
        internal async Task<(RootCheckpoints Checkpoints, bool FenceChecked, long? FenceTime)> PrepareAsync(KernelDb db, CancellationToken token)
        {
            if (!db.UsesRootParkOpening || db.Database.CurrentTransaction is null)
                throw new StoreFormatException("The PostgreSQL mark plan requires the pinned owned transaction.");
            var inputs = Batch.Seen.Concat(Batch.Trial).Select(InputBinding).ToLookup(input => input.Creator, input => input.Name);
            var fences = Batch.Fenced;
            var checkFence = !Store.FenceInBatch && !Store.Unfenced && !Store.FenceIgnoresCount && !Store.UnorderedFenceLocks &&
                Batch.DueBacklog is null;
            var (matches, source, fenceTime) = await PostgresRootAdmission.PinRootMarkBindingsAsync(db, Scope.FixedPartition,
                inputs[true].Distinct(StringComparer.Ordinal).ToArray(), inputs[false].Distinct(StringComparer.Ordinal).ToArray(),
                checkFence ? fences : null, token);
            if (matches.Any(root => !Scope.Includes(root.Partition) || new InstanceRef(root.Actor).Partition != root.Partition)) throw RootShape();
            var roots = new WorkerRoots(Scope.FixedPartition, matches);
            var checkpoints = Store.WorkerCheckpoints(PrepareWorkerMarks(roots, Batch, fences.ToDictionary(fence => fence.Partition), source), [], []);
            return (checkpoints, checkFence, fenceTime);
        }

        internal override Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The PostgreSQL marks were not prepared.");
    }

    private async ValueTask<Committed> CommitWorkerRootAsync(WorkerRoots roots, CommitBatch batch, long sentAt, CancellationToken cancellationToken)
    {
        var fences = batch.Fenced;
        if (fences.Count == 0 || fences.Any(f => !roots.Includes(f.Partition)) ||
            fences[0] != new PartitionFence(batch.Partition, batch.Epoch, batch.Position) || batch.ClearJobTrials.Count != 0 ||
            batch.Window is not null || batch.PublicationReservation is not null ||
            batch.Turns.Select(t => t.Instance).Distinct().Count() != batch.Turns.Count) throw RootShape();

        if (batch.Seen.Concat(batch.Trial).Any(key =>
            key.Scope == -1 && (DeadlineActor(key) is not null ? !RootTimedJobSettlementEnabled : RootSelfIssuer is null) ||
            key.Scope > 0 && !RootJobsEnabled)) throw RootShape();
        StoreBatchRules.RequireDistinct(fences);
        if (postgres && OwnedCommit && roots.Discover && roots.KeepsParkInputs && batch.Turns.Count == 0 &&
            batch.Seen.Count + batch.Trial.Count > 0)
            return await CommitAsync(batch with { Seen = [], Trial = [] }, new PostgresMarkPreparation(this, roots, batch), sentAt, cancellationToken);

        // The queue spends each tenant's rate permit. Promotion reserves one node slot for the entire write batch.
        using var promotions = _inlineStarts is { } inline && batch.Turns.Any(turn => turn.RootCreator is InlineRootCreatorWitness)
            ? AdmissionPressure?.Begin(inline.Scope.Tenant, RootAdmissionKind.Start) : null;
        (CommitBatch Batch, RootCheckpoints Checkpoints, IReadOnlyDictionary<string, InlineRootResult>? InlineRefusals)? prepared;
        try
        {
            prepared = await PrepareWorkerBatchAsync(roots, batch, promotions, sentAt, cancellationToken);
            if (prepared is { } pending && !await ProtectPreparedChildBirthsAsync(pending.Checkpoints, cancellationToken))
                return new(false, 0, []);
        }
        catch (Exception error) when (error is not (OperationCanceledException or NotSupportedException) &&
            Classify(error) is { Failure: Failure.Definite or Failure.Transient } failure)
        {
            // Preparation reads inputs before either transactional fence mode has run.
            if (!await HoldsAfterFailureAsync(batch, cancellationToken)) return new(false, 0, []);
            if (ReferenceEquals(error, failure)) throw;
            throw failure;
        }
        if (prepared is not { } work) return new(false, 0, []);
        var committed = await CommitAsync(work.Batch, work.Checkpoints, sentAt, cancellationToken);
        return committed.Landed && work.InlineRefusals is { Count: > 0 }
            ? committed with { InlineRefusals = work.InlineRefusals } : committed;
    }

    private async Task<(CommitBatch, RootCheckpoints, IReadOnlyDictionary<string, InlineRootResult>?)?> PrepareWorkerBatchAsync(
        WorkerRoots roots, CommitBatch batch, RootAdmissionPressure.Attempt? promotions, long sentAt, CancellationToken cancellationToken)
    {
        var byPartition = batch.Fenced.ToDictionary(f => f.Partition);
        PartitionFence FenceFor(WorkerRoot root) =>
            byPartition.TryGetValue(root.Partition, out var fence) ? fence : throw RootShape();
        var observed = RootsFromCreatorWitnesses(roots, batch);
        // The birth and its pending creator can be read under the same pinned format for ordinary first turns.
        var shareFirstRootScope = observed is null && postgres && roots.Discover && roots.KeepsParkInputs &&
            batch.Turns.Count > 0 && batch.Turns.All(IsOrdinaryFirstTurn);
        await using var db = shareFirstRootScope ? create() : null;
        await using var pinned = db is not null ? await BeginPinnedRootOperationAsync(db, cancellationToken) : null;
        roots = observed ?? await ResolveWorkerRootsAsync(roots, batch.Turns.Where(t => t.RootCreator is not InlineRootCreatorWitness)
            .Select(t => t.Instance), batch.Seen.Concat(batch.Trial), cancellationToken, db);
        var commands = PrepareWorkerMarks(roots, batch, byPartition);
        var turns = new List<TurnCommit>();
        var revisions = new Dictionary<string, long>(StringComparer.Ordinal);
        List<EventRow>? publications = null;
        var positions = batch.Fenced.ToDictionary(fence => fence.Partition, fence => fence.Position);
        var deferred = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, InlineRootResult>? inlineRefusals = null;
        foreach (var submitted in batch.Turns)
        {
            var creator = observed is not null ? (PostgresCreatorWitness)submitted.RootCreator! : null;
            var turn = submitted.RootCreator is null ? submitted : submitted with { RootCreator = null };
            if (submitted.RootCreator is InlineRootCreatorWitness inline)
            {
                if (_inlineStarts is not { } installed || !ReferenceEquals(installed, inline.Issuer) ||
                    inline.Creator.FoundationRootBinding is not { } identity || installed.ScopeFor(identity.Tenant) is not { } scope ||
                    identity.Actor != turn.Instance || !byPartition.TryGetValue(inline.Creator.Target.Partition, out var fence))
                    throw RootShape();
                try { promotions?.RequireInlinePromotion(fence.Partition); }
                catch (RootAdmissionRefused refusal)
                {
                    (inlineRefusals ??= new(StringComparer.Ordinal)).Add(turn.Instance,
                        new(InlineRootDisposition.Refused, RefusalReason: refusal.Admission.Reason));
                    continue;
                }
                TimeSpan Remaining()
                {
                    var worker = inline.LeaseRemaining();
                    var commit = batch.Lease - System.Diagnostics.Stopwatch.GetElapsedTime(sentAt);
                    return worker < commit ? worker : commit;
                }
                var promotion = await PrepareInlineCheckpointAsync(scope, inline.Creator, turn, fence,
                    installed.ReceiptWindow, installed.Protect, installed, inline, Remaining, cancellationToken);
                if (promotion is null)
                {
                    (inlineRefusals ??= new(StringComparer.Ordinal)).Add(turn.Instance, new(InlineRootDisposition.Refused));
                    continue;
                }
                var position = ++positions[fence.Partition];
                var initial = (InlineRootCheckpoint)promotion.Checkpoint;
                if (initial.Publications.Count != 0)
                    (publications ??= []).AddRange(initial.Publications);
                turns.Add(promotion.JournalTurn!);
                revisions.Add(turn.Instance, initial.JournalRevision);
                deferred.Add(turn.Instance);
                commands.Add(initial with { Position = position });
                continue;
            }
            var root = roots.ForActor(turn.Instance);
            if (turn.FoundationRootPublications is { } authored)
            {
                if (_ordinaryRoots?.PublishesAuthoredEvents != true || RootSelfIssuer is not { } issuer)
                    throw RootShape();
                RootEventWaits.CollectPublications(root, turn, authored, issuer, publications ??= []);
                turn = turn with { FoundationRootPublications = null };
            }
            var prepared = await PrepareWorkerTurnAsync(root, turn, FenceFor(root), cancellationToken, db, creator);
            if (prepared is null) return null;
            var checkpoint = prepared.Checkpoint;
            if (prepared.JournalTurn is { } journal)
            {
                if (_ordinaryRoots?.TakesLiveEventWaits == true &&
                    (turn.Unsubscribe || turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0))
                    journal = RootEventWaits.Lower(root, turn, journal, _ordinaryRoots.KeepsEventWaits);
                turns.Add(journal);
                revisions.Add(journal.Instance, checkpoint.JournalRevision);
                if (journal.Journal is not null)
                {
                    var position = ++positions[root.Partition];
                    if (checkpoint is RootRetirement retirement)
                    {
                        checkpoint = retirement with { Position = position };
                        deferred.Add(journal.Instance);
                    }
                }
            }
            commands.Add(checkpoint);
            if (turn.FoundationTerminalOutput is { } output)
                commands.Add(new RootTerminalOutputCheckpoint(root, checkpoint.JournalRevision, output));
        }
        return (batch with { Turns = turns, Seen = [], Trial = [] },
            WorkerCheckpoints(commands, revisions, deferred) with { Publications = publications ?? [] }, inlineRefusals);
    }

    private static bool IsOrdinaryFirstTurn(TurnCommit turn) =>
        turn is { Expected: null, Row: not null, FoundationRootOperator: null, OpensPark: false, Delete: false } &&
        turn.RootCreator is not InlineRootCreatorWitness && turn.FoundationKept is null or { Count: 0 };

    private WorkerRoots? RootsFromCreatorWitnesses(WorkerRoots scope, CommitBatch batch)
    {
        if (!postgres || !scope.Discover || !scope.KeepsParkInputs || batch.Turns.Count == 0 ||
            batch.Seen.Count != 0 || batch.Trial.Count != 0) return null;
        var found = new List<WorkerRoot>(batch.Turns.Count);
        foreach (var turn in batch.Turns)
        {
            if (!IsOrdinaryFirstTurn(turn) || turn.RootCreator is not PostgresCreatorWitness witness ||
                !ReferenceEquals(_creatorWitnessIssuer, witness.Issuer) || witness.Root.Actor != turn.Instance || !scope.Includes(witness.Root.Partition) ||
                turn.Consumed is not [var creator] || creator != witness.Creator.InboxKey) return null;
            found.Add(witness.Root);
        }
        return new(scope.FixedPartition, found);
    }

    private static List<QualifiedRootCheckpoint> PrepareWorkerMarks(WorkerRoots roots, CommitBatch batch,
        IReadOnlyDictionary<int, PartitionFence> byPartition, PostgresRootAdmission.PinnedMarkSource? postgresSource = null)
    {
        var seen = batch.Seen.ToLookup(roots.ForInput);
        var trials = batch.Trial.ToLookup(roots.ForInput);
        var commands = new List<QualifiedRootCheckpoint>();
        foreach (var root in seen.Select(g => g.Key).Union(trials.Select(g => g.Key)))
        {
            if (!byPartition.TryGetValue(root.Partition, out var fence)) throw RootShape();
            commands.Add(new ScopedMarks(root, fence.Epoch, seen[root].ToArray(), trials[root].ToArray(), postgresSource));
        }
        return commands;
    }

    private RootCheckpoints WorkerCheckpoints(IReadOnlyList<QualifiedRootCheckpoint> commands, Dictionary<string, long> revisions,
        HashSet<string> deferred) => new(commands, revisions, RootJobsEnabled, RootTimedJobIssuanceEnabled, RootTimedJobSettlementEnabled,
            RootTimedJobPreviewEnabled, RootValuesEnabled, deferred, SqliteRuntime: !postgres);

    private static bool InputsMatch(WorkerRoot root, TurnCommit turn, bool jobs = false) =>
        turn.Done.All(k => k.Scope == InboxKey.FoundationOccurrenceScope && root.Owns(k)) &&
        turn.Consumed.All(k => (k.Scope == -1 || jobs && k.Scope > 0 ||
            IsChildOccurrence(root, k)) && root.Owns(k));

    private static bool IsRootJobInput(InboxKey key) => key.Scope > 0 || DeadlineActor(key) is not null;

    private static bool SameInputs(IEnumerable<Message> inputs, IEnumerable<InboxKey> expected)
    {
        var keys = inputs.Select(m => m.InboxKey).ToArray();
        return keys.Length == expected.Count() &&
            keys.GroupBy(k => k.Scope).All(group => group.SequenceEqual(expected.Where(key => key.Scope == group.Key)));
    }

    private async Task<PreparedRoot?> PrepareWorkerTurnAsync(WorkerRoot root, TurnCommit turn, PartitionFence fence,
        CancellationToken cancellationToken, KernelDb? firstRootScope = null, PostgresCreatorWitness? observedCreator = null)
    {
        if (turn.FoundationRootRefusedInput is { } refusal)
            return PrepareRootRefusal(root, turn, refusal);
        if (turn.FoundationRootTimerRearms is not null && turn.FoundationRootOperator?.Kind != Kinds.Recover)
            throw RootShape();
        if (turn.FoundationTerminalOutput is not null &&
            (turn.Row?.Status != InstanceStatus.Finished || turn.Delete || turn.OpensPark || turn.FoundationRootOperator is not null))
            throw RootShape();
        if (!postgres && (turn.FoundationKept is not null && !turn.OpensPark && _ordinaryRoots?.KeepsParkArrivals != true ||
            turn.FoundationRootOperator is { Kind: not (Kinds.Terminate or Kinds.Delete) } sqliteOperator &&
            (sqliteOperator.Kind != Kinds.Recover || _ordinaryRoots?.RecoversPark != true)))
            throw new NotSupportedException("This SQLite root writer supports ordinary checkpoints, timed jobs, Park opening and selected Terminate and Delete.");
        if (turn is { Row.Status: InstanceStatus.Running, FoundationRootOperator: { Kind: Kinds.Terminate or Kinds.Delete,
                Operator: { RootParkMark: not null, RootParkClear: false } } stale })
            return await PrepareRecoveredParkRefusalAsync(root, turn, stale, cancellationToken);
        if (turn.FoundationRootOperator is { Kind: Kinds.Recover }) return await PrepareRootParkRecoverAsync(root, turn, cancellationToken);
        // A delete, or a retired marker, of an ended Faulted root goes on to retirement below.
        if (turn is { FoundationRootOperator: not null, Row: { Status: InstanceStatus.Faulted or InstanceStatus.Cancelled, Retired: false },
                OpensPark: false, FoundationKept: null, Delete: false } &&
            (turn.Row.Status == InstanceStatus.Faulted || turn.FoundationRootOperator.Operator?.RootParkMark is not null))
        {
            await using var profile = create();
            // S5b-2: an Open ("clearing" entry) or a page (no entry) of a clearing, or the end of the Park, clears kept rows.
            // S5b-3b: so does the protected end of a Delete, which discards the Park and keeps the row Faulted.
            if (profile.UsesRootParkedOperators)
                return turn.Row.Status == InstanceStatus.Cancelled || turn.FoundationRootOperator.Operator?.RootParkFacts is not null &&
                    (turn.Journal is null || turn.Journal.StartsWith("""{"Outcome":"clearing",""", StringComparison.Ordinal) ||
                        turn.Journal.StartsWith("""{"Outcome":"protected","Disposition":"discarded",""", StringComparison.Ordinal))
                    ? await PrepareRootParkedTerminateAsync(root, turn, cancellationToken)
                    : await PrepareRootParkedRefusalAsync(root, turn, cancellationToken);
        }
        if (turn.OpensPark && turn.Expected is null &&
            (!postgres || _ordinaryRoots?.PublishesAuthoredEvents == true ||
                _rootChildReference is not null && turn.Row?.Parent is not null))
            return await PrepareRootCreatorParkAsync(root, turn, fence, cancellationToken);
        if (turn.OpensPark || turn.FoundationKept is { Count: > 0 })
        {
            await using var profile = create();
            if (profile.UsesRootParkArrivals || profile.UsesRootParkOpening && turn.OpensPark)
                return await PrepareRootParkOpeningAsync(root, turn, profile.UsesRootParkedOperators, cancellationToken);
        }
        if (turn.Delete) return await PrepareRootRetirementAsync(root, turn, cancellationToken);
        if (turn.Row is null)
        {
            var held = turn.FoundationRootOperator;
            if (held is not null && (held.Operator?.RootHeldReply != true || held.Target.Id != root.Actor ||
                held.TargetCreation != 1 || turn.Done is not [var key] || key != held.InboxKey || turn.Consumed.Count != 0))
                throw RootShape();
            var ended = new TurnCommit(root.Actor, null, null, turn.Consumed, turn.Done, turn.Sent, turn.Subscribed,
                turn.Unsubscribed, null, Held: turn.Held) { FoundationRootOperator = held };
            if (turn != ended || turn.Done.Count + turn.Consumed.Count == 0 || turn.Sent.Count != 0 ||
                turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 } ||
                !InputsMatch(root, turn, jobs: RootTimedJobSettlementEnabled)) throw RootShape();
            if (turn.Done is [var refused] && turn.Consumed.Count == 0 && PostgresRootAdmission.IsOperatorOccurrence(refused) &&
                await PrepareLiveCompoundRefusalAsync(root, refused, held, cancellationToken) is { } liveRefusal)
                return new(null, liveRefusal);
            // A job that was running when its root ended (a Terminate or a Faulted end settled it) finishes where nothing waits
            // for it: its ticket and deadline rows left with the end, so the job's own input settles nothing.
            var settled = turn.Consumed.Where(IsRootJobInput).Select(StoredJobId).ToArray();
            return new(null, new TerminalRootInputs(root, [.. turn.Done, .. turn.Consumed.Where(key => !IsRootJobInput(key))], RootSelfIssuer,
                WithoutParkFacts(held), settled, _rootChildReference, _rootChildMessageHandle));
        }
        var row = turn.Row;
        if (turn.FoundationRootChildSends is not null && _rootChildReference is null) throw RootShape();
        var childEffects = PrepareRootChildEffects(root, row, turn.FoundationRootChildSends ?? [], fence);
        var recalls = turn.FoundationSelfRecalls;
        var values = turn.FoundationValues;
        if (values is not null && (!RootValuesEnabled || turn.Expected is null || turn.FoundationRootOperator is not null))
            throw RootShape();
        if (recalls is not null && (RootSelfIssuer is null || turn.Expected is null ||
            recalls.Any(key => SelfActor(key) != root.Actor) ||
            turn.Consumed.Any(recalls.Contains) || turn.Sent.Any(m => recalls.Contains(m.InboxKey)))) throw RootShape();
        // A family that ends Faulted roots (timed-job settlement) commits a Faulted end from any turn, the first included.
        var timedFault = RootTimedJobSettlementEnabled && row.Status == InstanceStatus.Faulted;
        var terminated = row.Status == InstanceStatus.Cancelled && (turn.FoundationRootOperator is { Kind: Kinds.Terminate } ||
            _rootChildReference is not null && turn.Consumed is [var childInput] && IsChildOccurrence(root, childInput));
        if (row.Id != root.Actor ||
            row.Creation != 1 || !RootJobsEnabled && row.NextJobGeneration is not (null or 0) || row.StartIntent != root.Fingerprint || row.Revision != checked((turn.Expected ?? 0) + 1) ||
            row.Status is not (InstanceStatus.Running or InstanceStatus.Finished) && !timedFault && !terminated ||
            row.FoundationDispatchHeader is not { } header || header.Tenant != root.Tenant || header.Reference != root.Reference ||
            turn.Unsubscribe != (row.Status == InstanceStatus.Finished || timedFault || terminated) ||
            _ordinaryRoots?.TakesLiveEventWaits != true && (turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0) ||
            turn.Held is { Count: > 0 }) throw RootShape();

        // Record equality checks the full shape, including extension fields. Adding a new durable effect
        // fails closed here until the qualified SQL writer can represent it.
        var supported = new TurnCommit(turn.Instance, row, turn.Expected, turn.Consumed, turn.Done, turn.Sent,
            turn.Subscribed, turn.Unsubscribed, turn.Journal, turn.Unsubscribe, turn.Held)
        {
            FoundationHeader = turn.FoundationHeader, FoundationRootOperator = turn.FoundationRootOperator, FoundationSelfRecalls = recalls,
            FoundationCascade = turn.FoundationCascade is { Input: null } cascade &&
                (cascade.Entries.Children.IsEmpty
                    ? RootTimedJobSettlementEnabled &&
                        (terminated || (timedFault || row.Status == InstanceStatus.Finished) && turn.FoundationRootOperator is null)
                    : _rootChildReference is not null && terminated)
                ? cascade : null,
            FoundationRootJobCompletion = RootJobsEnabled ? turn.FoundationRootJobCompletion : null,
            FoundationValues = values,
            FoundationRootChildSends = turn.FoundationRootChildSends,
            FoundationTerminalOutput = turn.FoundationTerminalOutput
        };
        var supportedRow = new InstanceRow(row.Id, row.Model, row.Definition, row.Status, row.State, row.Revision,
            _rootChildReference is null ? null : row.Parent,
            History: row.History, Suspended: row.Suspended, StateVersion: row.StateVersion, Creation: 1, StartIntent: root.Fingerprint,
            Jobs: RootJobsEnabled ? row.Jobs : null, Children: childEffects?.Children,
            ParentCreation: _rootChildReference is not null && row.Parent is not null ? 1 : 0)
            { NextJobGeneration = row.NextJobGeneration, FoundationNullState = row.FoundationNullState, FoundationDispatchHeader = header };
        if (turn != supported || row != supportedRow) throw RootShape();
        var change = turn.FoundationHeader;
        if (change is not null && (change.TypeData != header.Header.TypeData || change.Labels.Count != header.Header.Labels.Count ||
            change.Labels.Any(pair => !header.Header.Labels.TryGetValue(pair.Key, out var value) || pair.Value != value))) throw RootShape();

        if (RootTimedJobIssuanceEnabled && turn.Expected is not null ||
            RootTimedJobs && !RootTimedJobPreviewEnabled && turn.Expected is null && !RootJobTicket.StartsTimedPair(turn.Sent, root.Actor, row.Model)) throw RootShape();
        var jobState = RootJobsEnabled ? new RootJobState(row.Jobs, row.NextJobGeneration) : null;
        PreparedRoot WithSends(PreparedRoot prepared, Message[] sends)
        {
            if (sends.Length != 0) prepared = prepared with { JournalTurn = prepared.JournalTurn! with { Sent = sends } };
            return WithRootChildEffects(prepared, childEffects, row.Parent);
        }
        var state = row.FoundationNullState ? null : row.State;
        if (turn.Expected is null)
        {
            if (turn.Journal is null || turn.Consumed is not [var key] || key != new InboxKey("root:" + root.Actor) ||
                turn.Done.Count != 0 || row.Suspended || change is null ||
                row.Status is InstanceStatus.Finished or InstanceStatus.Faulted && (turn.Sent.Count != 0 || row.Jobs is not null)) throw RootShape();
            var creator = observedCreator?.Creator ?? (firstRootScope is not null
                ? await PostgresRootAdmission.ReadPendingCreatorAsync(firstRootScope, root.Tenant, root.Reference, cancellationToken)
                : postgres
                    ? await new PostgresRootAdmission(create, TimeSpan.Zero).ReadPendingCreatorAsync(root.Tenant, root.Reference, cancellationToken)
                    : await ReadSqlitePendingCreatorAsync(root.Tenant, root.Reference, cancellationToken));
            if (creator is null) return null;
            if (row.Model != creator.Start!.Model || row.Definition != creator.Start.Definition || row.History != creator.Start.History || row.Parent != creator.Start.Parent) throw RootShape();
            return WithSends(PrepareFirstRoot(root.Tenant, root.Reference, creator, fence, state, row.StateVersion,
                change.TypeData, change.Labels, turn.Journal, jobState, row.Status, header.TerminalGrowth,
                creatorSource: observedCreator is null ? null : (observedCreator.BirthTableOid, observedCreator.Sequence)),
                EncodeRootFirstSends(root, row, turn));
        }

        var wires = EncodeRootSends(root, row, turn.Sent);
        if (turn.Done.Count + turn.Consumed.Count == 0 || !InputsMatch(root, turn, RootJobsEnabled))
            throw RootShape();
        var current = await ReadFirstRootAsync(root.Tenant, root.Reference, cancellationToken);
        if (current?.Revision != turn.Expected || row.Parent != current.Parent) return null;
        // A Terminate, or a Faulted or Finished end, of a root with outstanding timed jobs settles them: its cascade names exactly the root's
        // jobs, and their ticket and deadline rows leave in its terminal commit.
        IReadOnlyList<InboxKey> inputKeys = turn.Consumed;
        string[]? settledJobs = null;
        if (supported.FoundationCascade is { Entries.Children.IsEmpty: false } childCascade)
            childEffects = WithPendingChildCascade(root, row, current, childCascade, childEffects);
        if (supported.FoundationCascade is { Entries.Jobs.IsEmpty: false } jobCascade)
        {
            if (current.Jobs is null || !JobRegistry.Read(current.Jobs).SequenceEqual(jobCascade.Entries.Jobs)) throw RootShape();
            await using var jobsDb = create();
            settledJobs = [.. await jobsDb.Messages.AsNoTracking().Where(m => m.Target == root.Actor && m.Partition == root.Partition &&
                m.InboxScope == RootTimedJobScope && !m.Done).Select(m => m.Id).ToListAsync(cancellationToken)];
            if (settledJobs.Length == 0) throw RootShape();
        }
        if (row.Model != current.Model || row.Definition != current.Definition || row.History != current.History ||
            change is null && (header.Header.TypeData != current.TypeData || header.Header.Labels.Count != current.Labels.Count ||
                current.Labels.Any(pair => !header.Header.Labels.TryGetValue(pair.Key, out var value) || pair.Value != value))) throw RootShape();
        if (settledJobs is null && RootTimedJobSettlementEnabled &&
            (!RootTimedJobPreviewEnabled || turn.Consumed.Any(IsRootJobInput)))
        {
            var timed = await PrepareRootTimedTurnAsync(root, turn, row, state, current, fence, wires, cancellationToken);
            return timed is null ? null : WithRootChildEffects(timed, childEffects, row.Parent);
        }
        await using var db = create();
        await using var pinned = await BeginPinnedRootOperationAsync(db, cancellationToken);
        var ids = turn.Done.Concat(inputKeys).Select(key => key.Scope > 0 ? StoredJobId(key) : key.Id).ToArray();
        var messages = postgres && RootSelfIssuer is not null ? db.Messages.FromSqlRaw("""SELECT * FROM "FoundationRootMessages" """) : db.Messages;
        var records = await messages.AsNoTracking().Where(m => m.Target == root.Actor && m.Partition == root.Partition &&
            (m.InboxScope == InboxKey.FoundationOccurrenceScope || m.InboxScope == RootSelfScope || RootJobsEnabled && !RootTimedJobPreviewEnabled && m.InboxScope == RootJobScope) && m.EventMatch == null && ids.Contains(m.Id) && !m.Done).OrderBy(m => m.Seq).ToListAsync(cancellationToken);
        var inputs = records.Select(record => DecodeRootInput(db, record, root.Reference, RootSelfIssuer, RootJobsEnabled)).ToArray();
        if (!SameInputs(inputs, turn.Done.Concat(inputKeys))) return null;
        if (turn.FoundationRootOperator is { } command)
        {
            if (inputs is not [var persisted] || persisted != command) return null;
            if (command.Kind == Kinds.Terminate)
            {
                if (!db.UsesRootTermination || !terminated || row.Suspended != current.Suspended ||
                    turn.Sent.Count != 0 || recalls is not null || (current.Jobs is not null) != (settledJobs is not null) || row.Jobs is not null ||
                    row.StateVersion != current.StateVersion || turn.Journal is null) throw RootShape();
                return WithRootChildEffects(PrepareRootTurn(root.Tenant, root.Reference, current, inputs, fence, row.Status, state,
                    row.StateVersion, header.Header.TypeData, header.Header.Labels, turn.Journal, operatorTermination: true,
                    jobState: settledJobs is null ? null : new(null, current.NextJobGeneration), settledJobs: settledJobs,
                    withheldInputs: AuthoredWithholding(root, current)), childEffects, row.Parent);
            }
            if (!db.UsesRootOperatorProtocol || row.Status != InstanceStatus.Running ||
                row.Suspended != (command.Kind == Kinds.Suspend) || row.StateVersion != current.StateVersion || state != current.State ||
                turn.Sent.Count != 0 || recalls is not null || turn.Journal is not null) throw RootShape();
            return PrepareRootTurn(root.Tenant, root.Reference, current, inputs, fence, row.Status, state,
                row.StateVersion, header.Header.TypeData, header.Header.Labels, null, operatorSuspension: row.Suspended,
                terminalGrowth: header.TerminalGrowth, withheldInputs: AuthoredWithholding(root, current));
        }
        if (row.Suspended != current.Suspended || inputs.Any(input => input.Operator is not null)) throw RootShape();
        if (settledJobs is not null)
        {
            if (!(terminated || timedFault || row.Status == InstanceStatus.Finished) || turn.Sent.Count != 0 || recalls is not null || row.Jobs is not null || turn.FoundationRootJobCompletion is not null ||
                values is not null || inputs.Any(input => input.Kind == Kinds.Job)) throw RootShape();
            return WithRootChildEffects(PrepareRootTurn(root.Tenant, root.Reference, current, inputs, fence, row.Status, state,
                row.StateVersion, header.Header.TypeData, header.Header.Labels, turn.Journal,
                jobState: new(null, current.NextJobGeneration), terminalGrowth: header.TerminalGrowth, settledJobs: settledJobs),
                childEffects, row.Parent);
        }
        if (RootTimedJobPreviewEnabled)
            RequireRootTimedIssuance(turn, row);
        if (RootJobsEnabled) ValidateRootJobChange(current, row, inputs, turn.Sent, turn.FoundationRootJobCompletion, RootTimedJobs);
        return WithSends(PrepareRootTurn(root.Tenant, root.Reference, current, inputs, fence, row.Status, state,
            row.StateVersion, header.Header.TypeData, header.Header.Labels, turn.Journal, recalls?.Select(key => key.Id).ToArray(),
            jobState: jobState, jobCompletion: turn.FoundationRootJobCompletion, values: values, terminalGrowth: header.TerminalGrowth), wires);
    }
}
