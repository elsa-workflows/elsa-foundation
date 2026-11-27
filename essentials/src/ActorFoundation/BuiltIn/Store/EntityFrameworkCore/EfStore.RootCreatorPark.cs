using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static string KeptRootCreator(KernelDb db, string row) => $"""
        ({row}."InboxScope" = 0 AND {row}."Kind" = '{Kinds.Start}' AND {row}."Id" = 'root:' || {row}."Target"
            {(db.UsesRootAuthoredEvents ? "" : $"AND {row}.\"Sender\" IS NOT NULL")}
            AND {row}."FoundationKeptBy" = {(short)ParkKeepReason.Consumed})
        """;

    private async Task<PreparedRoot?> PrepareRootCreatorParkAsync(WorkerRoot root, TurnCommit turn, PartitionFence fence,
        CancellationToken token)
    {
        var key = new InboxKey("root:" + root.Actor);
        if (turn.Row is not { Fault: { } fault } row || turn.FoundationHeader is not { } header || turn.Journal is null ||
            turn.FoundationKept is not { Count: 1 } kept || !kept.TryGetValue(key, out var reason) || reason != ParkKeepReason.Consumed ||
            turn.Done is not [var consumed] || consumed != key || turn.Consumed.Count != 0 || turn.Sent.Count != 0 ||
            turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 } ||
            turn != new TurnCommit(root.Actor, row, null, turn.Consumed, turn.Done, turn.Sent, turn.Subscribed, turn.Unsubscribed,
                turn.Journal, Unsubscribe: true, Held: turn.Held)
                { OpensPark = true, FoundationHeader = header, FoundationKept = kept, FoundationRootChildSends = turn.FoundationRootChildSends })
            throw RootShape();

        var creator = postgres
            ? await new PostgresRootAdmission(create, TimeSpan.Zero).ReadPendingCreatorAsync(root.Tenant, root.Reference, token)
            : await ReadSqlitePendingCreatorAsync(root.Tenant, root.Reference, token);
        if (creator is null) return null;
        var start = creator.Start!;
        var initial = new InstanceRow(root.Actor, start.Model, start.Definition, InstanceStatus.Faulted, "null", 1, start.Parent,
            Fault: fault, History: start.History, Creation: 1, StartIntent: root.Fingerprint,
            ParentCreation: start.ParentCreation) { FoundationNullState = true };
        if (row != initial || header.TypeData != start.Definition || header.Labels.Count != 0 ||
            postgres && _ordinaryRoots?.PublishesAuthoredEvents != true && (!creator.BindFoundationChild || start.Parent is null))
            throw RootShape();
        RootChildEffects? effects = null;
        if (creator.BindFoundationChild && start.Parent is not null)
        {
            if (turn.FoundationRootChildSends is not [var notice] || notice != FoundationChildNotice.Create(row, null, final: false))
                throw RootShape();
            effects = PrepareRootChildEffects(root, row, turn.FoundationRootChildSends) ?? throw RootShape();
        }
        else if (turn.FoundationRootChildSends is not null) throw RootShape();
        var jobs = postgres && RootJobsEnabled ? new RootJobState(null, 0) : null;
        var prepared = PrepareFirstRoot(root.Tenant, root.Reference, creator, fence, null, row.StateVersion, header.TypeData,
            header.Labels, turn.Journal, jobState: jobs, status: row.Status, creatorFault: fault);
        return WithRootChildEffects(prepared, effects, row.Parent);
    }

    private static async Task<bool> BindPostgresCreatorParkAsync(KernelDb db, FirstRootCheckpoint park, CancellationToken token)
    {
        var creator = park.Creator;
        if (creator.Start is not { } start) return false;
        var parent = start.Parent;
        var effects = park.ChildEffects;
        if (parent is null
            ? !db.UsesRootAuthoredEvents || creator.BindFoundationChild || effects is not null
            : !db.UsesRootChildren || !creator.BindFoundationChild || effects is null) return false;
        var fingerprint = CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload);
        if (fingerprint is null) return false;
        var family = RootStatePrefix(db);
        var retained = await db.Messages.FromSqlInterpolated(AddRootWire(db, $"""
            WITH kept AS (
                UPDATE "Messages" m SET "Done" = true, "Trial" = 0, "FoundationKeptBy" = {(short)ParkKeepReason.Consumed}
                FROM "FoundationRootBirths" b
                WHERE b."Actor" = m."Target" AND b."Partition" = m."Partition" AND b."Creator" = m."Id"
                    AND b."Tenant" = {park.Tenant} AND b."Reference" = {park.Reference} AND b."Creation" = 1
                    AND b."Fingerprint" = {fingerprint} AND m."Id" = {creator.InboxKey.Id} AND m."Target" = {creator.Target.Id}
                    AND m."InboxScope" = 0 AND (m."Due" = 0 OR
                        {parent}::text IS NULL AND m."Due" <= floor(extract(epoch from clock_timestamp()) * 1000)::bigint)
                    AND m."Sender" IS NOT DISTINCT FROM {parent}::text AND NOT m."Done"
                    AND m."KeepUntil" IS NULL AND m."ReplyTo" IS NOT DISTINCT FROM {creator.ReplyTo}::text
                    AND m."EventMatch" IS NULL AND m."FoundationKeptBy" IS NULL
                RETURNING m.*
            ), bound AS (
                UPDATE "Instances" i SET "FoundationReference" = {park.Reference}, "FoundationTenant" = {park.Tenant},
                    "FoundationHeader" = {park.Header}, "State" = {family} || i."State", /*root-wire*/
                    "FoundationParkAt" = floor(extract(epoch from clock_timestamp()) * 1000)::bigint,
                    "FoundationParkRevision" = {park.JournalRevision}
                FROM kept k
                WHERE i."Id" = k."Target" AND i."Partition" = k."Partition" AND i."Parent" IS NOT DISTINCT FROM {parent}::text
                    AND i."Revision" = {park.JournalRevision} AND i."Status" = {(int)InstanceStatus.Faulted} AND i."Fault" = {park.Row.Fault}
                    AND i."FoundationReference" IS NULL AND i."FoundationTerminalAt" IS NULL AND i."FoundationTerminalRevision" IS NULL
                RETURNING i."Id"
            )
            SELECT kept.* FROM kept JOIN bound ON bound."Id" = kept."Target"
            """, park.Wire)).AsNoTracking().ToListAsync(token);
        return retained is [var envelope] && park.MatchesCreator(PostgresRootAdmission.DecodeCreator(envelope, children: true)) &&
            (effects is null || await effects.BindAsync(db, park.Tenant, token));
    }
}
