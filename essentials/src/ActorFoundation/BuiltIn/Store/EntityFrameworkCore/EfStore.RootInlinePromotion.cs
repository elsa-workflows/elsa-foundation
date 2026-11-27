using System.Runtime.CompilerServices;
using Kernel.Foundation.Hosting;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed record InlineRootSource(byte[] Key, (uint Marker, uint Namespace, long Table)? Postgres,
        Func<FormattableString, FormattableString>? Bind);
    private sealed record InlineStarts(Contract.BoundScope Scope, byte[] Key, (uint Marker, uint Namespace, long Table)? Postgres,
        Func<StartSpec, object, bool> Validate, ProtectRootStartAsync Protect, TimeSpan ReceiptWindow,
        Contract.BoundScope? AdditionalScope, Func<Contract.ActorStatus, bool>? AllowsAdditionalWriteFreeCompletion, bool HoldsAdditional)
    {
        internal Contract.BoundScope? ScopeFor(string tenant) => tenant == Scope.Tenant ? Scope :
            tenant == AdditionalScope?.Tenant ? AdditionalScope : null;

        internal bool AllowsWriteFreeCompletion(string tenant, Contract.ActorStatus status) =>
            tenant != AdditionalScope?.Tenant || AllowsAdditionalWriteFreeCompletion!(status);

        internal bool Holds(Contract.BoundScope scope) => HoldsAdditional && ReferenceEquals(AdditionalScope, scope);
    }
    private InlineStarts? _inlineStarts;

    internal async Task EnableInlinePromotionAsync(Contract.BoundScope scope, Func<StartSpec, object, bool> validate,
        ProtectRootStartAsync protect, TimeSpan receiptWindow, CancellationToken token,
        Contract.BoundScope? additionalScope = null, Func<Contract.ActorStatus, bool>? allowsAdditionalWriteFreeCompletion = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(validate);
        ArgumentNullException.ThrowIfNull(protect);
        ArgumentOutOfRangeException.ThrowIfLessThan(receiptWindow, TimeSpan.FromMilliseconds(1));
        if ((additionalScope is null) != (allowsAdditionalWriteFreeCompletion is null) || additionalScope?.Tenant == scope.Tenant)
            throw new ArgumentException("An additional inline tenant requires its own completion policy.", nameof(additionalScope));
        await using var db = create();
        if (!db.UsesRootInlinePromotion || !OwnedCommit ||
            additionalScope is not null && !db.UsesRootEphemeralTestRuns)
            throw new NotSupportedException("Inline starts require an owned promotion profile that supports the selected scope.");
        await using var transaction = await (Engine?.BeginAsync(db, token) ?? db.Database.BeginTransactionAsync(token));
        var origin = await PinInlineRootSourceAsync(db, token);
        await transaction.CommitAsync(token);
        lock (_workerBinding)
        {
            if (_ordinaryRoots is not null || _inlineStarts is not null)
                throw new InvalidOperationException("Configure inline starts once before binding the worker.");
            _inlineStarts = new(scope, origin.Key, origin.Postgres, validate, protect, receiptWindow,
                additionalScope, allowsAdditionalWriteFreeCompletion,
                db.UsesRootHeldEphemeralHttpTestRuns && additionalScope is not null);
        }
    }

    internal (Contract.ActorRef Reference, Task<InlineRootResult> Outcome)? QueueInlineRoot(Worker worker, string operationId,
        Contract.StartSpec spec, object material) => CreateInlineRootSpec(operationId, spec) is { } start
        ? QueueInlineRoot(worker, operationId, start, material, wake: null)
        : null;

    internal static StartSpec? CreateInlineRootSpec(string operationId, Contract.StartSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (!FoundationMetadataLimits.IsOutsideMessageId(operationId) || !FoundationMetadataLimits.FitsName(spec.Type) ||
            !spec.Ephemeral || !Enum.IsDefined(spec.History))
            return null;
        var start = new StartSpec(spec.Type, spec.TypeData, spec.Input, Ephemeral: true, History: (History)spec.History);
        try { RootAdmissionWire.RequireCreator(operationId, start); }
        catch (MetadataLimitExceeded) { return null; }
        return start;
    }

    internal (Contract.ActorRef Reference, Task<InlineRootResult> Outcome)? QueueInlineRoot(Worker worker, string operationId,
        StartSpec start, object material, Action<InstanceRef>? wake, Contract.BoundScope? selectedScope = null, HeldRootRequest? held = null)
    {
        if (_inlineStarts is not { } inline || !ReferenceEquals(_ordinaryRoots?.InlineIssuer, inline)) return null;
        var scope = selectedScope ?? inline.Scope;
        if (!ReferenceEquals(inline.ScopeFor(scope.Tenant), scope) ||
            held is not null && (!inline.Holds(scope) || held.Tenant != scope.Tenant)) return null;
        var name = scope.Tenant + "_" + Guid.NewGuid().ToString("N");
        var operation = RootPreparedStarts.InlineOperation(operationId);
        var digest = FoundationRootCreator.OperationDigest(operation);
        if (worker.TryPlaceQualifiedInlineStart(name, start, material, inline, AdmissionPressure, Bind, held) is not { } selected)
            return null;
        var (creator, receipt) = selected;
        wake?.Invoke(creator.Target);
        return (new(creator.FoundationRootBinding!.Value.Reference), receipt);

        Message Bind(InstanceRef actor)
        {
            var reference = FoundationRootCreator.ReferenceFor(inline.Key, scope.Tenant, new(actor.Id, 1));
            return FoundationRootCreator.Create(actor, operation, digest, start, null, held?.Address)
                with { FoundationRootBinding = (actor.Id, scope.Tenant, reference) };
        }
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
            creator.Due is not null || creator.ReplyTo is not null && (installed is null || !installed.Holds(scope)) || creator.KeepUntil is not null ||
            turn is not { Expected: null, Journal: not null, FoundationHeader: { } header,
                Row: { Revision: > 0, FoundationInlineBirth: { } birth } row } ||
            turn.Instance != binding.Actor || birth != (binding.Tenant, creator.InboxKey) ||
            receiptWindow <= TimeSpan.Zero)
            throw RootShape();
        var parked = turn.FoundationInlinePark is not null;
        var ended = !parked && row.Status is (InstanceStatus.Finished or InstanceStatus.Faulted);
        await using (var profile = create())
            if (!profile.UsesRootInlinePromotion &&
                (row.Revision != 1 || ended || parked || turn.Sent.Count != 0 || turn.FoundationRootChildSends is not null)) throw RootShape();

        var fingerprint = CreationProtocol.Fingerprint(spec, creator.Kind, creator.Payload)!;
        var root = new WorkerRoot(binding.Tenant, binding.Reference, binding.Actor, fence.Partition, fingerprint);
        var children = turn.FoundationRootChildSends is { } childSends
            ? PrepareRootChildEffects(root, row, childSends, fence) ?? throw RootShape()
            : null;
        var attached = children?.Starts.Where(child => child.Creator.Start!.Parent is not null)
            .Select(child => new CreationAddress(child.Creator.Target.Id, 1)) ?? [];
        var supported = new TurnCommit(row.Id, row, null, turn.Consumed, turn.Done, turn.Sent,
            turn.Subscribed, turn.Unsubscribed, turn.Journal, Unsubscribe: ended || parked, Held: turn.Held)
        {
            FoundationHeader = header,
            OpensPark = parked,
            FoundationInlinePark = turn.FoundationInlinePark,
            FoundationInlineJob = turn.FoundationInlineJob,
            FoundationKept = parked && turn.FoundationKept is { Count: 0 } ? turn.FoundationKept : null,
            FoundationRootChildSends = turn.FoundationRootChildSends,
            FoundationRootPublications = turn.FoundationRootPublications,
            FoundationTerminalOutput = row.Status == InstanceStatus.Finished ? turn.FoundationTerminalOutput : null
        };
        var expected = new InstanceRow(binding.Actor, spec.Model, spec.Definition, row.Status,
            row.State, row.Revision, Parent: null, Fault: parked ? row.Fault : null, Jobs: RootJobsEnabled ? row.Jobs : null,
            History: spec.History, Children: children?.Children,
            StateVersion: row.StateVersion, Creation: 1, StartIntent: fingerprint)
        {
            FoundationInlineBirth = birth, FoundationInlineHeader = row.FoundationInlineHeader,
            FoundationNullState = row.FoundationNullState, FoundationDispatchHeader = row.FoundationDispatchHeader,
            NextJobGeneration = row.NextJobGeneration
        };
        if (turn != supported || row != expected || row.Status != InstanceStatus.Running && !ended && !parked ||
            children is { Notice: not null } || !ChildRegistry.Read(row.Children).SequenceEqual(attached) ||
            row.Status == InstanceStatus.Faulted && !parked && !RootTimedJobSettlementEnabled ||
            !RootJobsEnabled && row.NextJobGeneration is not (null or 0) ||
            ended && (turn.Sent.Count != 0 || turn.Subscribed.Count != 0 || row.Jobs is not null) ||
            turn.Consumed.Count + turn.Done.Count + turn.Unsubscribed.Count + (turn.Held?.Count ?? 0) != 0 ||
            row.FoundationDispatchHeader is not { } dispatch || dispatch.Tenant != binding.Tenant ||
            dispatch.Reference != binding.Reference || dispatch.Header.TypeData != header.TypeData ||
            dispatch.Header.Labels.Count != header.Labels.Count ||
            dispatch.Header.Labels.Any(label => header.Labels.GetValueOrDefault(label.Key) != label.Value))
            throw RootShape();
        if (children is { Starts.Length: > 0, PendingBirths: null }) return null;

        List<EventRow>? publications = null;
        if (turn.FoundationRootPublications is { } authored)
        {
            if (installed is null || witness is null || _ordinaryRoots?.PublishesAuthoredEvents != true || RootSelfIssuer is not { } issuer)
                throw RootShape();
            RootEventWaits.CollectPublications(root, turn, authored, issuer, publications = []);
        }

        var park = turn.FoundationInlinePark is { } parkWitness ? LowerInlinePark(root, turn, row, parkWitness, witness) : null;
        var capturedJob = turn.FoundationInlineJob;
        if (capturedJob is not null && (installed is null || witness is null || !RootTimedJobSettlementEnabled || parked ||
            !MatchesInlineJob(row, capturedJob, turn.Sent))) throw RootShape();
        var sends = capturedJob is null ? EncodeRootFirstSends(root, row, turn, promotingInline: true)
            : EncodeRootSends(root, row, turn.Sent, promotingInline: true);
        var first = WithRootChildEffects(PrepareFirstRoot(binding.Tenant, binding.Reference, creator, fence,
            row.FoundationNullState ? null : row.State, row.StateVersion, header.TypeData, header.Labels, turn.Journal,
            jobState: row.NextJobGeneration is null ? null : new RootJobState(row.Jobs, row.NextJobGeneration), status: row.Status,
            terminalGrowth: dispatch.TerminalGrowth, creatorFault: park is null ? null : row.Fault, revision: row.Revision), children, null);
        var journal = RootEventWaits.Lower(root, turn, first.JournalTurn! with { Sent = sends }, _ordinaryRoots?.KeepsEventWaits == true);
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
            if (!await RootPreparedStarts.ProtectAsync(protect, offer, budget.Token) ||
                children is { PendingBirths: not null } &&
                !await RootPreparedStarts.ProtectAsync((_, ct) => ProtectChildrenAsync(children, ct), offer, budget.Token))
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
            turn.FoundationTerminalOutput is { } output ? new RootTerminalOutputCheckpoint(root, row.Revision, output) : null, park,
            capturedJob, publications ?? []);
        return new(journal, checkpoint);

        async ValueTask<bool> ProtectChildrenAsync(RootChildEffects effects, CancellationToken ct)
        {
            try { return await ProtectRootChildEffectsAsync([effects], ct); }
            catch (StoreException error) when (error.Failure == Failure.Definite) { return false; }
        }

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

    private static bool MatchesInlineJob(InstanceRow row, InlineJobWinner captured, IReadOnlyList<Message> sends) =>
        row is { Status: InstanceStatus.Running, Creation: 1, Jobs: not null } && captured.Checkpoint is
            { Status: InstanceStatus.Running, Creation: 1, FoundationInlineBirth: not null } previous &&
        row.Id == previous.Id && row.Model == previous.Model && row.StartIntent == previous.StartIntent && row.Revision >= previous.Revision &&
        row.FoundationInlineBirth == previous.FoundationInlineBirth && row.Jobs == previous.Jobs && row.NextJobGeneration == previous.NextJobGeneration &&
        JobRegistry.Read(row.Jobs) is [var registered] && registered.Generation >= 0 && registered.Generation + 1 == row.NextJobGeneration &&
        captured.Ticket is { HasIssuedTicket: true } original && RootJobTicket.Read(original, row.Id, row.Model, timed: true) is { } ticket &&
        ticket.Key == registered.Key && original.JobGeneration == registered.Generation && captured.Deadline.HasIssuedTicket &&
        captured.Deadline.WithoutTicketAuthority() == RootJobTicket.Deadline(row.Id, ticket) &&
        RootJobTicket.IsRecordedResult(captured.Result, row) &&
        (captured.Callback is { } callback
            ? callback is { Result.HasIssuedTicket: true, PublishedAt: >= 0 } && callback.PublishedAt < ticket.Deadline &&
                callback.Original == original && callback.Result.Payload == captured.Result.Payload &&
                RootJobTicket.MatchesCompletion(original, callback.Result, ticket.Key)
            : captured.Result.Read<JobFinished>() is { Result: null, Error: "timeout" }) &&
        sends.Where(RootJobTicket.IsTimedSend).SequenceEqual([captured.Result]);

    private sealed record InlineParkWire(Message Input, Message Wire, bool Failed);
    private sealed record InlineParkOpening(IReadOnlyList<InlineParkWire> Inputs, FoundationSelfIssuer Issuer);

    private InlineParkOpening LowerInlinePark(WorkerRoot root, TurnCommit turn, InstanceRow row, InlineParkWitness park,
        InlineRootCreatorWitness? witness)
    {
        if (witness is null || _ordinaryRoots?.KeepsPrivateParkInputs != true || RootSelfIssuer is not { } issuer ||
            turn is not { FoundationKept.Count: 0, FoundationRootChildSends: null, Subscribed.Count: 0, Sent.Count: 0 } ||
            row is not { Status: InstanceStatus.Faulted, Fault: { } fault })
            throw RootShape();
        if (park.Checkpoint is null)
        {
            if (row is not { Revision: 1, FoundationNullState: true, Jobs: null, Children: null } ||
                row.StartIntent != root.Fingerprint || park.Inputs is not [{ Failed: true, Envelope: var failed }] ||
                !ReferenceEquals(failed, witness.Creator) ||
                turn.FoundationHeader is not { Labels.Count: 0 } header || header.TypeData != row.Definition)
                throw RootShape();
            var retry = FoundationInlineRetry.Create(issuer, failed);
            if (!FoundationInlineRetry.Matches(issuer, row, retry)) throw RootShape();
            return new([new(retry, EncodeRootSelf(root.Actor, 1, FoundationInlineRetry.Ordinal, retry), Failed: true)], issuer);
        }
        if (park.Checkpoint is not { Status: InstanceStatus.Running, Revision: > 0, Suspended: false, Jobs: null,
                Children: null, FoundationDispatchHeader: not null } before ||
            row != (before with { Status = InstanceStatus.Faulted, Revision = checked(before.Revision + 1), Fault = fault }) ||
            !park.Inputs.Any(input => input.Failed) ||
            park.Inputs.Select(input => input.Envelope.InboxKey).Distinct().Count() != park.Inputs.Count ||
            !ParkRecoveryCredit.FitsInlineMailbox(park.Inputs.Select(input => input.Envelope)) ||
            park.Inputs.Any(input => !FoundationSelfSend.IsSelfInput(input.Envelope, root.Actor, 1)))
            throw RootShape();

        var wires = EncodeRootSends(root, before, park.Inputs.Select(input => input.Envelope).ToArray(), promotingInline: true);
        return new(park.Inputs.Select((input, index) => new InlineParkWire(input.Envelope, wires[index], input.Failed)).ToArray(), issuer);
    }

    private sealed record InlineRootCheckpoint(FirstRootCheckpoint First, Contract.BoundScope Scope, PreparedRootStart Stage, string Issuance,
        (uint Marker, uint Namespace, long Table)? Postgres, long ReceiptWindow, long Position, string Journal, long Epoch,
        Func<TimeSpan>? LeaseRemaining, RootTerminalOutputCheckpoint? Output, InlineParkOpening? Park, InlineJobWinner? Job,
        IReadOnlyList<EventRow> Publications)
        : QualifiedRootCheckpoint(First.JournalRevision)
    {
        internal override bool WritesJournal(string actor) => actor == Stage.Actor;
        internal override ValueTask CheckBeforeWriteAsync(KernelDb db, CancellationToken token) => First.CheckBeforeWriteAsync(db, token);

        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken token)
        {
            if (LeaseRemaining?.Invoke() <= TimeSpan.Zero) return false;
            var origin = await PinInlineRootSourceAsync(db, token);
            if (origin.Postgres != Postgres || Park is not null && (!db.UsesRootInlinePromotion || !db.RecordsRootParkRevision) ||
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
            var ended = Park is null && First.Row.Status != InstanceStatus.Running;
            var updated = await db.Database.ExecuteSqlInterpolatedAsync(RootPreparedStarts.Bind(db, $"""
                WITH source AS MATERIALIZED (/*prepared-start-source*/)
                UPDATE /*prepared-only*/"Instances" SET "FoundationReference" = {Stage.Reference}, "FoundationTenant" = {Stage.Tenant},
                    "FoundationHeader" = {First.Header}, "FoundationStateWire" = {First.Wire},
                    "FoundationWriteRevision" = {Stage.FirstRevision},
                    "FoundationTerminalRevision" = {(ended ? JournalRevision : (long?)null)},
                    "FoundationTerminalAt" = {(ended ? now : (long?)null)},
                    "FoundationParkAt" = {(Park is null ? (long?)null : now)},
                    "FoundationParkRevision" = {(Park is null ? (long?)null : JournalRevision)},
                    "State" = {RootStatePrefix(db)} || "State"
                WHERE "Id" = {Stage.Actor} AND "Partition" = {Stage.Partition} AND "Revision" = {First.JournalRevision}
                    AND "Status" = {(int)First.Row.Status} AND "FoundationReference" IS NULL
                    AND (NOT {ended || Park is not null} OR NOT EXISTS (SELECT 1 FROM "Subscriptions" WHERE "Instance" = {Stage.Actor}))
                    AND (SELECT "SourceReady" FROM source)
                """, origin.Bind), token);
            return updated == 1 && (Output is null || await Output.BindCheckpointAsync(db, token)) &&
                (First.ChildEffects is null || await First.ChildEffects.BindAsync(db, Stage.Tenant, token)) &&
                (Park is null || await BindParkAsync(db, origin, Park, now, token)) &&
                (Job is null || await VerifyInlineJobAsync(db, Job, now, token)) &&
                await ReadInlineClockAsync(db, token) < Math.Min(Stage.Deadline, currentLease) &&
                !(LeaseRemaining?.Invoke() <= TimeSpan.Zero);
        }

        private async Task<bool> VerifyInlineJobAsync(KernelDb db, InlineJobWinner job, long at, CancellationToken token)
        {
            if (!db.UsesRootInlinePromotion || !db.UsesRootTimedJobSettlement ||
                (job.Callback?.PublishedAt is { } publication ? publication > at : job.Deadline.Due > at)) return false;
            var rows = await db.Messages.AsNoTracking().Where(message => message.Target == Stage.Actor && message.Partition == Stage.Partition &&
                message.InboxScope == RootTimedJobScope && !message.Done).Take(2).ToListAsync(token);
            return rows is [var stored] && DecodeRootTimedRow(stored) == job.Result;
        }

        private async Task<bool> BindParkAsync(KernelDb db, InlineRootSource source, InlineParkOpening park, long at, CancellationToken token)
        {
            if (park.Inputs.Any(input => input.Failed && input.Wire.Due > at)) return false;
            // Preserve the local mailbox order in physical sequences, including a pending timer that matures before Recover.
            var payload = Json.Write(park.Inputs.Select((input, position) => new
            {
                Id = input.Wire.InboxKey.Id, input.Wire.Kind, input.Wire.Payload, Due = input.Wire.Due ?? 0, Position = position
            }).ToArray());
            var inlineRows = db.UsesSqliteRootRuntime
                ? """
                    SELECT json_extract(value, '$.Id') AS "Id", json_extract(value, '$.Kind') AS "Kind",
                        json_extract(value, '$.Payload') AS "Payload", json_extract(value, '$.Due') AS "Due", CAST(key AS integer) AS "Position"
                    FROM json_each(/*rows*/)
                    """
                : """
                    SELECT * FROM jsonb_to_recordset(/*rows*/::jsonb) AS x("Id" text, "Kind" text, "Payload" text, "Due" bigint, "Position" integer)
                    """;
            FormattableString insert = $"""
                WITH source AS MATERIALIZED (/*prepared-start-source*/), arrivals AS MATERIALIZED (/*inline-rows*/)
                INSERT INTO "Messages" ("Id", "InboxScope", "Target", "Partition", "Kind", "Payload", "Sender",
                    "Due", "Done", "Seen", "Attempts", "Trial", "JobGeneration", "FoundationKeptBy"/*seq-column*/)
                SELECT arrivals."Id", {RootSelfScope}, {Stage.Actor}, {Stage.Partition}, arrivals."Kind", arrivals."Payload", {Stage.Actor},
                    arrivals."Due", arrivals."Due" <= {at}, 0, 0, 0, 0, CASE WHEN arrivals."Due" <= {at} THEN {(short)ParkKeepReason.Arrived} END/*inline-seq*/
                FROM arrivals CROSS JOIN source WHERE "SourceReady" ORDER BY arrivals."Position"
                """;
            insert = FormattableStringFactory.Create(insert.Format
                .Replace("/*inline-rows*/", inlineRows.Replace("/*rows*/", "{" + insert.ArgumentCount + "}", StringComparison.Ordinal), StringComparison.Ordinal)
                .Replace("/*inline-seq*/", db.UsesSqliteRootRuntime ? """, (SELECT coalesce(max("Seq"), 0) + 1 FROM "Messages") + arrivals."Position" """ : "", StringComparison.Ordinal),
                [.. insert.GetArguments(), payload]);
            if (await db.Database.ExecuteSqlInterpolatedAsync(RootPreparedStarts.Bind(db, RootChildDialectQuery(db, insert), source.Bind), token) != park.Inputs.Count)
                return false;
            var relation = db.UsesSqliteRootRuntime ? "\"Messages\"" : PostgresPrivateInboxView;
            FormattableString read = $"""
                SELECT * FROM /*inline-park-inputs*/ WHERE "Target" = {Stage.Actor} AND "Partition" = {Stage.Partition}
                    AND "InboxScope" = {RootSelfScope} AND "EventMatch" IS NULL AND "Trial" = 0
                    AND (("Due" <= {at} AND "Done" AND "FoundationKeptBy" = {(short)ParkKeepReason.Arrived}) OR
                        ("Due" > {at} AND NOT "Done" AND "FoundationKeptBy" IS NULL))
                """;
            var ids = park.Inputs.Select(input => input.Wire.InboxKey.Id).ToArray();
            var kept = await db.Messages.FromSqlInterpolated(FormattableStringFactory.Create(
                read.Format.Replace("/*inline-park-inputs*/", relation, StringComparison.Ordinal), read.GetArguments()))
                .AsNoTracking().Where(message => ids.Contains(message.Id)).OrderBy(message => message.Seq).ToArrayAsync(token);
            return kept.Select(row => DecodeRootInput(db, row, Stage.Reference, park.Issuer, timed: false)).SequenceEqual(park.Inputs.Select(input => input.Input)) &&
                await VerifyRootParkProjectionAsync(db, Stage.Tenant, Stage.Reference, JournalRevision, First.Row.Fault!, token);
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
