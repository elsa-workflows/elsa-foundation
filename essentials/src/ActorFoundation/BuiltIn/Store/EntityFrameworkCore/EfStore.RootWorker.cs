using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private WorkerRoots? _ordinaryRoots;
    private readonly object _workerBinding = new();

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

    internal sealed class WorkerRoots(int? partition, IReadOnlyList<WorkerRoot> roots, bool discover = false)
    {
        internal int? FixedPartition { get; } = partition;
        internal int Partition => FixedPartition ?? throw new InvalidOperationException("This node serves a runtime-owned set.");
        internal bool Includes(int home) => FixedPartition is null || FixedPartition == home;
        internal bool Discover { get; } = discover;
        internal bool RequiresCommittedTerminal { get; set; }
        internal bool KeepsParkInputs { get; set; }
        internal bool KeepsParkArrivals { get; set; }
        internal bool RecoversPark { get; set; }
        internal bool RefusesParkedOperators { get; set; }
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

    internal sealed class RootHeaders(WorkerRoots roots) : IRootDispatchHeaders
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

        public bool RequiresCommittedTerminal => roots.RequiresCommittedTerminal;
        public bool KeepsParkInputs => roots.KeepsParkInputs;
        public bool KeepsParkArrivals => roots.KeepsParkArrivals;
        public bool RecoversPark => roots.RecoversPark;
        public bool RefusesParkedOperators => roots.RefusesParkedOperators;

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

    internal WorkerRoots BindWorkerNode()
    {
        if (!postgres || Engine is not null || !OwnedSetsAvailable)
            throw new NotSupportedException("A public root node requires PostgreSQL owned-set commits.");
        return InstallWorkerScope(new(null, [], discover: true));
    }

    private WorkerRoots InstallWorkerScope(WorkerRoots scope)
    {
        using var db = create();
        lock (_workerBinding)
        {
            if (_ordinaryRoots is not null)
                throw new InvalidOperationException("The PostgreSQL owner is already bound.");
            if (db.UsesRootJobProtocol != RootJobsEnabled || db.UsesRootTimedJobIssuance != RootTimedJobIssuanceEnabled ||
                db.UsesRootTimedJobSettlement != RootTimedJobSettlementEnabled || db.UsesRootTimedJobPreview != RootTimedJobPreviewEnabled)
                throw new StoreFormatException("The PostgreSQL owner must initialize its job capability before binding to the job profile.");
            if (RootTimedJobs && !RootTimedJobPreviewEnabled && (scope.Discover || scope.Actors.Length != 1))
                throw new NotSupportedException("The timed job fixture binds exactly one generated root.");
            if (db.UsesRootValueProtocol != RootValuesEnabled)
                throw new StoreFormatException("The PostgreSQL owner must initialize its value capability before binding to the value qualification profile.");
            scope.RequiresCommittedTerminal = db.UsesRootRetirement;
            scope.KeepsParkInputs = db.UsesRootParkOpening;
            scope.KeepsParkArrivals = db.UsesRootParkArrivals;
            scope.RecoversPark = db.UsesRootParkRecovery;
            scope.RefusesParkedOperators = db.UsesRootParkedOperators;
            Volatile.Write(ref _ordinaryRoots, scope);
            return scope;
        }
    }

    // Every lookup is bounded by the requested actor IDs and input keys. The host retains no index of the partition's birth history.
    private async Task<WorkerRoots> ResolveWorkerRootsAsync(WorkerRoots scope, IEnumerable<string> actors, IEnumerable<InboxKey> inputs, CancellationToken token)
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
        await using var db = create();
        await using var pinned = await BeginPinnedRootOperationAsync(db, token);
        var found = await db.Database.SqlQuery<WorkerRoot>($"""
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

    private InstanceRow WorkerRow(WorkerRoot root, RootImage image)
    {
        if (RootJobsEnabled)
        {
            var jobs = JobRegistry.Read(image.Jobs);
            if (image.NextJobGeneration is not >= 0 || jobs.Count > 1 ||
                jobs is [var job] && (job.Generation < 0 || job.Generation != image.NextJobGeneration - 1 ||
                    image.Status != InstanceStatus.Running)) throw RootShape();
        }
        else if (image.Jobs is not null || image.NextJobGeneration is > 0) throw RootShape();
        return new InstanceRow(image.Actor.Id, image.Model, image.Definition, image.Status, "null", image.Revision, null,
            Fault: image.Fault, History: image.History, Suspended: image.Suspended, StateVersion: image.StateVersion, Creation: image.Actor.Creation,
            StartIntent: root.Fingerprint, Jobs: image.Jobs).WithFoundationState(image.State)
            with
            {
                NextJobGeneration = image.NextJobGeneration ?? 0,
                FoundationDispatchHeader = new(root.Tenant, root.Reference, new(image.TypeData, image.Labels))
                {
                    Terminal = image.TerminalRevision is { } revision && image.TerminalAt is { } at ? new(revision, at) : null
                }
            };
    }

    private sealed record ScopedMarks(WorkerRoot Root, long Epoch, IReadOnlyList<InboxKey> Seen, IReadOnlyList<InboxKey> Trial) : QualifiedRootCheckpoint(0)
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken)
        {
            return await Mark(Seen, false) && await Mark(Trial, true);

            async Task<bool> Mark(IReadOnlyList<InboxKey> keys, bool trial)
            {
                foreach (var group in keys.GroupBy(key => key.Scope))
                {
                    var scope = group.Key switch
                    {
                        -1 or > 0 when db.UsesRootTimedJobSettlement => RootTimedJobScope,
                        -1 => RootSelfScope,
                        > 0 => RootJobScope,
                        _ => group.Key
                    };
                    var ids = group.Select(key => scope is RootJobScope or RootTimedJobScope ? StoredJobId(key) : key.Id).ToArray();
                    var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE "Messages" SET "Seen" = {Epoch}, "Trial" = CASE WHEN {trial} THEN {Epoch} ELSE "Trial" END
                        WHERE "Target" = {Root.Actor} AND "Partition" = {Root.Partition} AND "InboxScope" = {scope}
                            AND "Id" = ANY({ids}) AND "EventMatch" IS NULL AND NOT "Done"
                        """, cancellationToken);
                    if (count != ids.Length) return false;
                }
                return true;
            }
        }
    }

    private sealed record TerminalRootInputs(WorkerRoot Root, IReadOnlyList<InboxKey> Keys, FoundationSelfIssuer? Issue,
        Message? HeldOperator) : QualifiedRootCheckpoint(0)
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken)
        {
            var terminal = await LockQualifiedTerminalAsync(db, Root, cancellationToken);
            return terminal.Count == 1 && await SettleRootInputsAsync(db, Root.Reference, new(Root.Actor, 1), Keys,
                HeldOperator is null ? null : [HeldOperator], Issue, false, cancellationToken);
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
        bool JobProfile, bool TimedJobIssuance, bool TimedJobSettlement, bool TimedJobPreview, bool ValueProfile, IReadOnlySet<string> DeferredJournal)
        : QualifiedRootCheckpoint(0)
    {
        internal override long RevisionFor(string actor) => Revisions[actor];
        internal override bool WritesJournal(string actor) => DeferredJournal.Contains(actor);
        internal override async ValueTask CheckBeforeWriteAsync(KernelDb db, CancellationToken cancellationToken)
        {
            foreach (var command in Commands) await command.CheckBeforeWriteAsync(db, cancellationToken);
        }

        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken)
        {
            if (db.UsesRootJobProtocol != JobProfile || db.UsesRootTimedJobIssuance != TimedJobIssuance ||
                db.UsesRootTimedJobSettlement != TimedJobSettlement || db.UsesRootTimedJobPreview != TimedJobPreview ||
                db.UsesRootValueProtocol != ValueProfile) return false;
            foreach (var command in Commands)
                if (!await command.BindCheckpointAsync(db, cancellationToken)) return false;
            return true;
        }
    }

    private static NotSupportedException RootShape() => new("The PostgreSQL root owner cannot persist this batch without dropping effects.");

    private async ValueTask<Committed> CommitWorkerRootAsync(WorkerRoots roots, CommitBatch batch, long sentAt, CancellationToken cancellationToken)
    {
        var fences = batch.Fenced;
        if (fences.Count == 0 || fences.Any(f => !roots.Includes(f.Partition)) ||
            fences[0] != new PartitionFence(batch.Partition, batch.Epoch, batch.Position) || batch.ClearJobTrials.Count != 0 ||
            batch.Window is not null || batch.PublicationReservation is not null || batch.DueBacklog is not null ||
            batch.Turns.Select(t => t.Instance).Distinct().Count() != batch.Turns.Count) throw RootShape();

        if (batch.Seen.Concat(batch.Trial).Any(key =>
            key.Scope == -1 && RootSelfIssuer is null && !RootTimedJobSettlementEnabled ||
            key.Scope > 0 && !RootJobsEnabled)) throw RootShape();
        StoreBatchRules.RequireDistinct(fences);
        (CommitBatch Batch, RootCheckpoints Checkpoints)? prepared;
        try { prepared = await PrepareWorkerBatchAsync(roots, batch, cancellationToken); }
        catch (Exception error) when (error is not (OperationCanceledException or NotSupportedException) &&
            Classify(error) is { Failure: Failure.Definite or Failure.Transient } failure)
        {
            // Preparation reads inputs before either transactional fence mode has run.
            if (!await HoldsAfterFailureAsync(batch, cancellationToken)) return new(false, 0, []);
            if (ReferenceEquals(error, failure)) throw;
            throw failure;
        }
        return prepared is { } work ? await CommitAsync(work.Batch, work.Checkpoints, sentAt, cancellationToken) : new(false, 0, []);
    }

    private async Task<(CommitBatch, RootCheckpoints)?> PrepareWorkerBatchAsync(WorkerRoots roots, CommitBatch batch,
        CancellationToken cancellationToken)
    {
        var byPartition = batch.Fenced.ToDictionary(f => f.Partition);
        PartitionFence FenceFor(WorkerRoot root) =>
            byPartition.TryGetValue(root.Partition, out var fence) ? fence : throw RootShape();
        roots = await ResolveWorkerRootsAsync(roots, batch.Turns.Select(t => t.Instance), batch.Seen.Concat(batch.Trial), cancellationToken);
        var commands = new List<QualifiedRootCheckpoint>();
        var turns = new List<TurnCommit>();
        var revisions = new Dictionary<string, long>(StringComparer.Ordinal);
        var positions = batch.Fenced.ToDictionary(fence => fence.Partition, fence => fence.Position);
        var deferred = new HashSet<string>(StringComparer.Ordinal);
        var seen = batch.Seen.ToLookup(roots.ForInput);
        var trials = batch.Trial.ToLookup(roots.ForInput);
        foreach (var root in seen.Select(g => g.Key).Union(trials.Select(g => g.Key)))
            commands.Add(new ScopedMarks(root, FenceFor(root).Epoch, seen[root].ToArray(), trials[root].ToArray()));
        foreach (var turn in batch.Turns)
        {
            var root = roots.ForActor(turn.Instance);
            var prepared = await PrepareWorkerTurnAsync(root, turn, FenceFor(root), cancellationToken);
            if (prepared is null) return null;
            var checkpoint = prepared.Checkpoint;
            if (prepared.JournalTurn is { } journal)
            {
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
        }
        return (batch with { Turns = turns, Seen = [], Trial = [] },
            new RootCheckpoints(commands, revisions, RootJobsEnabled, RootTimedJobIssuanceEnabled, RootTimedJobSettlementEnabled, RootTimedJobPreviewEnabled, RootValuesEnabled, deferred));
    }

    private static bool InputsMatch(WorkerRoot root, TurnCommit turn, bool jobs = false) =>
        turn.Done.All(k => k.Scope == InboxKey.FoundationOccurrenceScope && root.Owns(k)) &&
        turn.Consumed.All(k => (k.Scope == -1 || jobs && k.Scope > 0) && root.Owns(k));

    private static bool SameInputs(IEnumerable<Message> inputs, IEnumerable<InboxKey> expected)
    {
        var keys = inputs.Select(m => m.InboxKey).ToArray();
        return keys.Length == expected.Count() &&
            keys.GroupBy(k => k.Scope).All(group => group.SequenceEqual(expected.Where(key => key.Scope == group.Key)));
    }

    private async Task<PreparedRoot?> PrepareWorkerTurnAsync(WorkerRoot root, TurnCommit turn, PartitionFence fence, CancellationToken cancellationToken)
    {
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
                !InputsMatch(root, turn)) throw RootShape();
            return new(null, new TerminalRootInputs(root, [.. turn.Done, .. turn.Consumed], RootSelfIssuer, WithoutParkFacts(held)));
        }
        var row = turn.Row;
        var recalls = turn.FoundationSelfRecalls;
        var values = turn.FoundationValues;
        if (values is not null && (!RootValuesEnabled || turn.Expected is null || turn.FoundationRootOperator is not null))
            throw RootShape();
        if (recalls is not null && (RootSelfIssuer is null || turn.Expected is null ||
            recalls.Any(key => key.Scope != -1 || !root.Owns(key)) ||
            turn.Consumed.Any(recalls.Contains) || turn.Sent.Any(m => recalls.Contains(m.InboxKey)))) throw RootShape();
        var timedFault = RootTimedJobSettlementEnabled && turn.Expected is not null && row.Status == InstanceStatus.Faulted;
        var terminated = turn.FoundationRootOperator is { Kind: Kinds.Terminate } && row.Status == InstanceStatus.Cancelled;
        if (row.Id != root.Actor ||
            row.Creation != 1 || !RootJobsEnabled && row.NextJobGeneration is not (null or 0) || row.StartIntent != root.Fingerprint || row.Revision != checked((turn.Expected ?? 0) + 1) ||
            row.Status is not (InstanceStatus.Running or InstanceStatus.Finished) && !timedFault && !terminated ||
            row.FoundationDispatchHeader is not { } header || header.Tenant != root.Tenant || header.Reference != root.Reference ||
            turn.Unsubscribe != (row.Status == InstanceStatus.Finished || timedFault || terminated) ||
            turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 }) throw RootShape();

        // Record equality checks the full shape, including extension fields. Adding a new durable effect
        // fails closed here until the qualified SQL writer can represent it.
        var supported = new TurnCommit(turn.Instance, row, turn.Expected, turn.Consumed, turn.Done, turn.Sent,
            turn.Subscribed, turn.Unsubscribed, turn.Journal, turn.Unsubscribe, turn.Held)
        {
            FoundationHeader = turn.FoundationHeader, FoundationRootOperator = turn.FoundationRootOperator, FoundationSelfRecalls = recalls,
            FoundationRootJobCompletion = RootJobsEnabled ? turn.FoundationRootJobCompletion : null,
            FoundationValues = values
        };
        var supportedRow = new InstanceRow(row.Id, row.Model, row.Definition, row.Status, row.State, row.Revision, null,
            History: row.History, Suspended: row.Suspended, StateVersion: row.StateVersion, Creation: 1, StartIntent: root.Fingerprint,
            Jobs: RootJobsEnabled ? row.Jobs : null)
            { NextJobGeneration = row.NextJobGeneration, FoundationNullState = row.FoundationNullState, FoundationDispatchHeader = header };
        if (turn != supported || row != supportedRow) throw RootShape();
        var change = turn.FoundationHeader;
        if (change is not null && (change.TypeData != header.Header.TypeData || change.Labels.Count != header.Header.Labels.Count ||
            change.Labels.Any(pair => !header.Header.Labels.TryGetValue(pair.Key, out var value) || pair.Value != value))) throw RootShape();

        if (RootTimedJobIssuanceEnabled && turn.Expected is not null ||
            RootTimedJobs && !RootTimedJobPreviewEnabled && turn.Expected is null && !RootJobTicket.StartsTimedPair(turn.Sent, root.Actor, row.Model)) throw RootShape();
        var wires = RootTimedJobs ? EncodeRootTimedSends(turn.Sent) :
            turn.Sent.Select((message, index) => RootJobsEnabled && message.Kind == Kinds.Job
                ? EncodeRootJob(message, root, row.Model) : EncodeRootSelf(root, row.Revision, index, message)).ToArray();
        var jobState = RootJobsEnabled ? new RootJobState(row.Jobs, row.NextJobGeneration) : null;
        PreparedRoot WithSends(PreparedRoot prepared) => wires.Length == 0 ? prepared :
            prepared with { JournalTurn = prepared.JournalTurn! with { Sent = wires } };
        var state = row.FoundationNullState ? null : row.State;
        if (turn.Expected is null)
        {
            if (turn.Journal is null || turn.Consumed is not [var key] || key != new InboxKey("root:" + root.Actor) ||
                turn.Done.Count != 0 || row.Suspended || change is null ||
                row.Status == InstanceStatus.Finished && (turn.Sent.Count != 0 || row.Jobs is not null)) throw RootShape();
            var creator = await new PostgresRootAdmission(create, TimeSpan.Zero).ReadPendingCreatorAsync(root.Tenant, root.Reference, cancellationToken);
            if (creator is null) return null;
            if (row.Model != creator.Start!.Model || row.Definition != creator.Start.Definition || row.History != creator.Start.History) throw RootShape();
            if (RootTimedJobPreviewEnabled) RequireRootTimedIssuance(turn, row);
            if (RootJobsEnabled) ValidateRootJobChange(null, row, [], turn.Sent, turn.FoundationRootJobCompletion, RootTimedJobs);
            return WithSends(PrepareFirstRoot(root.Tenant, root.Reference, creator, fence, state, row.StateVersion,
                change.TypeData, change.Labels, turn.Journal, jobState, row.Status, header.TerminalGrowth));
        }

        if (turn.Done.Count + turn.Consumed.Count == 0 || !InputsMatch(root, turn, RootJobsEnabled))
            throw RootShape();
        var current = await ReadFirstRootAsync(root.Tenant, root.Reference, cancellationToken);
        if (current?.Revision != turn.Expected) return null;
        if (row.Model != current.Model || row.Definition != current.Definition || row.History != current.History ||
            change is null && (header.Header.TypeData != current.TypeData || header.Header.Labels.Count != current.Labels.Count ||
                current.Labels.Any(pair => !header.Header.Labels.TryGetValue(pair.Key, out var value) || pair.Value != value))) throw RootShape();
        if (RootTimedJobSettlementEnabled && (!RootTimedJobPreviewEnabled || turn.Consumed.Any(key => key.Scope == -1 || key.Scope > 0)))
            return await PrepareRootTimedTurnAsync(root, turn, row, state, current, fence, wires, cancellationToken);
        await using var db = create();
        await using var pinned = await BeginPinnedRootOperationAsync(db, cancellationToken);
        var ids = turn.Done.Concat(turn.Consumed).Select(key => key.Scope > 0 ? StoredJobId(key) : key.Id).ToArray();
        var messages = RootSelfIssuer is null ? db.Messages : db.Messages.FromSqlRaw("""SELECT * FROM "FoundationRootMessages" """);
        var records = await messages.AsNoTracking().Where(m => m.Target == root.Actor && m.Partition == root.Partition &&
            (m.InboxScope == InboxKey.FoundationOccurrenceScope || m.InboxScope == RootSelfScope || RootJobsEnabled && !RootTimedJobPreviewEnabled && m.InboxScope == RootJobScope) && m.EventMatch == null && ids.Contains(m.Id) && !m.Done).OrderBy(m => m.Seq).ToListAsync(cancellationToken);
        var inputs = records.Select(record => DecodeRootInput(db, record, root.Reference, RootSelfIssuer, RootJobsEnabled)).ToArray();
        if (!SameInputs(inputs, turn.Done.Concat(turn.Consumed))) return null;
        if (turn.FoundationRootOperator is { } command)
        {
            if (inputs is not [var persisted] || persisted != command) return null;
            if (command.Kind == Kinds.Terminate)
            {
                if (!db.UsesRootTermination || !terminated || row.Suspended != current.Suspended ||
                    turn.Sent.Count != 0 || recalls is not null || current.Jobs is not null || row.Jobs is not null ||
                    row.StateVersion != current.StateVersion || turn.Journal is null) throw RootShape();
                return PrepareRootTurn(root.Tenant, root.Reference, current, inputs, fence, row.Status, state,
                    row.StateVersion, header.Header.TypeData, header.Header.Labels, turn.Journal, operatorTermination: true);
            }
            if (!db.UsesRootOperatorProtocol || row.Status != InstanceStatus.Running ||
                row.Suspended != (command.Kind == Kinds.Suspend) || row.StateVersion != current.StateVersion || state != current.State ||
                turn.Sent.Count != 0 || recalls is not null || turn.Journal is not null) throw RootShape();
            return PrepareRootTurn(root.Tenant, root.Reference, current, inputs, fence, row.Status, state,
                row.StateVersion, header.Header.TypeData, header.Header.Labels, null, operatorSuspension: row.Suspended, terminalGrowth: header.TerminalGrowth);
        }
        if (row.Suspended != current.Suspended || inputs.Any(input => input.Operator is not null)) throw RootShape();
        if (RootTimedJobPreviewEnabled)
            RequireRootTimedIssuance(turn, row);
        if (RootJobsEnabled) ValidateRootJobChange(current, row, inputs, turn.Sent, turn.FoundationRootJobCompletion, RootTimedJobs);
        return WithSends(PrepareRootTurn(root.Tenant, root.Reference, current, inputs, fence, row.Status, state,
            row.StateVersion, header.Header.TypeData, header.Header.Labels, turn.Journal, recalls?.Select(key => key.Id).ToArray(),
            jobState: jobState, jobCompletion: turn.FoundationRootJobCompletion, values: values, terminalGrowth: header.TerminalGrowth));
    }
}
