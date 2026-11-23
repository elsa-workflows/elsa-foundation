using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed record RootTurnCheckpoint(string Tenant, string Reference, RootImage Current, InstanceStatus Status,
        string State, string Header, int StateVersion, Message[] Inputs) : QualifiedRootCheckpoint(checked(Current.Revision + 1))
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken)
        {
            var home = new InstanceRef(Current.Actor.Id).Partition;
            var headers = await db.Database.SqlQuery<string>($"""
                SELECT i."FoundationHeader" AS "Value" FROM "Instances" i JOIN "FoundationRootBirths" b
                    ON b."Reference" = i."FoundationReference" AND b."Tenant" = i."FoundationTenant"
                    AND b."Actor" = i."Id" AND b."Partition" = i."Partition"
                WHERE b."Reference" = {Reference} AND b."Tenant" = {Tenant} AND b."Creation" = {Current.Actor.Creation}
                    AND i."Id" = {Current.Actor.Id} AND i."Partition" = {home} AND i."Revision" = {Current.Revision}
                    AND i."Status" = {(int)InstanceStatus.Running} AND NOT i."Retired"
                    AND i."FoundationTerminalAt" IS NULL AND i."FoundationTerminalRevision" IS NULL
                FOR UPDATE OF i
                """).ToListAsync(cancellationToken);
            if (headers.Count != 1) return false;
            var old = DurableKeyJson.Read<RootHeader>(headers[0]);
            if (old.Model != Current.Model || old.Definition != Current.Definition) return false;

            if (!await SettleDirectInputsAsync(db, Current.Actor, Inputs.Select(input => input.InboxKey).ToArray(), Inputs, cancellationToken))
                return false;
            var finished = Status == InstanceStatus.Finished;
            // This preview cannot create or discharge qualified waits. A terminal commit may fulfil
            // UnsubscribeAll here only when the exact root has no physical subscriptions.
            var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "State" = {State}, "FoundationHeader" = {Header}, "StateVersion" = {StateVersion},
                    "Status" = {(int)Status}, "Revision" = {JournalRevision}, "FoundationWriteRevision" = {JournalRevision},
                    "FoundationTerminalRevision" = CASE WHEN {finished} THEN {JournalRevision} ELSE NULL END,
                    "FoundationTerminalAt" = CASE WHEN {finished} THEN floor(extract(epoch from clock_timestamp()) * 1000)::bigint ELSE NULL END
                WHERE "Id" = {Current.Actor.Id} AND "Partition" = {home} AND "FoundationTenant" = {Tenant}
                    AND "FoundationReference" = {Reference} AND "Revision" = {Current.Revision}
                    AND (NOT {finished} OR NOT EXISTS (SELECT 1 FROM "Subscriptions"
                        WHERE "Instance" = {Current.Actor.Id}))
                """, cancellationToken);
            return updated == 1;
        }
    }

    // Both a model turn and a terminal no-turn disposition settle the same due FIFO prefix.
    private static async Task<bool> SettleDirectInputsAsync(KernelDb db, CreationAddress actor, IReadOnlyList<InboxKey> expected,
        Message[]? expectedEnvelopes, CancellationToken cancellationToken)
    {
        var home = new InstanceRef(actor.Id).Partition;
        var rows = await db.Messages.FromSqlInterpolated($"""
            SELECT * FROM "Messages" WHERE "Target" = {actor.Id} AND "Partition" = {home}
                AND "InboxScope" = {InboxKey.FoundationOccurrenceScope} AND "EventMatch" IS NULL
                AND NOT "Done" AND "Due" <= floor(extract(epoch from clock_timestamp()) * 1000)::bigint
            ORDER BY "Seq" LIMIT {expected.Count} FOR UPDATE
            """).AsNoTracking().ToListAsync(cancellationToken);
        var messages = rows.Select(PostgresRootAdmission.DecodeDirect).ToArray();
        if (!messages.Select(m => m.InboxKey).SequenceEqual(expected) || messages.Any(m => m.TargetCreation != actor.Creation) ||
            expectedEnvelopes is not null && !messages.SequenceEqual(expectedEnvelopes)) return false;

        var ids = expected.Select(key => key.Id).ToArray();
        var settled = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Messages" SET "Done" = true, "Payload" = NULL, "Start" = NULL
            WHERE "InboxScope" = {InboxKey.FoundationOccurrenceScope} AND "Id" = ANY({ids}) AND "EventMatch" IS NULL
                AND NOT "Done" AND "Target" = {actor.Id} AND "Partition" = {home} AND "KeepUntil" IS NOT NULL
            """, cancellationToken);
        return settled == expected.Count;
    }

    internal ValueTask<Committed> CommitRootTurnAsync(string tenant, string reference, RootImage current,
        IReadOnlyList<Message> inputs, PartitionFence fence, TimeSpan lease, InstanceStatus status, string? state, int stateVersion,
        string typeData, IReadOnlyDictionary<string, string> labels, string journal, CancellationToken cancellationToken = default) =>
        CommitPreparedRootAsync(fence, lease, PrepareRootTurn(tenant, reference, current, inputs, fence, status, state, stateVersion, typeData, labels, journal), cancellationToken);

    private PreparedRoot PrepareRootTurn(string tenant, string reference, RootImage current,
        IReadOnlyList<Message> inputs, PartitionFence fence, InstanceStatus status, string? state, int stateVersion,
        string typeData, IReadOnlyDictionary<string, string> labels, string journal)
    {
        if (!postgres || Engine is not null) throw new NotSupportedException("Qualified root turns require PostgreSQL.");
        if (current is not { Revision: >= 1, Status: InstanceStatus.Running, Actor.Creation: 1 } ||
            new InstanceRef(current.Actor.Id).Partition != fence.Partition ||
            status is not (InstanceStatus.Running or InstanceStatus.Finished))
            throw new ArgumentException("Invalid running root or target disposition.", nameof(current));
        var accepted = inputs.ToArray();
        if (accepted.Length == 0 || accepted.Any(message => message.InboxKey.Scope != InboxKey.FoundationOccurrenceScope ||
            message.InboxKey.Match is not null || message.Target.Id != current.Actor.Id || message.TargetCreation != current.Actor.Creation) ||
            accepted.Select(message => message.InboxKey).Distinct().Count() != accepted.Length)
            throw new ArgumentException("The turn requires distinct ordinary inputs for its exact actor creation.", nameof(inputs));
        var (stored, header) = EncodeRootContent(current.Model, current.Definition, state, stateVersion, typeData, labels, journal);
        var image = new InstanceRow(current.Actor.Id, "", "", status, "null", checked(current.Revision + 1), null)
            .WithFoundationState(stored);
        var root = new RootTurnCheckpoint(tenant, reference, current, status, PublicRootState + StoredState.Encode(image),
            header, stateVersion, accepted);
        var turn = new TurnCommit(image.Id, null, null, [], [], [], [], [], journal);
        return new(turn, root);
    }
}
