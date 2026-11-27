using Kernel.Foundation.Hosting;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed record InlineRootSource(byte[] Key, (uint Marker, uint Namespace, long Table)? Postgres,
        Func<FormattableString, FormattableString>? Bind);

    // A sealed Running checkpoint can be promoted with its first subscriptions. Additional
    // effects need the existing qualified lowering before enabling this entry point from Worker.
    internal async ValueTask<Committed> PromoteInlineRootAsync(Contract.BoundScope scope, Message creator, TurnCommit turn, PartitionFence fence,
        TimeSpan lease, TimeSpan receiptWindow, ProtectRootStartAsync protect, CancellationToken token)
    {
        if (!OwnedCommit || creator.Start is not { Ephemeral: true, Parent: null } spec ||
            creator.FoundationRootBinding is not { } binding || binding.Actor != creator.Target.Id || binding.Tenant != scope.Tenant ||
            !RootPreparedStarts.IsInlineOperation(creator.Id) ||
            creator.Due is not null || creator.ReplyTo is not null || creator.KeepUntil is not null ||
            turn is not { Expected: null, Journal: not null, FoundationHeader: { } header,
                Row: { Status: InstanceStatus.Running, Revision: > 0, FoundationInlineBirth: { } birth } row } ||
            turn.Instance != binding.Actor || birth != (binding.Tenant, creator.InboxKey) ||
            receiptWindow <= TimeSpan.Zero)
            throw RootShape();
        await using (var profile = create())
            if (row.Revision != 1 && !profile.UsesRootInlinePromotion) throw RootShape();

        var fingerprint = CreationProtocol.Fingerprint(spec, creator.Kind, creator.Payload)!;
        var supported = new TurnCommit(row.Id, row, null, turn.Consumed, turn.Done, turn.Sent,
            turn.Subscribed, turn.Unsubscribed, turn.Journal, Held: turn.Held) { FoundationHeader = header };
        var expected = new InstanceRow(binding.Actor, spec.Model, spec.Definition, InstanceStatus.Running,
            row.State, row.Revision, Parent: null, History: spec.History, StateVersion: row.StateVersion, Creation: 1, StartIntent: fingerprint)
        {
            FoundationInlineBirth = birth, FoundationInlineHeader = row.FoundationInlineHeader,
            FoundationNullState = row.FoundationNullState, FoundationDispatchHeader = row.FoundationDispatchHeader,
            NextJobGeneration = row.NextJobGeneration
        };
        if (turn != supported || row != expected || row.NextJobGeneration is not (null or 0) ||
            turn.Consumed.Count + turn.Done.Count + turn.Sent.Count +
            turn.Unsubscribed.Count + (turn.Held?.Count ?? 0) != 0 ||
            row.FoundationDispatchHeader is not { } dispatch || dispatch.Tenant != binding.Tenant ||
            dispatch.Reference != binding.Reference || dispatch.Header.TypeData != header.TypeData ||
            dispatch.Header.Labels.Count != header.Labels.Count ||
            dispatch.Header.Labels.Any(label => header.Labels.GetValueOrDefault(label.Key) != label.Value))
            throw RootShape();

        var first = PrepareFirstRoot(binding.Tenant, binding.Reference, creator, fence,
            row.FoundationNullState ? null : row.State, row.StateVersion, header.TypeData, header.Labels, turn.Journal,
            jobState: row.NextJobGeneration is null ? null : new RootJobState(null, row.NextJobGeneration),
            terminalGrowth: dispatch.TerminalGrowth, revision: row.Revision);
        var root = new WorkerRoot(binding.Tenant, binding.Reference, binding.Actor, fence.Partition, fingerprint);
        var journal = RootEventWaits.Lower(root, turn, first.JournalTurn!);
        PreparedRootStart stage;
        PendingRootStart offer;
        (uint Marker, uint Namespace, long Table)? source;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(RootPreparedStarts.OneWayBudget);
        try
        {
            (stage, offer, source) = await PrepareAsync(budget.Token);
            if (!await RootPreparedStarts.ProtectAsync(protect, offer, budget.Token))
            {
                await DisarmAsync(stage, offer.Issuance, source, budget.Token);
                return new(false, 0, []);
            }
            budget.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && budget.IsCancellationRequested)
        {
            return new(false, 0, []);
        }

        var checkpoint = new InlineRootCheckpoint((FirstRootCheckpoint)first.Checkpoint, scope, stage, offer.Issuance,
            source, (long)receiptWindow.TotalMilliseconds, checked(fence.Position + 1), turn.Journal);
        var sentAt = System.Diagnostics.Stopwatch.GetTimestamp();
        return await CommitAsync(new CommitBatch(fence.Partition, fence.Epoch, fence.Position, lease, false, [journal], [], []),
            checkpoint, sentAt, token);

        async Task<(PreparedRootStart, PendingRootStart, (uint Marker, uint Namespace, long Table)?)> PrepareAsync(CancellationToken ct)
        {
            await using var db = create();
            await using var transaction = await (Engine?.BeginAsync(db, ct) ?? db.Database.BeginTransactionAsync(ct));
            var origin = await PinInlineRootSourceAsync(db, ct);
            var deadline = RootPreparedStarts.OneWayDeadline(await ReadInlineClockAsync(db, ct));
            var preparation = new PreparedRootStart(binding.Tenant, binding.Reference, binding.Actor,
                fence.Partition, FoundationRootCreator.OperationDigest(creator.Id), fingerprint, deadline,
                db.UsesRootInlinePromotion ? row.Revision : null);
            var pending = RootPreparedStarts.Offer(preparation, spec, origin.Key);
            if (!await RootPreparedStarts.RecordAsync(db, preparation, ct, origin.Bind))
                throw new StoreFormatException("The inline root preparation no longer names the selected store.");
            await transaction.CommitAsync(ct);
            return (preparation, pending, origin.Postgres);
        }

        async Task DisarmAsync(PreparedRootStart preparation, string issuance, (uint Marker, uint Namespace, long Table)? physical,
            CancellationToken ct)
        {
            await using var db = create();
            await using var transaction = await (Engine?.BeginAsync(db, ct) ?? db.Database.BeginTransactionAsync(ct));
            var origin = await PinInlineRootSourceAsync(db, ct);
            if (origin.Postgres != physical ||
                RootPreparedStarts.Verify(origin.Key, scope, new(preparation.Reference), issuance) != preparation) return;
            await RootPreparedStarts.ConsumeAsync(db, preparation, ct, origin.Bind);
            await transaction.CommitAsync(ct);
        }
    }

    private sealed record InlineRootCheckpoint(FirstRootCheckpoint First, Contract.BoundScope Scope, PreparedRootStart Stage, string Issuance,
        (uint Marker, uint Namespace, long Table)? Postgres, long ReceiptWindow, long Position, string Journal)
        : QualifiedRootCheckpoint(First.JournalRevision)
    {
        internal override bool WritesJournal(string actor) => actor == Stage.Actor;
        internal override ValueTask CheckBeforeWriteAsync(KernelDb db, CancellationToken token) => First.CheckBeforeWriteAsync(db, token);

        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken token)
        {
            var origin = await PinInlineRootSourceAsync(db, token);
            if (origin.Postgres != Postgres ||
                (Stage.FirstRevision ?? 1) != JournalRevision ||
                RootPreparedStarts.Verify(origin.Key, Scope, new(Stage.Reference), Issuance) != Stage)
                return false;
            var now = await ReadInlineClockAsync(db, token);
            if (!await RootPreparedStarts.AdmitInlineBirthAsync(db, Stage, now, checked(now + ReceiptWindow), token, origin.Bind))
                return false;
            // The sequenced journal and Playback projectors qualify the new row through its birth.
            var journaled = await db.Database.ExecuteSqlInterpolatedAsync(RootPreparedStarts.Bind(db, $"""
                WITH source AS MATERIALIZED (/*prepared-start-source*/)
                INSERT INTO "Journal" ("Partition", "Position", "Instance", "Revision", "Entries")
                SELECT {Stage.Partition}, {Position}, {Stage.Actor}, {JournalRevision}, {Journal}
                FROM source WHERE "SourceReady"
                """, origin.Bind), token);
            if (journaled != 1) return false;
            var updated = await db.Database.ExecuteSqlInterpolatedAsync(RootPreparedStarts.Bind(db, $"""
                WITH source AS MATERIALIZED (/*prepared-start-source*/)
                UPDATE /*prepared-only*/"Instances" SET "FoundationReference" = {Stage.Reference}, "FoundationTenant" = {Stage.Tenant},
                    "FoundationHeader" = {First.Header}, "FoundationStateWire" = {First.Wire},
                    "FoundationWriteRevision" = {Stage.FirstRevision},
                    "State" = {RootStatePrefix(db)} || "State"
                WHERE "Id" = {Stage.Actor} AND "Partition" = {Stage.Partition} AND "Revision" = {JournalRevision}
                    AND "Status" = {(int)InstanceStatus.Running} AND "FoundationReference" IS NULL
                    AND (SELECT "SourceReady" FROM source)
                """, origin.Bind), token);
            return updated == 1 && await ReadInlineClockAsync(db, token) < Stage.Deadline;
        }
    }

    private static async Task<InlineRootSource> PinInlineRootSourceAsync(KernelDb db, CancellationToken token)
    {
        if (!db.UsesRootPreparedStarts || !db.UsesRootLiveEventWaits || !db.UsesRootStableBody)
            throw new StoreFormatException("Inline promotion requires the prepared stable root profile.");
        if (db.Database.IsNpgsql())
        {
            var (key, physical, bind) = await PostgresRootAdmission.PinInlineRootPreparationAsync(db, token);
            return new(key, physical, bind);
        }
        if (!db.UsesSqliteRootRuntime)
            throw new StoreFormatException("Inline promotion requires an installed SQLite owner context.");
        await RequireSqliteOperationalFormatAsync(db, token);
        return new(await SqliteRootAdmission.ReadRootIssuerKeyAsync(db, token), null, null);
    }

    private static Task<long> ReadInlineClockAsync(KernelDb db, CancellationToken token) =>
        db.Database.IsNpgsql() ? RootEventAdmission.ReadPostgresClockAsync(db, token)
            : SqliteRootAdmission.ReadClockAsync(db, token);
}
