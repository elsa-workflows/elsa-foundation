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

            var keys = Inputs.Select(message => message.InboxKey.Id).ToArray();
            var rows = await db.Messages.FromSqlInterpolated($"""
                SELECT * FROM "Messages" WHERE "Target" = {Current.Actor.Id} AND "Partition" = {home}
                    AND "InboxScope" = {InboxKey.FoundationOccurrenceScope}
                    AND NOT "Done" AND "Due" <= floor(extract(epoch from clock_timestamp()) * 1000)::bigint
                ORDER BY "Seq" LIMIT {Inputs.Length} FOR UPDATE
                """).AsNoTracking().ToListAsync(cancellationToken);
            if (rows.Count != Inputs.Length || !rows.Select(PostgresRootAdmission.DecodeDirect).SequenceEqual(Inputs))
                return false;

            // Direct admission keeps a receipt and every accepted occurrence has a finite KeepUntil.
            // Settle the pending rows under their full physical keys and drop the completed payloads.
            var settled = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "Done" = true, "Payload" = NULL, "Start" = NULL
                WHERE "InboxScope" = {InboxKey.FoundationOccurrenceScope} AND "Id" = ANY({keys}) AND NOT "Done"
                    AND "Target" = {Current.Actor.Id} AND "Partition" = {home} AND "KeepUntil" IS NOT NULL
                """, cancellationToken);
            if (settled != Inputs.Length) return false;
            var finished = Status == InstanceStatus.Finished;
            var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "State" = {State}, "FoundationHeader" = {Header}, "StateVersion" = {StateVersion},
                    "Status" = {(int)Status}, "Revision" = {JournalRevision}, "FoundationWriteRevision" = {JournalRevision},
                    "FoundationTerminalRevision" = CASE WHEN {finished} THEN {JournalRevision} ELSE NULL END,
                    "FoundationTerminalAt" = CASE WHEN {finished} THEN floor(extract(epoch from clock_timestamp()) * 1000)::bigint ELSE NULL END
                WHERE "Id" = {Current.Actor.Id} AND "Partition" = {home} AND "FoundationTenant" = {Tenant}
                    AND "FoundationReference" = {Reference} AND "Revision" = {Current.Revision}
                """, cancellationToken);
            return updated == 1;
        }
    }

    internal ValueTask<Committed> CommitRootTurnAsync(string tenant, string reference, RootImage current,
        IReadOnlyList<Message> inputs, PartitionFence fence, TimeSpan lease, InstanceStatus status, string? state, int stateVersion,
        string typeData, IReadOnlyDictionary<string, string> labels, string journal, CancellationToken cancellationToken = default)
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
        return CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, lease, false, [turn], [], []), root, cancellationToken);
    }
}
