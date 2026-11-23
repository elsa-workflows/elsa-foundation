using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // The generic load treats this as an unknown state family and leaves its bytes intact.
    private const string PublicRootState = StoredState.Family + "4:public-root:";

    internal sealed record RootImage(CreationAddress Actor, string Model, string Definition, string TypeData,
        IReadOnlyDictionary<string, string> Labels, string? State, int StateVersion, long Revision)
    {
        internal History History { get; init; }
        internal InstanceStatus Status { get; init; }
        internal long? TerminalRevision { get; init; }
        internal long? TerminalAt { get; init; }
        internal long AsOf { get; init; }
    }

    private sealed record RootHeader(string Model, string Definition, string TypeData, KeyValuePair<string, string>[] Labels, bool EscapedState);

    private abstract record QualifiedRootCheckpoint(long JournalRevision)
    {
        internal abstract Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken);
    }

    private sealed record FirstRootCheckpoint(string Tenant, string Reference, Message Creator, string Header) : QualifiedRootCheckpoint(1)
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken cancellationToken)
        {
            // The existing commit already holds its partition fence. Claim one ready creator in that
            // transaction and compare its persisted envelope exactly. Earlier birth fingerprints
            // used the JSON serializer's replacement for isolated UTF-16 units.
            var candidates = await db.Messages.FromSqlInterpolated($"""
                SELECT m.* FROM "Messages" m JOIN "FoundationRootBirths" b
                    ON b."Creator" = m."Id" AND b."Actor" = m."Target" AND b."Partition" = m."Partition"
                WHERE b."Reference" = {Reference} AND b."Tenant" = {Tenant} AND b."Creation" = 1
                    AND m."Id" = {Creator.InboxKey.Id} AND m."InboxScope" = 0 AND NOT m."Done"
                    AND m."Due" <= floor(extract(epoch from clock_timestamp()) * 1000)::bigint FOR UPDATE OF m
                """).AsNoTracking().ToListAsync(cancellationToken);
            if (candidates.Count != 1) return false;
            var admitted = PostgresRootAdmission.DecodeCreator(candidates[0]);
            if (admitted.Id != Creator.Id || admitted.Target != Creator.Target || admitted.InboxKey != Creator.InboxKey ||
                admitted.Start != Creator.Start || admitted.Payload != Creator.Payload || admitted.Due != Creator.Due ||
                admitted.KeepUntil != Creator.KeepUntil || admitted.ReplyTo != Creator.ReplyTo ||
                admitted.SenderCreation != Creator.SenderCreation) return false;
            var consumed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "Messages" WHERE "Id" = {Creator.InboxKey.Id} AND "InboxScope" = 0 AND NOT "Done"
                """, cancellationToken);
            if (consumed != 1) return false;

            var fingerprint = CreationProtocol.Fingerprint(Creator.Start, Creator.Kind, Creator.Payload);
            var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" i SET "FoundationReference" = b."Reference", "FoundationTenant" = b."Tenant",
                    "FoundationHeader" = {Header}, "State" = {PublicRootState} || i."State"
                FROM "FoundationRootBirths" b WHERE b."Reference" = {Reference} AND b."Tenant" = {Tenant}
                    AND b."Actor" = {Creator.Target.Id} AND b."Creation" = 1 AND b."Creator" = {Creator.InboxKey.Id}
                    AND b."Fingerprint" = {fingerprint} AND i."Id" = b."Actor" AND i."Partition" = b."Partition"
                    AND i."Revision" = 1 AND i."FoundationReference" IS NULL
                """, cancellationToken);
            return affected == 1;
        }
    }

    internal static Task InstallFirstRootCheckpointsAsync(KernelDb db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Instances" ADD COLUMN IF NOT EXISTS "FoundationReference" text COLLATE "C" REFERENCES "FoundationRootBirths" ("Reference"),
                                  ADD COLUMN IF NOT EXISTS "FoundationTenant" text COLLATE "C",
                                  ADD COLUMN IF NOT EXISTS "FoundationHeader" text,
                                  ADD COLUMN IF NOT EXISTS "FoundationWriteRevision" bigint,
                                  ADD COLUMN IF NOT EXISTS "FoundationTerminalRevision" bigint,
                                  ADD COLUMN IF NOT EXISTS "FoundationTerminalAt" bigint;
            CREATE OR REPLACE FUNCTION kernel_require_root_checkpoint_writer() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                IF TG_OP = 'UPDATE' AND NEW."FoundationWriteRevision" = OLD."Revision" + 1
                    AND NEW."Revision" = NEW."FoundationWriteRevision" AND OLD."Status" = 0
                    AND NEW."FoundationReference" = OLD."FoundationReference" AND NEW."FoundationTenant" = OLD."FoundationTenant"
                    AND NEW."Id" = OLD."Id" AND NEW."Partition" = OLD."Partition" AND NOT NEW."Retired"
                THEN RETURN NEW; END IF;
                RAISE EXCEPTION 'Qualified actor checkpoint requires a qualified update' USING ERRCODE = '23514';
            END
            $fn$;
            CREATE OR REPLACE TRIGGER kernel_require_root_checkpoint_writer BEFORE UPDATE OR DELETE ON "Instances"
            FOR EACH ROW WHEN (OLD."FoundationReference" IS NOT NULL) EXECUTE FUNCTION kernel_require_root_checkpoint_writer();
            CREATE OR REPLACE FUNCTION kernel_require_root_journal_revision() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                IF EXISTS (SELECT 1 FROM "Instances" i WHERE i."Id" = NEW."Instance" AND i."FoundationReference" IS NOT NULL
                    AND (i."Partition" <> NEW."Partition" OR NEW."Revision" <> i."Revision" + 1 OR i."Status" <> 0))
                THEN RAISE EXCEPTION 'Qualified actor journal requires the next running checkpoint revision' USING ERRCODE = '23514'; END IF;
                RETURN NEW;
            END
            $fn$;
            CREATE OR REPLACE TRIGGER kernel_require_root_journal_revision BEFORE INSERT ON "Journal"
            FOR EACH ROW EXECUTE FUNCTION kernel_require_root_journal_revision();
            """, cancellationToken);

    /// <summary>The first Running checkpoint of a generated root, with its creator as the sole input.
    /// Later actor revisions and turns with effects require their own qualified paths.</summary>
    internal ValueTask<Committed> CommitFirstRootAsync(string tenant, string reference, Message creator,
        PartitionFence fence, TimeSpan lease, string? state, int stateVersion, string typeData,
        IReadOnlyDictionary<string, string> labels, string journal, CancellationToken cancellationToken = default) =>
        WriteFirstRootAsync(tenant, reference, creator, fence, lease, state, stateVersion, typeData, labels, journal, cancellationToken, null);

    private ValueTask<Committed> WriteFirstRootAsync(string tenant, string reference, Message creator,
        PartitionFence fence, TimeSpan lease, string? state, int stateVersion, string typeData,
        IReadOnlyDictionary<string, string> labels, string journal, CancellationToken cancellationToken, CommitBatch? workerBatch)
    {
        if (!postgres || Engine is not null) throw new NotSupportedException("First root checkpoints require PostgreSQL.");
        if (creator.Start is not { Parent: null, Creation: { Generation: 1, Previous: 0 } } start ||
            creator.TargetCreation != 1 || creator.Kind != Kinds.Start || creator.Target.Partition != fence.Partition ||
            creator.InboxKey != new InboxKey("root:" + creator.Target.Id) || creator.Payload != start.Input || creator.Sender is not null ||
            start.Creation.Intent != FoundationRootCreator.OperationDigest(creator.Id))
            throw new ArgumentException("First root checkpoint requires the admitted exact creator.", nameof(creator));
        var (storedState, header) = EncodeRootContent(start.Model, start.Definition, state, stateVersion, typeData, labels, journal);
        // Model and definition are in the qualified header, including values that PostgreSQL text cannot carry.
        var row = new InstanceRow(creator.Target.Id, "", "", InstanceStatus.Running, "null", 1, null,
            History: start.History, StateVersion: stateVersion).WithFoundationState(storedState);
        // BindCheckpointAsync consumes the exact, due creator before this same transaction lands.
        var turn = new TurnCommit(row.Id, row, null, [], [], [], [], [], journal);
        var batch = new CommitBatch(fence.Partition, fence.Epoch, fence.Position, lease, false, [turn], [], []);
        return CommitRootBatchAsync(workerBatch is null ? batch : workerBatch with { Turns = [turn] }, new FirstRootCheckpoint(tenant, reference, creator, header), cancellationToken);
    }

    private static (string? State, string Header) EncodeRootContent(string model, string definition, string? state,
        int stateVersion, string typeData, IReadOnlyDictionary<string, string> labels, string? journal)
    {
        if (stateVersion < 1 || !FoundationMetadataLimits.FitsContent(typeData, FoundationMetadataLimits.TypeDataBytes))
            throw new ArgumentException("Invalid qualified root checkpoint header.");

        var captured = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in labels)
        {
            if (captured.Count == 4 || key is null || value is null || Encoding.UTF8.GetByteCount(key) > 1024 ||
                Encoding.UTF8.GetByteCount(value) > 1024 || !captured.TryAdd(key, value))
                throw new ArgumentException("A root checkpoint accepts four ordinally distinct labels of at most 1,024 UTF-8 bytes per key and value.", nameof(labels));
        }

        if (journal is not null)
        {
            using var entries = JsonDocument.Parse(journal);
            if (entries.RootElement.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Qualified root checkpoint journal entries must form an array.", nameof(journal));
        }

        var escaped = state is not null && (state.Contains('\0') || DurableKeyJson.HasUnpairedSurrogate(state));
        var storedState = escaped ? DurableKeyJson.Write(state) : state;
        var header = DurableKeyJson.Write(new RootHeader(model, definition, typeData, captured.ToArray(), escaped));
        if (Encoding.UTF8.GetByteCount(header) + (storedState is null ? 0L : Encoding.UTF8.GetByteCount(storedState)) >
            16 * 1024 * 1024 - 24 * 1024)
            throw new ArgumentException("The encoded PostgreSQL checkpoint exceeds its storage budget.");
        return (storedState, header);
    }

    private sealed record PhysicalRoot(string Id, long Creation, string State, int StateVersion, long Revision, string Header,
        History History, InstanceStatus Status, long? TerminalRevision, long? TerminalAt, long AsOf);

    internal async Task<RootImage?> ReadFirstRootAsync(string tenant, string reference, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        var physical = await db.Database.SqlQuery<PhysicalRoot>($"""
            SELECT i."Id", b."Creation", i."State", i."StateVersion", i."Revision", i."FoundationHeader" AS "Header",
                i."History", i."Status", i."FoundationTerminalRevision" AS "TerminalRevision", i."FoundationTerminalAt" AS "TerminalAt",
                floor(extract(epoch from statement_timestamp()) * 1000)::bigint AS "AsOf"
            FROM "Instances" i JOIN "FoundationRootBirths" b ON b."Reference" = i."FoundationReference"
                AND b."Actor" = i."Id" AND b."Tenant" = i."FoundationTenant"
            WHERE i."FoundationTenant" = {tenant} AND i."FoundationReference" = {reference} AND i."Partition" = b."Partition" AND NOT i."Retired"
            """).SingleOrDefaultAsync(cancellationToken);
        if (physical is null) return null;
        if (!physical.State.StartsWith(PublicRootState, StringComparison.Ordinal))
            throw new StoreException(Failure.Definite, "Unsupported qualified actor checkpoint.");
        var header = DurableKeyJson.Read<RootHeader>(physical.Header);
        var logical = StoredState.Restore(new(physical.Id, header.Model, header.Definition, InstanceStatus.Running,
            physical.State[PublicRootState.Length..], physical.Revision, null, Creation: physical.Creation));
        var state = logical.FoundationNullState ? null : header.EscapedState ? DurableKeyJson.Read<string>(logical.State) : logical.State;
        return new(new(physical.Id, physical.Creation), header.Model, header.Definition, header.TypeData,
            header.Labels.ToDictionary(StringComparer.Ordinal), state, physical.StateVersion, physical.Revision)
            {
                History = physical.History, Status = physical.Status, TerminalRevision = physical.TerminalRevision,
                TerminalAt = physical.TerminalAt, AsOf = physical.AsOf
            };
    }
}
