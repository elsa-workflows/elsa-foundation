using System.Text;
using Kernel.Runtime;
using Contract = Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

/// <summary>Runs a contract actor in the owner's turn. The adapter only stages effects.</summary>
internal sealed partial class ActorAdapter : Durable.IActorType
{
    private readonly Durable.InMemoryStore? inMemory;
    private readonly Contract.IActorType actor;
    private readonly (Durable.Requests Requests, string Node)? local;
    private readonly Func<Contract.ActorHeader, TimeSpan?>? retentionWindow;
    private readonly IRootDispatchHeaders? roots;
    private readonly Durable.FoundationSelfIssuer? issueSelf;
    private Durable.InMemoryStore store => inMemory ?? throw new NotSupportedException("This provider does not implement this foundation operation.");

    internal ActorAdapter(Durable.InMemoryStore store, Contract.IActorType actor,
        (Durable.Requests Requests, string Node)? local = null, Func<Contract.ActorHeader, TimeSpan?>? retentionWindow = null)
    {
        inMemory = store;
        this.actor = actor;
        this.local = local;
        this.retentionWindow = retentionWindow;
    }

    internal ActorAdapter(IRootDispatchHeaders roots, Contract.IActorType actor, Durable.FoundationSelfIssuer? issueSelf = null) =>
        (this.roots, this.actor, this.issueSelf) = (roots, actor, issueSelf);

    private const int TerminalEnvelopeLimit = 16_457_728;
    private const int TerminalHeaderReserve = 16_384;
    private const int MaxFaultTextBytes = 4_096;

    public bool SupportsFoundationOperator(Durable.InstanceRow? row) => roots is null && row?.Model == actor.Type;
    bool Durable.IActorType.AcceptsFoundationOperator(Durable.InstanceRow? row, Durable.Message message) =>
        roots is null ? SupportsFoundationOperator(row) : row?.Model == actor.Type && IsRootSuspension(message);
    bool Durable.IActorType.SupportsFoundationInlineStart(string type) => roots is null && type == actor.Type;

    bool Durable.IActorType.SupportsTrialWithdrawal => roots is null;

    bool Durable.IActorType.AuthorizesTrialWithdrawal(Durable.InstanceRow? row, Durable.Message message, Durable.Message? creator)
    {
        if (message.Kind != Contract.Kinds.Terminate) return false;
        if (row is null)
            return creator is { BindFoundationChild: true, InboxScope: Durable.InboxKey.ChildCreatorScope,
                   Start: { Parent: { } creatorParent, ParentCreation: > 0, Creation.Generation: var creation } start } &&
                start.Model == actor.Type && creator.Target == message.Target && creator.TargetCreation == creation &&
                message.TargetCreation == creation && message.Operator is null && message.InboxKey.HasIssuedOrigin &&
                message.Sender == creatorParent && message.SenderCreation == start.ParentCreation;
        if (!SupportsFoundationOperator(row) || row.ParkDiscard?.Kind == Durable.ParkClearKind.Terminate) return false;
        if (store.FoundationTurnHeader(row).Park is not null)
            return (row.ParkRecovery is null || row.Status == Durable.InstanceStatus.Running || row.Children is not null || row.Jobs is not null) &&
                store.AuthorizesDrainingTerminate(row, message);
        if (message.Operator is { Selection: { } selection })
        {
            if (row.Status != Durable.InstanceStatus.Running && row.Children is null && row.Jobs is null) return false;
            var identity = store.FoundationIdentity(row.Id, row.Creation);
            return store.MatchesFoundationSelection(identity.Tenant, identity.Reference, selection);
        }
        if (message.Operator is not null) return row.Status == Durable.InstanceStatus.Running;
        if (row.Status != Durable.InstanceStatus.Running && (row.Children is null || message.ActorAuthored)) return false;
        return row.Parent is not null && message.InboxKey.HasIssuedOrigin &&
            message.Sender == row.Parent && message.SenderCreation == row.ParentCreation;
    }

    public bool Withholds(Durable.InstanceRow row, Durable.ITurnServices services)
    {
        if (roots is not null) return actor.Withholds(RootHeader(row));
        if (row.ParkDiscard?.Kind == Durable.ParkClearKind.Terminate) return true;
        var checkpoint = store.FoundationTurnHeader(row);
        if (row.ParkRecovery is not null)
            return row.Status == Durable.InstanceStatus.Running && RecoveryWithholds(Header(checkpoint));
        return checkpoint.Park is not null || !row.ParkDiscarded && actor.Withholds(Header(checkpoint));
    }

    bool Durable.IActorType.SettlesWhileWithheld(Durable.InstanceRow row, Durable.Delivery delivery) =>
        roots is null && row.ParkDiscard?.Kind != Durable.ParkClearKind.Terminate &&
        (row.ParkRecovery is null && store.FoundationTurnHeader(row).Park is not null
            ? !delivery.Message.IsControl
            : SettlesEndedChildNotice(row, delivery.Message) ||
                delivery.Message.Kind == Durable.Kinds.JobDeadline || delivery.IsWorkerJobCompletion && delivery.JobDeadline is not null);

    private static bool SettlesEndedChildNotice(Durable.InstanceRow? row, Durable.Message message) =>
        message.Kind == Contract.Kinds.ChildFinished && row is
            { EndedDuringParkRecovery: true, Status: not Durable.InstanceStatus.Running, ParkRecovery: null, ParkDiscarded: false };

    private static bool CapturesParkArrival(Durable.Delivery delivery) => !delivery.Message.IsControl && !delivery.IsWorkerJobCompletion;

    public bool SingleMessage(Durable.Message message) => message.Kind is Contract.Kinds.ChildFinished or Durable.Kinds.JobDeadline;

    public bool SelectsAlone(string? model, Durable.InstanceRow? row, Durable.Delivery delivery) =>
        SettlesEndedChildNotice(row, delivery.Message) ||
        !delivery.Held && (SingleMessage(delivery.Message) || delivery.Message.ReplyTo is not null ||
            delivery.IsWorkerJobCompletion && (delivery.JobDeadline is not null || IsParked(row)));

    private bool IsParked(Durable.InstanceRow? row) => roots is null && row is { Status: Durable.InstanceStatus.Faulted, ParkRecovery: null } &&
        store.FoundationTurnHeader(row).Park is not null;

    public Durable.TurnOutcome? Command(Durable.TurnInput input)
    {
        var delivery = input.Taken[input.From];
        var message = delivery.Message;
        if (input.Row is null && input.Spec is { } spec && !HeaderLimits.FitsStart(spec.Model, spec.Definition, spec.Input))
            return Durable.CreationProtocol.Refuse(input.Actor, null, input.Taken, "StartHeaderTooLarge");
        if (roots is not null)
            return RootRefusal(input) is { } reason
                ? Durable.CreationProtocol.Refuse(input.Actor, input.Row, input.Taken, reason) : null;
        if (message.Kind == Durable.Kinds.ParkExpiry)
            return ExpirePark(input);
        if (message.Kind == Durable.Kinds.Drain)
            return input.Row?.ParkRecovery is null ? ContinueDiscard(input) : ContinueRecover(input);
        if (message is { Kind: Contract.Kinds.Delete, Operator: { } deletion } && input.Row is { } deletionTarget)
        {
            var identity = store.FoundationIdentity(deletionTarget.Id, deletionTarget.Creation);
            if (!store.MatchesFoundationSelection(identity.Tenant, identity.Reference, deletion.Selection, requireTerminal: true))
                return Durable.CreationProtocol.Refuse(input.Actor, deletionTarget, input.Taken, "StaleSelection");
            if (deletion.RetentionDelete && RetentionRefusal(input, deletionTarget) is { } refusal) return refusal;
        }
        if (message.Operator?.DeleteTarget is not null &&
            (input.Row is not { } intended || !store.MatchesThenDeleteTarget(intended, message)))
            return Durable.CreationProtocol.Refuse(input.Actor, input.Row, input.Taken, "TargetMismatch");
        if (input.Row is { ParkDiscard: not null } draining)
        {
            if (message is { Kind: Contract.Kinds.Delete, Operator.Selection: { } deleteSelection })
                return TakeOverParkClear(input, draining, deleteSelection);
            return RefuseDuringResolution(input, draining);
        }
        if (message.Kind == Contract.Kinds.Terminate &&
            input.Row is { Status: Durable.InstanceStatus.Running, ParkRecovery: not null } liveRecovery &&
            store.FoundationTurnHeader(liveRecovery).Park is { } recoveryPark)
            return OpenParkTerminate(input, liveRecovery, recoveryPark);
        if (message is { Kind: Contract.Kinds.Delete, Operator.Selection: { } recoverySelection } &&
            input.Row is { Status: not Durable.InstanceStatus.Running, ParkRecovery: not null } endedRecovery)
            return TakeOverEndedRecovery(input, endedRecovery, recoverySelection);
        if (message.Operator is { DeleteTarget: not null } terminateDelete &&
            input.Row is { Status: not Durable.InstanceStatus.Running, ParkRecovery: not null } endedForDelete)
            return TakeOverEndedRecovery(input, endedForDelete, terminateDelete.Selection, thenDelete: true);
        if (input.Row is { ParkRecovery: not null } recovering &&
            message.Kind is Contract.Kinds.Recover or Contract.Kinds.Discard or Contract.Kinds.Delete or Contract.Kinds.Terminate)
            return RefuseDuringResolution(input, recovering);
        if (message.CascadeOrigin && !Durable.CreationProtocol.MatchesCascadeParent(input.Row, message))
            return Durable.CreationProtocol.Refuse(input.Actor, input.Row, input.Taken, "WrongParent");
        if (message.Kind == Contract.Kinds.Discard && message.Operator is not null)
            return OpenDiscard(input);
        if (message.Kind == Contract.Kinds.Recover && message.Operator is not null)
            return OpenRecover(input);
        if (input.Row is { } terminalParent && SettlesEndedChildNotice(terminalParent, message))
        {
            if (ChildNoticeError(terminalParent, message) is { } noticeError)
                return Durable.CreationProtocol.Refuse(input.Actor, terminalParent, input.Taken, noticeError);
            if (message.Payload is null) return Durable.CreationProtocol.Refuse(input.Actor, terminalParent, input.Taken, "InvalidChildCompletion");
            try
            {
                if (message.Read<Durable.FoundationChildNotice>() is not { Final: true })
                    return new(input.Actor) { Consumed = [.. input.Taken], Status = terminalParent.Status };
            }
            catch (System.Text.Json.JsonException)
            {
                return Durable.CreationProtocol.Refuse(input.Actor, terminalParent, input.Taken, "InvalidChildCompletion");
            }
            var children = Durable.ChildRegistry.Read(terminalParent.Children);
            children.Remove(new(message.Sender!, message.SenderCreation));
            return ParkCheckpoint(input, terminalParent, terminalParent with { Children = Durable.ChildRegistry.Write(children) });
        }
        if (input.Row is { ParkRecovery: not null, Status: not Durable.InstanceStatus.Running } endedDuringRecovery && !message.IsControl)
            return Durable.CreationProtocol.Refuse(input.Actor, endedDuringRecovery, input.Taken, "Ended");
        if (input.Row is { ParkDiscarded: true } discarded && !message.IsControl)
            return Durable.CreationProtocol.Refuse(input.Actor, discarded, input.Taken, "Ended");
        var parked = IsParked(input.Row);
        if (parked && CapturesParkArrival(delivery) &&
            input.Taken.All(item => item.Attempts < input.MaxAttempts || item.Message.Kind == Durable.Kinds.JobFinished))
        {
            var checkpoint = input.Row!;
            var revision = checked(checkpoint.Revision + 1);
            return new(input.Actor)
            {
                Expected = checkpoint.Revision, Row = checkpoint with { Revision = revision }, Revision = revision, Status = checkpoint.Status,
                Consumed = [.. input.Taken], FoundationKeep = Durable.ParkKeepReason.Arrived
            };
        }
        if (message.Kind == Durable.Kinds.JobDeadline)
            return TimeoutJob(input, message);
        if (delivery.IsWorkerJobCompletion && (delivery.JobDeadline is not null || parked) && input.Row is { } completedRow &&
            delivery.Original?.Read<Durable.JobTicket>() is { } ticket)
            return SettleJob(input, completedRow, message.Read<Durable.JobFinished>(), message.JobGeneration, ticket.Timer, cancel: false, keepCheckpoint: parked);
        if (message.Kind != Contract.Kinds.Delete && message.Operator is { Selection: { } selection } && input.Row is { } selected)
        {
            var identity = store.FoundationIdentity(selected.Id, selected.Creation);
            if (!store.MatchesFoundationSelection(identity.Tenant, identity.Reference, selection))
                return Durable.CreationProtocol.Refuse(input.Actor, selected, input.Taken, "StaleSelection");
        }
        if (message is { Kind: Contract.Kinds.Delete, Operator: not null } && input.Row is { } ended)
        {
            var checkpoint = store.FoundationTurnHeader(ended);
            if (ended.Status == Durable.InstanceStatus.Running)
                return Durable.CreationProtocol.Refuse(input.Actor, ended, input.Taken, "Protected");
            if (checkpoint.Park is not null)
                return message.Operator.Selection is null
                    ? Durable.CreationProtocol.Refuse(input.Actor, ended, input.Taken, "StaleSelection")
                    : OpenParkDelete(input, ended, checkpoint);
            var protection = DeletionRefusal(Header(checkpoint), ended.State, input.Services.Now);
            if (protection is not null)
                return Durable.CreationProtocol.Refuse(input.Actor, ended, input.Taken, protection);

            var marker = Durable.CreationProtocol.Retire(ended, Durable.CascadeRegistry.ForRetirement(ended));
            return new(input.Actor)
            {
                Row = marker, Expected = ended.Revision, Revision = marker.Revision, Status = ended.Status,
                Consumed = [.. input.Taken], Journal = Durable.Json.Write(new[] { "delete" }), Delete = true, Unsubscribe = true
            };
        }
        if (message.Kind == Contract.Kinds.Terminate && input.Row is { } row && store.FoundationTurnHeader(row).Park is { } park)
            return OpenParkTerminate(input, row, park);
        if (message.Kind is Contract.Kinds.Recover or Contract.Kinds.Discard or Contract.Kinds.Delete)
            return Durable.CreationProtocol.Refuse(input.Actor, input.Row, input.Taken, "ResolutionUnavailable");
        if (message.Operator?.DeleteTarget is not null && input.Row is { Status: not Durable.InstanceStatus.Running } endedThenDelete)
        {
            var header = Header(store.FoundationTurnHeader(endedThenDelete));
            var protection = DeletionRefusal(header, endedThenDelete.State, input.Services.Now);
            if (protection is not null)
                return Durable.CreationProtocol.Refuse(input.Actor, endedThenDelete, input.Taken, protection);
            var outcome = ParkCheckpoint(input, endedThenDelete, endedThenDelete);
            if (endedThenDelete.Cascade is null)
                Durable.CascadeProtocol.Open(outcome, Durable.CascadeRegistry.From(endedThenDelete));
            return RetireAfterTerminate(delivery, endedThenDelete, outcome);
        }
        if (message.Kind == Contract.Kinds.Terminate && input.Row is { Status: not Durable.InstanceStatus.Running } terminal)
        {
            if (terminal.Cascade is not null || message.ActorAuthored || message.CascadeOrigin && terminal is { Children: null, Jobs: null })
                return new(input.Actor) { Consumed = [.. input.Taken], Status = terminal.Status };
            if (!message.CascadeOrigin) return null;
            var next = terminal with { Revision = checked(terminal.Revision + 1) };
            return Durable.CascadeProtocol.Open(new(input.Actor)
            {
                Row = next, Expected = terminal.Revision, Revision = next.Revision, Status = terminal.Status,
                Consumed = [.. input.Taken]
            }, Durable.CascadeRegistry.From(terminal));
        }
        if (message.Kind != Contract.Kinds.ChildFinished) return null;

        if (ChildNoticeError(input.Row, message) is { } error)
            return Durable.CreationProtocol.Refuse(input.Actor, input.Row, input.Taken, error);
        return null;
    }

    private Durable.TurnOutcome? RetentionRefusal(Durable.TurnInput input, Durable.InstanceRow row)
    {
        var operation = row.ParkDiscard is { DeletesAtComplete: true } drain ? drain.DeleteOperation : row.ParkRecovery?.Deletion?.Operation;
        if (operation is not null)
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "InProgress", operation);

        var checkpoint = store.FoundationTurnHeader(row);
        if (checkpoint.Park is null && checkpoint.Terminal is { } mark &&
            retentionWindow?.Invoke(Header(checkpoint)) is { Ticks: >= 0 } window &&
            ((decimal)input.Services.Now - mark.At) * TimeSpan.TicksPerMillisecond >= window.Ticks)
            return null;
        return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "NotEligible");
    }

    private Durable.TurnOutcome TimeoutJob(Durable.TurnInput input, Durable.Message message)
    {
        var row = input.Row;
        if (!message.InboxKey.HasIssuedOrigin || row is null ||
            message.Sender != row.Id || message.SenderCreation != row.Creation)
            return new(input.Actor) { Consumed = [.. input.Taken], Status = row?.Status };
        var key = message.Read<Durable.JobDeadline>().Key;
        if (Durable.JobRegistry.Generation(row, key) is not { } generation)
            return new(input.Actor) { Consumed = [.. input.Taken], Status = row.Status };
        var (ticket, attempts) = store.FoundationJob(row.Id, row.Creation, key, generation);
        if (ticket is null && input.VolatileMessage(Durable.InboxKey.Ticket(row.Id, key, row.Creation, generation)) is { Kind: Durable.Kinds.Job } local)
            ticket = local.Read<Durable.JobTicket>();
        if (ticket is null || ticket.Timer != message.Id || ticket.Deadline != message.Due || attempts >= input.MaxAttempts)
            return new(input.Actor) { Consumed = [.. input.Taken], Status = row.Status };

        return SettleJob(input, row, new Durable.JobFinished(key, null, "timeout"), generation, null, cancel: true);
    }

    private Durable.TurnOutcome SettleJob(Durable.TurnInput input, Durable.InstanceRow row, Durable.JobFinished result,
        long generation, string? timer, bool cancel, bool keepCheckpoint = false)
    {
        var turn = new Durable.DurableTurn(input, foundation: true);
        if (cancel) turn.CancelJob(result.Key);
        else if (timer is not null && (store.HasStoredJobDeadline(row.Id, row.Creation, timer) ||
            input.VolatileMessage(Durable.InboxKey.Issued(timer)) is { Kind: Durable.Kinds.JobDeadline })) turn.Recall(timer);
        turn.ReportFoundationJobResult(result, generation);
        // The logical job remains pending until the actor applies or cancels this recorded result.
        var checkpoint = row with { Revision = turn.Revision };
        var outcome = turn.Outcome(row.Status);
        outcome.FoundationHeader = row.FoundationInlineHeader;
        turn.Seal(outcome, checkpoint, input.State, PendingJournal(input.Pending));
        if (keepCheckpoint)
        {
            // A parked job's conversion only replaces its counted ticket. A later Park arrival consumes the result.
            outcome.Row = null;
            outcome.Expected = null;
        }
        return outcome;
    }

    private static string? ChildNoticeError(Durable.InstanceRow? parent, Durable.Message message)
    {
        if (parent is null || message.Target.Id != parent.Id || message.TargetCreation != parent.Creation ||
            message.Sender is not { } child || !Durable.CreationProtocol.HasTerminalNoticeId(message, child, message.SenderCreation))
            return "InvalidChildCompletion";
        if (Durable.ChildRegistry.Read(parent.Children).Contains(new(child, message.SenderCreation))) return null;
        if (message.Payload is null) return "InvalidChildCompletion";
        try
        {
            var notice = message.Read<Durable.FoundationChildNotice>();
            if (notice is { Final: true, Status: Durable.InstanceStatus.Finished or Durable.InstanceStatus.Cancelled or Durable.InstanceStatus.Faulted } &&
                notice.Child == child && notice.Creation == message.SenderCreation)
                return "UnknownChildCreation";
        }
        catch (System.Text.Json.JsonException) { }
        return "InvalidChildCompletion";
    }

    public async ValueTask<Durable.TurnOutcome> TurnAsync(Durable.TurnInput input)
    {
        var header = roots is null
            ? input.Row is { } row ? Header(store.FoundationTurnHeader(row)) : StartHeader(input)
            : input.Row is { } running ? RootHeader(running) : RootStartHeader(input);
        var delivery = input.Taken[input.From];
        var ambient = local is { } host && input.Taken.Count - input.From == 1 && delivery.Message.ReplyTo is { } address
            ? host.Requests.TakeLocalAmbient(address, host.Node, header.Tenant, input.Services.Now, AmbientDelivery(delivery.Message))
            : null;
        var turn = new ActorTurn(inMemory, input, header, ambient, issueSelf);
        input.Started(input.Taken.Skip(input.From).Select(delivery => delivery.Message).ToArray());
        await actor.TurnAsync(turn, CancellationToken.None);
        var thenDelete = delivery.Message.Operator?.DeleteTarget is not null;
        if (thenDelete) turn.EnsureTerminated();
        var outcome = turn.Seal();
        if (outcome.Sent.Count + outcome.Held.Count(outcome.ConvertsHeldJobResult) > Durable.FoundationMetadataLimits.MaxTurnSends)
            throw new InvalidOperationException($"A turn may send at most {Durable.FoundationMetadataLimits.MaxTurnSends:N0} messages.");
        if (roots is not null) RequireRootOutcome(outcome);
        if (outcome.Row is { } nextRow)
        {
            var nextHeader = header with
            {
                Status = (Contract.ActorStatus)nextRow.Status,
                TypeData = outcome.FoundationHeader?.TypeData ?? header.TypeData,
                Labels = outcome.FoundationHeader?.Labels ?? header.Labels
            };
            ValidateTerminalEnvelope(nextRow, nextHeader);
            if (roots is not null)
            {
                // Withholds can run before this turn has landed. Carry its own metadata in the owner's row.
                outcome.Row = nextRow with
                {
                    Suspended = IsRootSuspension(delivery.Message) ? delivery.Message.Kind == Durable.Kinds.Suspend : nextRow.Suspended,
                    FoundationDispatchHeader = new(nextHeader.Tenant, nextHeader.Ref.Id, new(nextHeader.TypeData, nextHeader.Labels))
                };
            }
            if (nextRow.ParkCohort is { } cohort && (nextRow.Status != Durable.InstanceStatus.Running ||
                store.FirstUnheldRecoveryInput(nextRow, cohort, key =>
                    outcome.Consumed.Exists(input => input.Message.InboxKey == key) || outcome.Cancelled.Contains(key)) is null))
                outcome.Row = nextRow with { ParkCohort = null };
            if (nextRow.Status == Durable.InstanceStatus.Running && outcome.FoundationHeader is not null)
                outcome.FoundationWithholds = actor.Withholds(nextHeader);
        }
        return thenDelete && input.Row is { } prior ? FinishThenDelete(input, delivery, prior, header, outcome) : outcome;
    }

    private Durable.TurnOutcome FinishThenDelete(Durable.TurnInput input, Durable.Delivery delivery, Durable.InstanceRow prior,
        Contract.ActorHeader header, Durable.TurnOutcome outcome)
    {
        var ended = outcome.Row ?? throw new InvalidOperationException("Terminate and delete requires an ended checkpoint.");
        var updated = header with
        {
            Status = (Contract.ActorStatus)ended.Status,
            TypeData = outcome.FoundationHeader?.TypeData ?? header.TypeData,
            Labels = outcome.FoundationHeader?.Labels ?? header.Labels
        };
        if (DeletionRefusal(updated, ended.State, input.Services.Now) is null)
            return RetireAfterTerminate(delivery, prior, outcome);
        outcome.ReplyTo(delivery, "terminated and kept");
        return outcome;
    }

    private static Durable.TurnOutcome RetireAfterTerminate(Durable.Delivery delivery, Durable.InstanceRow prior,
        Durable.TurnOutcome outcome)
    {
        outcome.ReplyTo(delivery, "deleted");
        outcome.Row = Durable.CreationProtocol.Retire(outcome.Row! with { Revision = prior.Revision });
        outcome.Journal = Durable.Json.Write(new { Outcome = "deleted", Operation = delivery.Message.Id });
        outcome.FoundationHeader = null;
        outcome.Delete = outcome.Unsubscribe = true;
        return outcome;
    }

    private void ValidateTerminalEnvelope(Durable.InstanceRow row, Contract.ActorHeader header)
    {
        var growth = row.Status == Durable.InstanceStatus.Running ? actor.TerminalGrowth(header, row.State) : 0;
        // This is the built-in in-memory encoding. Keep enough space for the terminal registry even when the live row is smaller.
        var children = row.Children is null ? 0 : Durable.ChildRegistry.Read(row.Children).Count;
        var jobs = Durable.JobRegistry.Read(row.Jobs).Count;
        var registryBytes = Math.Max(Bytes(row.Children), children * (long)Durable.CascadeRegistry.ChildBytes)
            + Math.Max(Bytes(row.Jobs), jobs * (long)Durable.CascadeRegistry.JobBytes)
            + (row.Cascade?.Entries.StorageBytes ?? 0);
        var headerBytes = row.Status == Durable.InstanceStatus.Running ? HeaderLimits.VariableHeaderReserve
            : Bytes(header.TypeData) + header.Labels.Sum(label => Bytes(label.Key) + Bytes(label.Value));
        var bytes = TerminalHeaderReserve + registryBytes + headerBytes
            + Bytes(row.State) + Bytes(row.CustomHandles) + Bytes(row.CommitTimes) + Bytes(row.Model) + Bytes(row.Definition);
        if (growth < 0 || growth > TerminalEnvelopeLimit - bytes)
            throw new InvalidOperationException("The actor checkpoint and its terminal growth exceed the record envelope.");

        static long Bytes(string? value) => value is null ? 0 : Encoding.UTF8.GetByteCount(value);
    }

    private static Durable.InboxKey AmbientDelivery(Durable.Message message) => message.EventMatch is { } match
        ? new(message.Target.Id, Durable.InboxKey.EventDeliveryScope,
            Durable.FoundationEventDelivery.HasDigest(message) ? message.EventStartAmbientKey ?? message.Id : match)
        : message.InboxKey;

    public ValueTask<object?> ExecuteJobAsync(string type, Durable.IJob job, CancellationToken cancellationToken)
    {
        var reference = job is Durable.JobContext { FoundationInlineTenant: { } tenant }
            ? store.ReferenceFor(tenant, new(job.Instance.Id, job.Creation))
            : store.FoundationIdentity(job.Instance.Id, job.Creation).Reference;
        return actor.ExecuteJobAsync(new Job(new(reference), job), cancellationToken);
    }

    public Durable.TurnOutcome Park(Durable.InstanceRow? row, string address, IReadOnlyList<Durable.Delivery> taken,
        Durable.StartSpec? spec, string error)
        => Park(row, address, taken, spec, error, null);

    public Durable.TurnOutcome Park(Durable.InstanceRow? row, string address,
        IReadOnlyList<Durable.Delivery> taken, Durable.StartSpec? spec, string error, object? pending)
    {
        if (row is null && spec is not null && !HeaderLimits.FitsStart(spec.Model, spec.Definition, spec.Input))
            return Durable.CreationProtocol.Refuse(address, null, taken, "StartHeaderTooLarge");

        var result = new Durable.TurnOutcome(address) { Expected = row?.Revision, Spec = spec, Consumed = [.. taken] };
        if (row is null && spec is null)
        {
            result.Status = null;
            return result;
        }
        var next = row ?? NewRow(address, spec!, taken.First(item => item.Message.Start is not null).Message);
        var running = next.Status == Durable.InstanceStatus.Running;
        next = next with
        {
            Status = running ? Durable.InstanceStatus.Faulted : next.Status,
            Fault = running ? BoundFaultText(error) : next.Fault,
            Revision = checked(next.Revision + 1),
            ParkRecovery = null,
            ParkCohort = null
        };
        result.Row = next;
        result.Status = next.Status;
        result.Revision = next.Revision;
        result.Unsubscribe = true;
        if (running || roots is null && row is not null && store.FoundationTurnHeader(row).Park is not null)
            result.FoundationKeep = Durable.ParkKeepReason.Consumed;
        result.Journal = pending is List<object> entries
            ? Durable.Json.Write((object[])[.. entries, "parked"])
            : Durable.Json.Write(new[] { "parked" });
        result.FoundationHeader = next.FoundationInlineHeader ?? (row is null ? InitialHeader(next.Definition) : null);
        if (row?.ParkRecovery is { } recovery)
        {
            result.Cancelled.Add(recovery.Control);
            if (store.FoundationTurnHeader(row).Park is { } mark && store.FoundationExpiry(row) is { } expiry)
                result.Sent.Add(Durable.ParkDrainEnvelope.For(next, mark).Issue(next, checked(recovery.Serial + 1), expiryAt: expiry.At));
            return result;
        }
        if (running && next.Parent is not null) result.Sent.Add(Notice(next, null, final: false));
        return result;
    }

    public Durable.TurnOutcome Write(Durable.InstanceRow row, object? state, object? pending) => new(row.Id)
    {
        Row = row, State = state ?? (row.FoundationNullState ? null : row.State), Status = row.Status, Revision = row.Revision,
        FoundationHeader = row.FoundationInlineHeader ?? InitialHeader(row.Definition),
        Journal = PendingJournal(pending)
    };

    private static string? PendingJournal(object? pending) =>
        pending is List<object> entries ? Durable.Json.Write(entries) : null;

    private static string BoundFaultText(string error)
    {
        if (Encoding.UTF8.GetByteCount(error) <= MaxFaultTextBytes) return error;
        const string suffix = "…";
        Span<byte> prefix = stackalloc byte[MaxFaultTextBytes - 3];
        Encoding.UTF8.GetEncoder().Convert(error.AsSpan(), prefix, flush: true, out var chars, out _, out _);
        return string.Concat(error.AsSpan(0, chars), suffix);
    }

    private Contract.ActorHeader Header(Durable.InMemoryStore.ActorCheckpoint checkpoint)
    {
        var row = checkpoint.Row;
        var tenant = row.FoundationInlineBirth?.Tenant ?? store.FoundationIdentity(row.Id, row.Creation).Tenant;
        return new(new(checkpoint.Reference), row.Model, tenant, (Contract.ActorStatus)row.Status, row.Fault,
            Ref(tenant, row.Parent, row.ParentCreation), checkpoint.EndedAt, checkpoint.Header.TypeData, checkpoint.Header.Labels);
    }

    private Contract.ActorHeader StartHeader(Durable.TurnInput input)
    {
        var spec = input.Spec!;
        var tenant = spec.FoundationInlineTenant ?? store.FoundationIdentity(input.Actor, input.Creation).Tenant;
        var reference = store.ReferenceFor(tenant, new(input.Actor, input.Creation));
        return new(new(reference), spec.Model, tenant, Contract.ActorStatus.Running, null,
            Ref(tenant, spec.Parent, spec.ParentCreation), null, spec.Definition, InitialHeader(spec.Definition).Labels);
    }

    private Contract.ActorRef? Ref(string tenant, string? id, long creation) =>
        store.FoundationReferenceOf(tenant, id, creation) is { } reference ? new(reference) : null;

    private static Durable.InstanceRow NewRow(string id, Durable.StartSpec spec, Durable.Message creator) =>
        new(id, spec.Model, spec.Definition, Durable.InstanceStatus.Running, "null", 0, spec.Parent, History: spec.History,
            Creation: spec.Creation?.Generation ?? 0, ParentCreation: spec.ParentCreation,
            StartIntent: Durable.CreationProtocol.Fingerprint(spec, creator.Kind, creator.Payload))
        {
            FoundationNullState = true,
            FoundationInlineBirth = spec.FoundationInlineTenant is { } tenant ? (tenant, creator.InboxKey) : null
        };

    private static Durable.FoundationHeaderChange InitialHeader(string typeData) =>
        new(typeData, System.Collections.Frozen.FrozenDictionary<string, string>.Empty);

    private static Durable.Message Notice(Durable.InstanceRow row, string? output, bool final) =>
        Durable.FoundationChildNotice.Create(row, output, final);

    private sealed class Job(Contract.ActorRef actor, Durable.IJob job) : Contract.IJob
    {
        public Contract.ActorRef Actor => actor;
        public string Key => job.Key;
        public string? Arguments => job.Arguments;
        public string AttemptId => job.AttemptId ?? throw new InvalidOperationException("A job execution needs an attempt.");
        public int AttemptNumber => job.AttemptNumber;
        public long? Deadline => job.Deadline;
    }
}
