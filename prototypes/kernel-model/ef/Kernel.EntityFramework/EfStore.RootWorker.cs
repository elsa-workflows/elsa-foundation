using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private WorkerRoot? _ordinaryRoot;

    // This owner serves one generated root in the single-host preview.
    internal sealed record WorkerRoot(string Tenant, string Reference, string Actor, int Partition, string Fingerprint)
    {
        internal bool Owns(InboxKey key) => key.Match is null && (key.Scope == 0
            ? key.Id == "root:" + Actor
            : key.Scope == InboxKey.FoundationOccurrenceScope && key.Id.StartsWith(Reference + "/", StringComparison.Ordinal));
    }

    public bool CreationIdentityAvailable => _ordinaryRoot is not null;

    internal async Task<WorkerRoot> BindWorkerRootAsync(string tenant, string reference, CancellationToken cancellationToken = default)
    {
        if (!postgres || Engine is not null || _ordinaryRoot is not null)
            throw new InvalidOperationException("Bind a new PostgreSQL client to one generated root.");
        await using var db = create();
        var binding = await db.Database.SqlQuery<WorkerRoot>($"""
            SELECT "Tenant", "Reference", "Actor", "Partition", "Fingerprint" FROM "FoundationRootBirths"
            WHERE "Tenant" = {tenant} AND "Reference" = {reference} AND "Creation" = 1
            """).SingleAsync(cancellationToken);
        return _ordinaryRoot = binding;
    }

    private static InstanceRow WorkerRow(WorkerRoot root, RootImage image) =>
        new InstanceRow(image.Actor.Id, image.Model, image.Definition, image.Status, "null", image.Revision, null,
            History: image.History, StateVersion: image.StateVersion, Creation: image.Actor.Creation, StartIntent: root.Fingerprint)
            .WithFoundationState(image.State) with { NextJobGeneration = 0, FoundationDispatchHeader = new(root.Tenant, root.Reference, new(image.TypeData, image.Labels)) };

    private sealed record ScopedMarks(WorkerRoot Root, long Epoch, IReadOnlyList<InboxKey> Seen, IReadOnlyList<InboxKey> Trial,
        QualifiedRootCheckpoint? Checkpoint) : QualifiedRootCheckpoint(Checkpoint?.JournalRevision ?? 0)
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken)
        {
            if (!await Mark(Seen, false) || !await Mark(Trial, true)) return false;
            return Checkpoint is null || await Checkpoint.BindCheckpointAsync(db, cancellationToken);

            async Task<bool> Mark(IReadOnlyList<InboxKey> keys, bool trial)
            {
                foreach (var group in keys.GroupBy(key => key.Scope))
                {
                    var ids = group.Select(key => key.Id).ToArray();
                    var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE "Messages" SET "Seen" = {Epoch}, "Trial" = CASE WHEN {trial} THEN {Epoch} ELSE "Trial" END
                        WHERE "Target" = {Root.Actor} AND "Partition" = {Root.Partition} AND "InboxScope" = {group.Key}
                            AND "Id" = ANY({ids}) AND "EventMatch" IS NULL AND NOT "Done"
                        """, cancellationToken);
                    if (count != ids.Length) return false;
                }
                return true;
            }
        }
    }

    private ValueTask<Committed> CommitRootBatchAsync(CommitBatch batch, QualifiedRootCheckpoint? checkpoint, CancellationToken token)
    {
        if (batch.Seen.Count == 0 && batch.Trial.Count == 0) return CommitAsync(batch, checkpoint, token);
        var marks = new ScopedMarks(_ordinaryRoot!, batch.Epoch, batch.Seen, batch.Trial, checkpoint);
        return CommitAsync(batch with { Seen = [], Trial = [] }, marks, token);
    }

    private static NotSupportedException RootShape() => new("The PostgreSQL root owner cannot persist this batch without dropping effects.");

    private async ValueTask<Committed> CommitWorkerRootAsync(WorkerRoot root, CommitBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Fenced is not [var fence] || fence.Partition != root.Partition || batch.Partition != root.Partition ||
            batch.Epoch != fence.Epoch || batch.Position != fence.Position || batch.ClearJobTrials.Count != 0 ||
            batch.Window is not null || batch.PublicationReservation is not null || batch.DueBacklog is not null ||
            !batch.Seen.All(root.Owns) || !batch.Trial.All(root.Owns)) throw RootShape();

        if (batch.Turns.Count == 0) return await CommitRootBatchAsync(batch, null, cancellationToken);
        if (batch.Turns is not [var turn] || turn.Row is not { } row || turn.Instance != root.Actor || row.Id != root.Actor ||
            row.Creation != 1 || row.NextJobGeneration is not (null or 0) || row.StartIntent != root.Fingerprint || row.Revision != checked((turn.Expected ?? 0) + 1) ||
            row.Status is not (InstanceStatus.Running or InstanceStatus.Finished) ||
            row.FoundationDispatchHeader is not { } header || header.Tenant != root.Tenant || header.Reference != root.Reference ||
            turn.Journal is null || turn.Unsubscribe != (row.Status == InstanceStatus.Finished) ||
            turn.Sent.Count != 0 || turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 }) throw RootShape();

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

        var state = row.FoundationNullState ? null : row.State;
        if (turn.Expected is null)
        {
            if (turn.Consumed is not [var key] || key != new InboxKey("root:" + root.Actor) ||
                turn.Done.Count != 0 || row.Status != InstanceStatus.Running || change is null) throw RootShape();
            var creator = await new PostgresRootAdmission(create, TimeSpan.Zero).ReadPendingCreatorAsync(root.Tenant, root.Reference, cancellationToken);
            if (creator is null) return new(false, 0, []);
            if (row.Model != creator.Start!.Model || row.Definition != creator.Start.Definition || row.History != creator.Start.History) throw RootShape();
            return await WriteFirstRootAsync(root.Tenant, root.Reference, creator, fence, batch.Lease, state, row.StateVersion,
                change.TypeData, change.Labels, turn.Journal, cancellationToken, batch);
        }

        if (turn.Consumed.Count != 0 || turn.Done.Count == 0 || !turn.Done.All(key => root.Owns(key) && key.Scope == InboxKey.FoundationOccurrenceScope))
            throw RootShape();
        var current = await ReadFirstRootAsync(root.Tenant, root.Reference, cancellationToken);
        if (current?.Revision != turn.Expected) return new(false, 0, []);
        if (row.Model != current.Model || row.Definition != current.Definition || row.History != current.History ||
            change is null && (header.Header.TypeData != current.TypeData || header.Header.Labels.Count != current.Labels.Count ||
                current.Labels.Any(pair => !header.Header.Labels.TryGetValue(pair.Key, out var value) || pair.Value != value))) throw RootShape();
        await using var db = create();
        var ids = turn.Done.Select(key => key.Id).ToArray();
        var records = await db.Messages.AsNoTracking().Where(m => m.Target == root.Actor && m.InboxScope == InboxKey.FoundationOccurrenceScope &&
            ids.Contains(m.Id) && !m.Done).OrderBy(m => m.Seq).ToListAsync(cancellationToken);
        var inputs = records.Select(PostgresRootAdmission.DecodeDirect).ToArray();
        if (!inputs.Select(message => message.InboxKey).SequenceEqual(turn.Done)) return new(false, 0, []);
        return await WriteRootTurnAsync(root.Tenant, root.Reference, current, inputs, fence, batch.Lease, row.Status, state,
            row.StateVersion, header.Header.TypeData, header.Header.Labels, turn.Journal, cancellationToken, batch);
    }
}
