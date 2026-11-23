using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed record RootTurnCheckpoint(string Tenant, string Reference, RootImage Current, InstanceStatus Status,
        string State, string Header, int StateVersion, Message[] Inputs, Func<string, string>? Issue) : QualifiedRootCheckpoint(checked(Current.Revision + 1))
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

            if (!await SettleRootInputsAsync(db, Reference, Current.Actor, Inputs.Select(input => input.InboxKey).ToArray(), Inputs, Issue, cancellationToken))
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
    private static async Task<bool> SettleRootInputsAsync(KernelDb db, string reference, CreationAddress actor, IReadOnlyList<InboxKey> expected,
        Message[]? expectedEnvelopes, Func<string, string>? issue, CancellationToken cancellationToken)
    {
        var home = new InstanceRef(actor.Id).Partition;
        var source = issue is null ? "\"Messages\"" : "\"FoundationRootMessages\"";
        var rows = await db.Messages.FromSqlRaw($$"""
            SELECT * FROM {{source}} WHERE "Target" = {0} AND "Partition" = {1}
                AND ("InboxScope" = {2} OR ("InboxScope" = -1 AND "Kind" = {3})) AND "EventMatch" IS NULL
                AND NOT "Done" AND "Due" <= floor(extract(epoch from clock_timestamp()) * 1000)::bigint
            ORDER BY "Seq" LIMIT {4} FOR UPDATE
            """, actor.Id, home, InboxKey.FoundationOccurrenceScope, RootSelfWire, expected.Count).AsNoTracking().ToListAsync(cancellationToken);
        var messages = rows.Select(row => row.InboxScope == -1 ? DecodeRootSelf(row, issue) : PostgresRootAdmission.DecodeDirect(row, reference)).ToArray();
        if (!SameInputs(messages, expected) || messages.Any(m => m.TargetCreation != actor.Creation) ||
            expectedEnvelopes is not null && !messages.SequenceEqual(expectedEnvelopes)) return false;

        var consumed = expected.Where(k => k.Scope == -1).Select(k => k.Id).ToArray();
        if (consumed.Length > 0) await QualifySelfWriterAsync(db, cancellationToken);
        if (consumed.Length > 0 && await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "Messages" WHERE "InboxScope" = -1 AND "Id" = ANY({consumed}) AND "Kind" = {RootSelfWire}
                 AND "Target" = {actor.Id} AND "Partition" = {home} AND NOT "Done" AND "EventMatch" IS NULL AND "KeepUntil" IS NULL
            """, cancellationToken) != consumed.Length) return false;
        var ids = expected.Where(k => k.Scope == InboxKey.FoundationOccurrenceScope).Select(k => k.Id).ToArray();
        if (ids.Length == 0) return true;
        var settled = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Messages" SET "Done" = true, "Payload" = NULL, "Start" = NULL
            WHERE "InboxScope" = {InboxKey.FoundationOccurrenceScope} AND "Id" = ANY({ids}) AND "EventMatch" IS NULL
                AND NOT "Done" AND "Target" = {actor.Id} AND "Partition" = {home} AND "KeepUntil" IS NOT NULL
            """, cancellationToken);
        return settled == ids.Length;
    }

    internal ValueTask<Committed> CommitRootTurnAsync(string tenant, string reference, RootImage current,
        IReadOnlyList<Message> inputs, PartitionFence fence, TimeSpan lease, InstanceStatus status, string? state, int stateVersion,
        string typeData, IReadOnlyDictionary<string, string> labels, string journal, CancellationToken cancellationToken = default) =>
        CommitPreparedRootAsync(fence, lease, PrepareRootTurn(tenant, reference, current, inputs, fence, status, state, stateVersion, typeData, labels, journal), cancellationToken);

    private PreparedRoot PrepareRootTurn(string tenant, string reference, RootImage current,
        IReadOnlyList<Message> inputs, PartitionFence fence, InstanceStatus status, string? state, int stateVersion,
        string typeData, IReadOnlyDictionary<string, string> labels, string? journal)
    {
        if (!postgres || Engine is not null) throw new NotSupportedException("Qualified root turns require PostgreSQL.");
        if (current is not { Revision: >= 1, Status: InstanceStatus.Running, Actor.Creation: 1 } ||
            new InstanceRef(current.Actor.Id).Partition != fence.Partition ||
            status is not (InstanceStatus.Running or InstanceStatus.Finished))
            throw new ArgumentException("Invalid running root or target disposition.", nameof(current));
        var accepted = inputs.ToArray();
        if (accepted.Length == 0 || accepted.Any(message => (message.InboxKey.Scope != InboxKey.FoundationOccurrenceScope && (RootSelfIssuer is null || !FoundationSelfSend.IsImmediate(message, current.Actor.Id, current.Actor.Creation))) ||
            message.InboxKey.Match is not null || message.Target.Id != current.Actor.Id || message.TargetCreation != current.Actor.Creation) ||
            accepted.Select(message => message.InboxKey).Distinct().Count() != accepted.Length)
            throw new ArgumentException("The turn requires distinct ordinary inputs for its exact actor creation.", nameof(inputs));
        var (stored, header) = EncodeRootContent(current.Model, current.Definition, state, stateVersion, typeData, labels, journal);
        var image = new InstanceRow(current.Actor.Id, "", "", status, "null", checked(current.Revision + 1), null)
            .WithFoundationState(stored);
        var root = new RootTurnCheckpoint(tenant, reference, current, status, PublicRootState + StoredState.Encode(image),
            header, stateVersion, accepted, RootSelfIssuer);
        var turn = new TurnCommit(image.Id, null, null, [], [], [], [], [], journal);
        return new(turn, root);
    }
}
