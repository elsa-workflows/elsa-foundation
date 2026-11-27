using Kernel.Foundation.Hosting;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed record InlineRootSource(byte[] Key, (uint Marker, uint Namespace, long Table)? Postgres,
        Func<FormattableString, FormattableString>? Bind);
    private sealed record InlineStarts(Contract.BoundScope Scope, byte[] Key, (uint Marker, uint Namespace, long Table)? Postgres,
        Func<StartSpec, object, bool> Validate, ProtectRootStartAsync Protect, TimeSpan ReceiptWindow);
    private InlineStarts? _inlineStarts;

    internal async Task EnableInlinePromotionAsync(Contract.BoundScope scope, Func<StartSpec, object, bool> validate,
        ProtectRootStartAsync protect, TimeSpan receiptWindow, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(validate);
        ArgumentNullException.ThrowIfNull(protect);
        ArgumentOutOfRangeException.ThrowIfLessThan(receiptWindow, TimeSpan.FromMilliseconds(1));
        await using var db = create();
        if (!db.UsesRootInlinePromotion || !OwnedCommit)
            throw new NotSupportedException("Inline starts require the private owned promotion profile.");
        await using var transaction = await (Engine?.BeginAsync(db, token) ?? db.Database.BeginTransactionAsync(token));
        var origin = await PinInlineRootSourceAsync(db, token);
        await transaction.CommitAsync(token);
        lock (_workerBinding)
        {
            if (_ordinaryRoots is not null || _inlineStarts is not null)
                throw new InvalidOperationException("Configure inline starts once before binding the worker.");
            _inlineStarts = new(scope, origin.Key, origin.Postgres, validate, protect, receiptWindow);
        }
    }

    internal (Contract.ActorRef Reference, Task<InlineRootResult> Outcome)? QueueInlineRoot(Worker worker, string operationId,
        Contract.StartSpec spec, object material)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (_inlineStarts is not { } inline || _ordinaryRoots?.InlineIssuer != inline ||
            !FoundationMetadataLimits.IsOutsideMessageId(operationId) || !FoundationMetadataLimits.FitsName(spec.Type) ||
            !spec.Ephemeral || !Enum.IsDefined(spec.History))
            return null;
        var start = new StartSpec(spec.Type, spec.TypeData, spec.Input, Ephemeral: true, History: (History)spec.History);
        try { RootAdmissionWire.RequireCreator(operationId, start); }
        catch (MetadataLimitExceeded) { return null; }
        var owner = worker.Fences.FirstOrDefault();
        if (owner is null) return null;
        var scope = inline.Scope;
        var actor = InstanceRef.In(owner.Partition, scope.Tenant + "_" + Guid.NewGuid().ToString("N"));
        var reference = FoundationRootCreator.ReferenceFor(inline.Key, scope.Tenant, new(actor.Id, 1));
        var operation = RootPreparedStarts.InlineOperation(operationId);
        var creator = FoundationRootCreator.Create(actor, operation, FoundationRootCreator.OperationDigest(operation), start, null, null)
            with { FoundationRootBinding = (actor.Id, scope.Tenant, reference) };
        return worker.TryQueueQualifiedInlineStart(creator, material, inline) is { } receipt ? (new(reference), receipt) : null;
    }

    // The direct qualification path exercises the same preparation and binding as the owned worker.
    internal async ValueTask<Committed> PromoteInlineRootAsync(Contract.BoundScope scope, Message creator, TurnCommit turn, PartitionFence fence,
        TimeSpan lease, TimeSpan receiptWindow, ProtectRootStartAsync protect, CancellationToken token)
    {
        var sentAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var prepared = await PrepareInlineCheckpointAsync(scope, creator, turn, fence, receiptWindow, protect, null, null,
            () => lease - System.Diagnostics.Stopwatch.GetElapsedTime(sentAt), token);
        if (prepared is null) return new(false, 0, []);
        var checkpoint = (InlineRootCheckpoint)prepared.Checkpoint;
        return await CommitAsync(new CommitBatch(fence.Partition, fence.Epoch, fence.Position, lease, false, [prepared.JournalTurn!], [], []),
            checkpoint with { Position = checked(fence.Position + 1) }, sentAt, token);
    }

    private async Task<PreparedRoot?> PrepareInlineCheckpointAsync(Contract.BoundScope scope, Message creator, TurnCommit turn,
        PartitionFence fence, TimeSpan receiptWindow, ProtectRootStartAsync protect,
        InlineStarts? installed, InlineRootCreatorWitness? witness, Func<TimeSpan> leaseRemaining, CancellationToken token)
    {
        if (!OwnedCommit || creator.Start is not { Ephemeral: true, Parent: null } spec ||
            creator.FoundationRootBinding is not { } binding || binding.Actor != creator.Target.Id || binding.Tenant != scope.Tenant ||
            !RootPreparedStarts.IsInlineOperation(creator.Id) ||
            creator.Due is not null || creator.ReplyTo is not null || creator.KeepUntil is not null ||
            turn is not { Expected: null, Journal: not null, FoundationHeader: { } header,
                Row: { Revision: > 0, FoundationInlineBirth: { } birth } row } ||
            turn.Instance != binding.Actor || birth != (binding.Tenant, creator.InboxKey) ||
            receiptWindow <= TimeSpan.Zero)
            throw RootShape();
        var ended = row.Status is InstanceStatus.Finished or InstanceStatus.Faulted;
        await using (var profile = create())
            if (!profile.UsesRootInlinePromotion && (row.Revision != 1 || ended || turn.Sent.Count != 0)) throw RootShape();

        var fingerprint = CreationProtocol.Fingerprint(spec, creator.Kind, creator.Payload)!;
        var supported = new TurnCommit(row.Id, row, null, turn.Consumed, turn.Done, turn.Sent,
            turn.Subscribed, turn.Unsubscribed, turn.Journal, Unsubscribe: ended, Held: turn.Held)
        {
            FoundationHeader = header,
            FoundationTerminalOutput = row.Status == InstanceStatus.Finished ? turn.FoundationTerminalOutput : null
        };
        var expected = new InstanceRow(binding.Actor, spec.Model, spec.Definition, row.Status,
            row.State, row.Revision, Parent: null, Jobs: RootJobsEnabled ? row.Jobs : null, History: spec.History,
            StateVersion: row.StateVersion, Creation: 1, StartIntent: fingerprint)
        {
            FoundationInlineBirth = birth, FoundationInlineHeader = row.FoundationInlineHeader,
            FoundationNullState = row.FoundationNullState, FoundationDispatchHeader = row.FoundationDispatchHeader,
            NextJobGeneration = row.NextJobGeneration
        };
        if (turn != supported || row != expected || row.Status != InstanceStatus.Running && !ended ||
            row.Status == InstanceStatus.Faulted && !RootTimedJobSettlementEnabled ||
            !RootJobsEnabled && row.NextJobGeneration is not (null or 0) ||
            ended && (turn.Sent.Count != 0 || turn.Subscribed.Count != 0 || row.Jobs is not null) ||
            turn.Consumed.Count + turn.Done.Count + turn.Unsubscribed.Count + (turn.Held?.Count ?? 0) != 0 ||
            row.FoundationDispatchHeader is not { } dispatch || dispatch.Tenant != binding.Tenant ||
            dispatch.Reference != binding.Reference || dispatch.Header.TypeData != header.TypeData ||
            dispatch.Header.Labels.Count != header.Labels.Count ||
            dispatch.Header.Labels.Any(label => header.Labels.GetValueOrDefault(label.Key) != label.Value))
            throw RootShape();

        var root = new WorkerRoot(binding.Tenant, binding.Reference, binding.Actor, fence.Partition, fingerprint);
        var sends = EncodeRootFirstSends(root, row, turn, promotingInline: true);
        var first = PrepareFirstRoot(binding.Tenant, binding.Reference, creator, fence,
            row.FoundationNullState ? null : row.State, row.StateVersion, header.TypeData, header.Labels, turn.Journal,
            jobState: row.NextJobGeneration is null ? null : new RootJobState(row.Jobs, row.NextJobGeneration),
            status: row.Status, terminalGrowth: dispatch.TerminalGrowth, revision: row.Revision);
        var journal = RootEventWaits.Lower(root, turn, first.JournalTurn! with { Sent = sends });
        PreparedRootStart stage;
        PendingRootStart offer;
        (uint Marker, uint Namespace, long Table)? source;
        var remaining = leaseRemaining();
        if (remaining <= TimeSpan.Zero) return null;
        if (remaining > RootPreparedStarts.OneWayBudget) remaining = RootPreparedStarts.OneWayBudget;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(remaining);
        try
        {
            if (await PrepareAsync(budget.Token) is not { } ready) return null;
            (stage, offer, source, var providerWindow) = ready;
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);
            var timeLeft = (providerWindow < remaining ? providerWindow : remaining) - elapsed;
            if (timeLeft <= TimeSpan.Zero) return null;
            budget.CancelAfter(timeLeft);
            if (!await RootPreparedStarts.ProtectAsync(protect, offer, budget.Token))
            {
                await DisarmAsync(stage, offer.Issuance, source, budget.Token);
                return null;
            }
            budget.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && budget.IsCancellationRequested)
        {
            return null;
        }

        if (leaseRemaining() <= TimeSpan.Zero) return null;
        var checkpoint = new InlineRootCheckpoint((FirstRootCheckpoint)first.Checkpoint, scope, stage, offer.Issuance,
            source, (long)receiptWindow.TotalMilliseconds, 0, turn.Journal, fence.Epoch, witness?.LeaseRemaining,
            turn.FoundationTerminalOutput is { } output ? new RootTerminalOutputCheckpoint(root, row.Revision, output) : null);
        return new(journal, checkpoint);

        async Task<(PreparedRootStart, PendingRootStart, (uint Marker, uint Namespace, long Table)?, TimeSpan)?> PrepareAsync(CancellationToken ct)
        {
            await using var db = create();
            await using var transaction = await (Engine?.BeginAsync(db, ct) ?? db.Database.BeginTransactionAsync(ct));
            var origin = await PinInlineRootSourceAsync(db, ct);
            if (installed is not null && (origin.Postgres != installed.Postgres || !origin.Key.AsSpan().SequenceEqual(installed.Key)))
                return null;
            var now = await ReadInlineClockAsync(db, ct);
            var timeLeft = leaseRemaining();
            var serverLease = await ReadInlineLeaseUntilAsync(db, fence.Partition, fence.Epoch, fence.Position, ct);
            if (timeLeft.TotalMilliseconds < 1 || serverLease is null || serverLease <= now) return null;
            var deadline = Math.Min(serverLease.Value, checked(now + (long)Math.Min(timeLeft.TotalMilliseconds, remaining.TotalMilliseconds)));
            var preparation = new PreparedRootStart(binding.Tenant, binding.Reference, binding.Actor,
                fence.Partition, FoundationRootCreator.OperationDigest(creator.Id), fingerprint, deadline,
                db.UsesRootInlinePromotion ? row.Revision : null);
            var priorDeadline = witness is null ? null : await RootPreparedStarts.ReadInlineDeadlineAsync(db, preparation, ct, origin.Bind);
            if (priorDeadline <= now) return null;
            if (priorDeadline is { } prior) preparation = preparation with { Deadline = prior };
            var pending = RootPreparedStarts.Offer(preparation, spec, origin.Key);
            if (priorDeadline is null && !await RootPreparedStarts.RecordAsync(db, preparation, ct, origin.Bind))
                throw new StoreFormatException("The inline root preparation no longer names the selected store.");
            await transaction.CommitAsync(ct);
            return (preparation, pending, origin.Postgres, TimeSpan.FromMilliseconds(preparation.Deadline - now));
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
        (uint Marker, uint Namespace, long Table)? Postgres, long ReceiptWindow, long Position, string Journal, long Epoch,
        Func<TimeSpan>? LeaseRemaining, RootTerminalOutputCheckpoint? Output)
        : QualifiedRootCheckpoint(First.JournalRevision)
    {
        internal override bool WritesJournal(string actor) => actor == Stage.Actor;
        internal override ValueTask CheckBeforeWriteAsync(KernelDb db, CancellationToken token) => First.CheckBeforeWriteAsync(db, token);

        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken token)
        {
            if (LeaseRemaining?.Invoke() <= TimeSpan.Zero) return false;
            var origin = await PinInlineRootSourceAsync(db, token);
            if (origin.Postgres != Postgres ||
                (Stage.FirstRevision ?? 1) != JournalRevision ||
                RootPreparedStarts.Verify(origin.Key, Scope, new(Stage.Reference), Issuance) != Stage)
                return false;
            var now = await ReadInlineClockAsync(db, token);
            if (await ReadInlineLeaseUntilAsync(db, Stage.Partition, Epoch, null, token) is not { } currentLease || currentLease <= now)
                return false;
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
            var ended = First.Row.Status != InstanceStatus.Running;
            var updated = await db.Database.ExecuteSqlInterpolatedAsync(RootPreparedStarts.Bind(db, $"""
                WITH source AS MATERIALIZED (/*prepared-start-source*/)
                UPDATE /*prepared-only*/"Instances" SET "FoundationReference" = {Stage.Reference}, "FoundationTenant" = {Stage.Tenant},
                    "FoundationHeader" = {First.Header}, "FoundationStateWire" = {First.Wire},
                    "FoundationWriteRevision" = {Stage.FirstRevision}, "FoundationTerminalRevision" = {(ended ? JournalRevision : (long?)null)},
                    "FoundationTerminalAt" = {(ended ? now : (long?)null)},
                    "State" = {RootStatePrefix(db)} || "State"
                WHERE "Id" = {Stage.Actor} AND "Partition" = {Stage.Partition} AND "Revision" = {JournalRevision}
                    AND "Status" = {(int)First.Row.Status} AND "FoundationReference" IS NULL
                    AND (NOT {ended} OR NOT EXISTS (SELECT 1 FROM "Subscriptions" WHERE "Instance" = {Stage.Actor}))
                    AND (SELECT "SourceReady" FROM source)
                """, origin.Bind), token);
            return updated == 1 && (Output is null || await Output.BindCheckpointAsync(db, token)) &&
                await ReadInlineClockAsync(db, token) < Math.Min(Stage.Deadline, currentLease) &&
                !(LeaseRemaining?.Invoke() <= TimeSpan.Zero);
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

    private static Task<long?> ReadInlineLeaseUntilAsync(KernelDb db, int partition, long epoch, long? position, CancellationToken token) =>
        (from placement in db.Partitions
         join node in db.Nodes on placement.Owner equals node.Id
         where placement.Id == partition && placement.Epoch == epoch && (position == null || placement.Position == position)
         select (long?)node.LeaseUntil).SingleOrDefaultAsync(token);
}
