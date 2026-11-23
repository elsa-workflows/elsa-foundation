using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private WorkerRoots? _ordinaryRoots;

    // An Embedded owner serves generated roots in one partition, optionally restricted to an explicit set.
    internal sealed record WorkerRoot(string Tenant, string Reference, string Actor, int Partition, string Fingerprint)
    {
        internal bool Owns(InboxKey key) => key.Match is null && key.Scope switch
        {
            0 => key.Id == "root:" + Actor,
            -1 => SelfActor(key) == Actor,
            InboxKey.FoundationOccurrenceScope => key.Id.StartsWith(Reference + "/", StringComparison.Ordinal),
            _ => false
        };
    }

    internal sealed class WorkerRoots(int partition, IReadOnlyList<WorkerRoot> roots, bool discover = false)
    {
        internal int Partition { get; } = partition;
        internal bool Discover { get; } = discover;
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

    private static (string Name, bool Creator) InputBinding(InboxKey key)
    {
        if (key.Match is null && key.Scope == 0 && key.Id.StartsWith("root:", StringComparison.Ordinal)) return (key.Id[5..], true);
        if (SelfActor(key) is { } self) return (self, true);
        var split = key.Id.IndexOf('/');
        if (key.Match is null && key.Scope == InboxKey.FoundationOccurrenceScope && split > 0) return (key.Id[..split], false);
        throw RootShape();
    }

    internal async Task<(WorkerRoots Scope, int Count)> BindWorkerPartitionAsync(int partition, CancellationToken token)
    {
        if (!postgres || Engine is not null || partition < 0 || await PartitionCountAsync(token) is not { } count || partition >= count)
            throw new InvalidOperationException("A generated-root owner requires an existing PostgreSQL partition.");
        return (InstallWorkerScope(new(partition, [], discover: true)), count);
    }

    private WorkerRoots InstallWorkerScope(WorkerRoots scope) =>
        Interlocked.CompareExchange(ref _ordinaryRoots, scope, null) is null ? scope : throw new InvalidOperationException("The PostgreSQL owner is already bound.");

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
        if (actorIds.Count + references.Count == 0) return new(scope.Partition, []);
        var ids = actorIds.ToArray();
        var refs = references.ToArray();
        await using var db = create();
        var found = await db.Database.SqlQuery<WorkerRoot>($"""
            SELECT "Tenant", "Reference", "Actor", "Partition", "Fingerprint" FROM "FoundationRootBirths"
            WHERE "Partition" = {scope.Partition} AND "Creation" = 1 AND ("Actor" = ANY({ids}) OR "Reference" = ANY({refs}))
            """).ToListAsync(token);
        if (found.Any(root => new InstanceRef(root.Actor).Partition != scope.Partition)) throw RootShape();
        return new(scope.Partition, found);
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
        var bindings = await db.Database.SqlQuery<WorkerRoot>($"""
            SELECT "Tenant", "Reference", "Actor", "Partition", "Fingerprint" FROM "FoundationRootBirths"
            WHERE ("Tenant", "Reference") IN (SELECT * FROM unnest({tenants}::text[], {references}::text[])) AND "Creation" = 1
            """).ToListAsync(cancellationToken);
        if (bindings.Count != requested.Count || !requested.ToHashSet().SetEquals(bindings.Select(r => (r.Tenant, r.Reference))) ||
            bindings.Any(r => r.Partition != bindings[0].Partition || new InstanceRef(r.Actor).Partition != r.Partition))
            throw new InvalidOperationException("The supplied roots must resolve exactly in their tenant and share one partition.");
        return InstallWorkerScope(new(bindings[0].Partition, bindings));
    }

    private static InstanceRow WorkerRow(WorkerRoot root, RootImage image) =>
        new InstanceRow(image.Actor.Id, image.Model, image.Definition, image.Status, "null", image.Revision, null,
            History: image.History, StateVersion: image.StateVersion, Creation: image.Actor.Creation, StartIntent: root.Fingerprint)
            .WithFoundationState(image.State) with { NextJobGeneration = 0, FoundationDispatchHeader = new(root.Tenant, root.Reference, new(image.TypeData, image.Labels)) };

    private sealed record ScopedMarks(WorkerRoot Root, long Epoch, IReadOnlyList<InboxKey> Seen, IReadOnlyList<InboxKey> Trial) : QualifiedRootCheckpoint(0)
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken)
        {
            return await Mark(Seen, false) && await Mark(Trial, true);

            async Task<bool> Mark(IReadOnlyList<InboxKey> keys, bool trial)
            {
                foreach (var group in keys.GroupBy(key => key.Scope))
                {
                    var ids = group.Select(key => key.Id).ToArray();
                    var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE "Messages" SET "Seen" = {Epoch}, "Trial" = CASE WHEN {trial} THEN {Epoch} ELSE "Trial" END
                        WHERE "Target" = {Root.Actor} AND "Partition" = {Root.Partition} AND "InboxScope" = {(group.Key == -1 ? RootSelfScope : group.Key)}
                            AND "Id" = ANY({ids}) AND "EventMatch" IS NULL AND NOT "Done"
                        """, cancellationToken);
                    if (count != ids.Length) return false;
                }
                return true;
            }
        }
    }

    private sealed record FinishedRootInputs(WorkerRoot Root, IReadOnlyList<InboxKey> Keys, Func<string, string>? Issue) : QualifiedRootCheckpoint(0)
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken)
        {
            var terminal = await db.Database.SqlQuery<long>($"""
                SELECT i."FoundationTerminalRevision" AS "Value" FROM "Instances" i
                JOIN "FoundationRootBirths" b ON b."Actor" = i."Id" AND b."Partition" = i."Partition"
                    AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference"
                WHERE b."Tenant" = {Root.Tenant} AND b."Reference" = {Root.Reference} AND b."Actor" = {Root.Actor}
                    AND b."Creation" = 1 AND i."Partition" = {Root.Partition} AND NOT i."Retired"
                    AND i."Status" = {(int)InstanceStatus.Finished} AND i."FoundationTerminalAt" IS NOT NULL
                    AND i."FoundationTerminalRevision" = i."Revision" FOR SHARE OF i
                """).ToListAsync(cancellationToken);
            return terminal.Count == 1 && await SettleRootInputsAsync(db, Root.Reference, new(Root.Actor, 1), Keys, null, Issue, cancellationToken);
        }
    }

    private sealed record PreparedRoot(TurnCommit? JournalTurn, QualifiedRootCheckpoint Checkpoint);

    private ValueTask<Committed> CommitPreparedRootAsync(PartitionFence fence, TimeSpan lease, PreparedRoot prepared, CancellationToken token) =>
        CommitAsync(new CommitBatch(fence.Partition, fence.Epoch, fence.Position, lease, false, [prepared.JournalTurn!], [], []),
            prepared.Checkpoint, token);

    private sealed record RootCheckpoints(IReadOnlyList<QualifiedRootCheckpoint> Commands, IReadOnlyDictionary<string, long> Revisions)
        : QualifiedRootCheckpoint(0)
    {
        internal override long RevisionFor(string actor) => Revisions[actor];
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken)
        {
            foreach (var command in Commands)
                if (!await command.BindCheckpointAsync(db, cancellationToken)) return false;
            return true;
        }
    }

    private static NotSupportedException RootShape() => new("The PostgreSQL root owner cannot persist this batch without dropping effects.");

    private async ValueTask<Committed> CommitWorkerRootAsync(WorkerRoots roots, CommitBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Fenced is not [var fence] || fence.Partition != roots.Partition || batch.Partition != roots.Partition ||
            batch.Epoch != fence.Epoch || batch.Position != fence.Position || batch.ClearJobTrials.Count != 0 ||
            batch.Window is not null || batch.PublicationReservation is not null || batch.DueBacklog is not null ||
            batch.Turns.Select(t => t.Instance).Distinct().Count() != batch.Turns.Count) throw RootShape();

        roots = await ResolveWorkerRootsAsync(roots, batch.Turns.Select(t => t.Instance), batch.Seen.Concat(batch.Trial), cancellationToken);
        var commands = new List<QualifiedRootCheckpoint>();
        var turns = new List<TurnCommit>();
        var revisions = new Dictionary<string, long>(StringComparer.Ordinal);
        var seen = batch.Seen.ToLookup(roots.ForInput);
        var trials = batch.Trial.ToLookup(roots.ForInput);
        foreach (var root in seen.Select(g => g.Key).Union(trials.Select(g => g.Key)))
            commands.Add(new ScopedMarks(root, batch.Epoch, seen[root].ToArray(), trials[root].ToArray()));
        foreach (var turn in batch.Turns)
        {
            var prepared = await PrepareWorkerTurnAsync(roots.ForActor(turn.Instance), turn, fence, cancellationToken);
            if (prepared is null) return new(false, 0, []);
            commands.Add(prepared.Checkpoint);
            if (prepared.JournalTurn is { } journal)
            {
                turns.Add(journal);
                revisions.Add(journal.Instance, prepared.Checkpoint.JournalRevision);
            }
        }
        return await CommitAsync(batch with { Turns = turns, Seen = [], Trial = [] }, new RootCheckpoints(commands, revisions), cancellationToken);
    }

    private static bool InputsMatch(WorkerRoot root, TurnCommit turn) =>
        turn.Done.All(k => k.Scope == InboxKey.FoundationOccurrenceScope && root.Owns(k)) &&
        turn.Consumed.All(k => k.Scope == -1 && root.Owns(k));

    private static bool SameInputs(IEnumerable<Message> inputs, IEnumerable<InboxKey> expected)
    {
        var keys = inputs.Select(m => m.InboxKey).ToArray();
        return keys.Where(k => k.Scope == -1).SequenceEqual(expected.Where(k => k.Scope == -1)) &&
            keys.Where(k => k.Scope == InboxKey.FoundationOccurrenceScope).SequenceEqual(expected.Where(k => k.Scope == InboxKey.FoundationOccurrenceScope));
    }

    private async Task<PreparedRoot?> PrepareWorkerTurnAsync(WorkerRoot root, TurnCommit turn, PartitionFence fence, CancellationToken cancellationToken)
    {
        if (turn.Row is null)
        {
            var ended = new TurnCommit(root.Actor, null, null, turn.Consumed, turn.Done, turn.Sent, turn.Subscribed,
                turn.Unsubscribed, null, Held: turn.Held);
            if (turn != ended || turn.Done.Count + turn.Consumed.Count == 0 || turn.Sent.Count != 0 ||
                turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 } ||
                !InputsMatch(root, turn)) throw RootShape();
            return new(null, new FinishedRootInputs(root, [.. turn.Done, .. turn.Consumed], RootSelfIssuer));
        }
        var row = turn.Row;
        if (row.Id != root.Actor ||
            row.Creation != 1 || row.NextJobGeneration is not (null or 0) || row.StartIntent != root.Fingerprint || row.Revision != checked((turn.Expected ?? 0) + 1) ||
            row.Status is not (InstanceStatus.Running or InstanceStatus.Finished) ||
            row.FoundationDispatchHeader is not { } header || header.Tenant != root.Tenant || header.Reference != root.Reference ||
            turn.Unsubscribe != (row.Status == InstanceStatus.Finished) ||
            turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 }) throw RootShape();

        // Record equality checks the full shape, including extension fields. Adding a new durable effect
        // fails closed here until the qualified SQL writer can represent it.
        var supported = new TurnCommit(turn.Instance, row, turn.Expected, turn.Consumed, turn.Done, turn.Sent,
            turn.Subscribed, turn.Unsubscribed, turn.Journal, turn.Unsubscribe, turn.Held) { FoundationHeader = turn.FoundationHeader };
        var supportedRow = new InstanceRow(row.Id, row.Model, row.Definition, row.Status, row.State, row.Revision, null,
            History: row.History, StateVersion: row.StateVersion, Creation: 1, StartIntent: root.Fingerprint)
            { NextJobGeneration = row.NextJobGeneration, FoundationNullState = row.FoundationNullState, FoundationDispatchHeader = header };
        if (turn != supported || row != supportedRow) throw RootShape();
        var change = turn.FoundationHeader;
        if (change is not null && (change.TypeData != header.Header.TypeData || change.Labels.Count != header.Header.Labels.Count ||
            change.Labels.Any(pair => !header.Header.Labels.TryGetValue(pair.Key, out var value) || pair.Value != value))) throw RootShape();

        var wires = turn.Sent.Select((message, index) => EncodeRootSelf(root, row.Revision, index, message)).ToArray();
        PreparedRoot WithSends(PreparedRoot prepared) => wires.Length == 0 ? prepared :
            prepared with { JournalTurn = prepared.JournalTurn! with { Sent = wires } };
        var state = row.FoundationNullState ? null : row.State;
        if (turn.Expected is null)
        {
            if (turn.Consumed is not [var key] || key != new InboxKey("root:" + root.Actor) ||
                turn.Journal is null || turn.Done.Count != 0 || row.Status != InstanceStatus.Running || change is null) throw RootShape();
            var creator = await new PostgresRootAdmission(create, TimeSpan.Zero).ReadPendingCreatorAsync(root.Tenant, root.Reference, cancellationToken);
            if (creator is null) return null;
            if (row.Model != creator.Start!.Model || row.Definition != creator.Start.Definition || row.History != creator.Start.History) throw RootShape();
            return WithSends(PrepareFirstRoot(root.Tenant, root.Reference, creator, fence, state, row.StateVersion,
                change.TypeData, change.Labels, turn.Journal));
        }

        if (turn.Done.Count + turn.Consumed.Count == 0 || !InputsMatch(root, turn))
            throw RootShape();
        var current = await ReadFirstRootAsync(root.Tenant, root.Reference, cancellationToken);
        if (current?.Revision != turn.Expected) return null;
        if (row.Model != current.Model || row.Definition != current.Definition || row.History != current.History ||
            change is null && (header.Header.TypeData != current.TypeData || header.Header.Labels.Count != current.Labels.Count ||
                current.Labels.Any(pair => !header.Header.Labels.TryGetValue(pair.Key, out var value) || pair.Value != value))) throw RootShape();
        await using var db = create();
        var ids = turn.Done.Concat(turn.Consumed).Select(key => key.Id).ToArray();
        var messages = RootSelfIssuer is null ? db.Messages : db.Messages.FromSqlRaw("""SELECT * FROM "FoundationRootMessages" """);
        var records = await messages.AsNoTracking().Where(m => m.Target == root.Actor && m.Partition == root.Partition &&
            (m.InboxScope == InboxKey.FoundationOccurrenceScope || m.InboxScope == RootSelfScope) && m.EventMatch == null && ids.Contains(m.Id) && !m.Done).OrderBy(m => m.Seq).ToListAsync(cancellationToken);
        var inputs = records.Select(row => row.InboxScope == RootSelfScope ? DecodeRootSelf(row, RootSelfIssuer) : PostgresRootAdmission.DecodeDirect(row, root.Reference)).ToArray();
        if (!SameInputs(inputs, turn.Done.Concat(turn.Consumed))) return null;
        return WithSends(PrepareRootTurn(root.Tenant, root.Reference, current, inputs, fence, row.Status, state,
            row.StateVersion, header.Header.TypeData, header.Header.Labels, turn.Journal));
    }
}
