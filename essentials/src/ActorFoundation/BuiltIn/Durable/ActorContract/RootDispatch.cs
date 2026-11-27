using Contract = Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Durable;

/// <summary>Qualified read results supplied by the store for a generated root. Reads belong to the
/// same admitted creation or loaded revision as the envelope or checkpoint passed here.</summary>
internal interface IRootDispatchHeaders
{
    (string Tenant, string Reference) Creator(Durable.Message creator);
    Durable.FoundationDispatchHeader Checkpoint(Durable.InstanceRow row);
    bool SupportsChildren => false;
    Func<string, string>? ChildMessageHandle => null;
    string? ChildReference(string tenant, Durable.CreationAddress address) => null;
    bool RequiresCommittedTerminal { get; }
    bool KeepsParkInputs { get; }
    bool KeepsParkArrivals { get; }
    bool RecoversPark { get; }
    bool RefusesParkedOperators { get; }
    bool SettlesEndedMailbox { get; }
}

internal sealed partial class ActorAdapter
{
    private Contract.ActorHeader RootStartHeader(Durable.TurnInput input)
    {
        var spec = input.Spec!;
        var (tenant, reference) = roots!.Creator(input.Taken[input.From].Message);
        return new(new(reference), spec.Model, tenant, Contract.ActorStatus.Running, null,
            RootParent(tenant, spec.Parent, spec.ParentCreation), null, spec.Definition, InitialHeader(spec.Definition).Labels);
    }

    private Contract.ActorHeader RootHeader(Durable.InstanceRow row)
    {
        var metadata = row.FoundationDispatchHeader ?? roots!.Checkpoint(row);
        return new(new(metadata.Reference), row.Model, metadata.Tenant, (Contract.ActorStatus)row.Status, row.Fault,
            RootParent(metadata.Tenant, row.Parent, row.ParentCreation), metadata.Terminal?.At, metadata.Header.TypeData, metadata.Header.Labels);
    }

    private Contract.ActorRef? RootParent(string tenant, string? parent, long creation) =>
        parent is not null && roots!.ChildReference(tenant, new(parent, creation)) is { } reference ? new(reference) : null;

    private static bool IsRootControl(Durable.Message message) => message is
    {
        Kind: Durable.Kinds.Suspend or Durable.Kinds.Resume or Durable.Kinds.Terminate or Durable.Kinds.Delete,
        InboxScope: Durable.InboxKey.FoundationOccurrenceScope, StorageId: not null, TargetCreation: 1, KeepUntil: not null,
        Operator: { Selection: null, RecoverMode: null, RecoverHandles: null },
        Start: null, Payload: null, EventMatch: null, Sender: null, SenderCreation: 0, ActorAuthored: false, JobGeneration: 0
    } && (message.Kind == Durable.Kinds.Delete
            // S5b-3b: a Delete names the root's terminal mark, or the Park of a parked root.
            ? (message.Operator.QualifiedTerminal is not null) != (message.Operator.RootParkMark is not null)
            : message.Operator.QualifiedTerminal is null)
        && (message.ReplyTo is not null) == message.Operator.RootHeldReply
        && (message.ReplyTo is null || !message.Operator.RetentionDelete &&
            message.Kind is Durable.Kinds.Terminate or Durable.Kinds.Delete &&
            Durable.ReplyTo.Parse(message.ReplyTo) is { BeforeCommit: false })
        && (!message.Operator.RetentionDelete || message.Kind == Durable.Kinds.Delete)
        && (message.Operator.RootThenDelete ? message.Kind == Durable.Kinds.Terminate && message.Operator.DeleteTarget is not null
            : message.Operator.DeleteTarget is null);

    private bool IsRootRecover(Durable.Message message) => roots?.RecoversPark == true && message is
    {
        Kind: Contract.Kinds.Recover, InboxScope: Durable.InboxKey.FoundationOccurrenceScope, StorageId: not null, TargetCreation: 1,
        KeepUntil: not null, ReplyTo: null, Start: null, Payload: null, EventMatch: null, Sender: null, SenderCreation: 0,
        ActorAuthored: false, JobGeneration: 0,
        Operator: { Selection: null, RecoverMode: Durable.ParkRecoveryMode.Resume, RecoverHandles: null, QualifiedTerminal: null,
            DeleteTarget: null, RootThenDelete: false, RetentionDelete: false, RootHeldReply: false, RootParkMark: not null, RootParkWindow: >= 0, RootParkFacts: not null }
    };

    // One page of a qualified root's kept set resolves in one fenced commit: the store restores the arrivals and clears the
    // fault input. Any other case keeps the Park, consumes the command and journals a refusal at the next revision.
    private Durable.TurnOutcome RecoverRootPark(Durable.TurnInput input, Durable.InstanceRow row, Durable.Delivery recover)
    {
        var message = recover.Message;
        var facts = message.Operator!.RootParkFacts!;
        var reason = message.Target.Id != row.Id || row.Creation != 1 ? "target"
            : row is not { Status: Durable.InstanceStatus.Faulted, Retired: false, Suspended: false } ||
                facts.Mark != message.Operator.RootParkMark ? "selection"
            : facts.Clearing is not null ? "clearing"
            : !CanRecoverRootWaits(row, facts) ? "plan" : !facts.FitsPage ? "page" : null;
        var revision = checked(row.Revision + 1);
        var next = reason is null ? row with
            { Status = Durable.InstanceStatus.Running, Fault = null, Jobs = facts.KeepsRegisteredJob ? row.Jobs : null, Revision = revision }
            : row with { Revision = revision };
        return new(input.Actor)
        {
            Expected = row.Revision, Row = next, Revision = revision, Status = next.Status, Consumed = [recover],
            Journal = reason is null
                ? Durable.Json.Write(new { Outcome = "recovered", Operation = message.Id, Restored = facts.Arrived, Cleared = facts.Consumed, AlreadyRearmed = 0 })
                : Durable.Json.Write(new { Outcome = "refused", Operation = message.Id, Reason = reason })
        };
    }

    // S5a: a composed Park set delivers an operator command to a parked root. A Park keeps the row's Suspended flag
    // (ActorAdapter.Park), so a root parked while suspended is reached the same way. S5b: a Terminate that names the
    // root's Park ends it in one fenced commit when its kept rows fit one page, as the in-memory drain's Complete does:
    // Cancelled and no longer suspended, journalled "terminated" with the rows it cleared, and a held caller answered once.
    // Any other command is consumed and refused with a journalled reason at the next revision, and the Park and checkpoint
    // stay as they are: "parked" for one that names no Park (admitted before the Park opened), "selection" for a stale
    // Park, "outstanding" for a row with jobs, children or custom handles (the end cannot cancel or cascade them yet, and
    // retirement refuses them), "clearing" for one that names a Park being cleared. A held caller of a refused command is
    // answered with the refusal. S5b-2: a Park larger than one page opens a clearing ("clearing" entry, held caller answered
    // "started"); each store control clears the next page, and the page that leaves nothing kept ends the root.
    private Durable.TurnOutcome? RefuseOnParkedRoot(Durable.TurnInput input)
    {
        if (roots is not { RefusesParkedOperators: true } || input.Row is not
            { Status: Durable.InstanceStatus.Faulted, Retired: false, Creation: 1, FoundationDispatchHeader: { Terminal: null } header } row ||
            input.Taken is not [{ Durable: true, Message: var control } refused] ||
            !IsRootControl(control) || control.Target.Id != row.Id) return null;
        var revision = checked(row.Revision + 1);
        var (mark, facts, clear) = (control.Operator!.RootParkMark, control.Operator.RootParkFacts, control.Operator.RootParkClear);
        var deletes = control.Kind == Durable.Kinds.Delete || control.Operator.RootThenDelete;
        var reason = control.Kind is not (Durable.Kinds.Terminate or Durable.Kinds.Delete) || mark is null || facts is null ? "parked"
            : clear ? facts.Clearing != control.Id || facts.Mark != mark ? "selection" : null
            : facts.Clearing is not null ? "clearing" : facts.Mark != mark ? "selection"
            : facts.Outstanding || deletes && facts.Residue ? "outstanding" : null;
        // S5b-3b: a Delete runs ProtectsDeletion at its Open, as the in-memory OpenParkDelete does. A protected root, or one
        // whose protection cannot be read, is refused with its Park and kept rows as they are. A single page is its own Open.
        if (reason is null && !clear && control.Kind == Durable.Kinds.Delete)
            reason = DeletionRefusal(RootHeader(row), row.State, input.Services.Now) switch
            {
                null => null, "Protected" => "protected", _ => "unavailable"
            };
        if (reason is null && !facts!.FitsPage)
        {
            var page = new Durable.TurnOutcome(input.Actor)
            {
                Expected = row.Revision, Row = row with { Revision = revision }, Revision = revision, Status = row.Status, Consumed = [refused],
                Journal = clear ? null : Durable.Json.Write(new { Outcome = "clearing", Operation = control.Id, Cleared = facts.Page })
            };
            page.ReplyTo(refused, "started");
            return page;
        }
        if (reason is null)
        {
            var cleared = facts!.Cleared + facts.Arrived + facts.Consumed;
            if (control.Kind == Durable.Kinds.Delete) return DeleteParkedRoot(input, row, refused, cleared, clear);
            var ended = row with
            {
                Status = Durable.InstanceStatus.Cancelled, Suspended = false, Revision = revision,
                FoundationDispatchHeader = header with { RefreshTerminal = roots.RequiresCommittedTerminal }
            };
            // S5b-3a: a Terminate-then-delete retires the ended root in the same commit, unless the row is protected then;
            // a protected row is terminated and kept, as the in-memory drain's Complete does.
            var kept = control.Operator.RootThenDelete && DeletionRefusal(RootHeader(ended), row.State, input.Services.Now) is not null;
            if (control.Operator.RootThenDelete && !kept)
            {
                var marker = Durable.CreationProtocol.Retire(row with { Status = Durable.InstanceStatus.Cancelled });
                var deleted = new Durable.TurnOutcome(input.Actor)
                {
                    Row = marker, Expected = row.Revision, Revision = marker.Revision, Status = marker.Status, Consumed = [refused],
                    Journal = """["delete"]""", Delete = true, Unsubscribe = true
                };
                deleted.ReplyTo(refused, "deleted");
                return deleted;
            }
            var terminated = new Durable.TurnOutcome(input.Actor)
            {
                Expected = row.Revision, Row = ended, Revision = revision, Status = ended.Status, Consumed = [refused], Unsubscribe = true,
                Journal = kept ? Durable.Json.Write(new { Outcome = "protected", Disposition = "terminated", Operation = control.Id, Cleared = cleared })
                    : Durable.Json.Write(new { Outcome = "terminated", Operation = control.Id, Cleared = cleared })
            };
            terminated.ReplyTo(refused, kept ? "terminated and kept" : "terminated");
            return terminated;
        }
        var outcome = new Durable.TurnOutcome(input.Actor)
        {
            Expected = row.Revision, Row = row with { Revision = revision }, Revision = revision, Status = row.Status, Consumed = [refused],
            Journal = Durable.Json.Write(new { Outcome = "refused", Operation = control.Id, Reason = reason })
        };
        if (control.ReplyTo is not null)
            outcome.ReplyTo(refused, new Durable.CreationResult(input.Actor, reason switch
            {
                "selection" => "StaleSelection", "outstanding" => "ParkOutstanding", "clearing" => "ResolutionOpen",
                "protected" => "Protected", "unavailable" => "ProtectionUnavailable", _ => "Parked"
            }, control.TargetCreation, row.Creation), creationRefusal: true);
        return outcome;
    }

    // S5b-3b: a Delete of a parked root has no end of its own (observability, "Delete during a drain"). The commit that
    // leaves nothing kept retires the root still Faulted. A single page checked protection at its Open; the last page of a
    // clearing runs ProtectsDeletion once more, as the in-memory Complete does. Protection that appeared during the clearing
    // resolves the Park as a discard would (DT-4, the end state only): Faulted under a new terminal mark, journalled as
    // protected. An answer that cannot be read neither deletes nor discards: the control is refused as "unavailable", the
    // clearing stays open, and the store inserts the next control, due after a capped backoff.
    private Durable.TurnOutcome DeleteParkedRoot(Durable.TurnInput input, Durable.InstanceRow row, Durable.Delivery command, long cleared,
        bool completes)
    {
        var revision = checked(row.Revision + 1);
        var protection = completes ? DeletionRefusal(RootHeader(row), row.State, input.Services.Now) : null;
        if (protection is null)
        {
            var marker = Durable.CreationProtocol.Retire(row);
            var deleted = new Durable.TurnOutcome(input.Actor)
            {
                Row = marker, Expected = row.Revision, Revision = marker.Revision, Status = marker.Status, Consumed = [command],
                Journal = """["delete"]""", Delete = true, Unsubscribe = true
            };
            deleted.ReplyTo(command, "deleted");
            return deleted;
        }
        var unavailable = protection != "Protected";
        var facts = command.Message.Operator!.RootParkFacts!;
        return new Durable.TurnOutcome(input.Actor)
        {
            Expected = row.Revision, Row = row with
            {
                Revision = revision,
                FoundationDispatchHeader = unavailable ? row.FoundationDispatchHeader
                    : row.FoundationDispatchHeader! with { RefreshTerminal = roots!.RequiresCommittedTerminal }
            },
            Revision = revision, Status = row.Status, Consumed = [command], Unsubscribe = !unavailable,
            Journal = unavailable
                ? facts.StopsForUnreadableProtection ? Durable.FoundationRootParkFacts.ProtectionStopped(command.Message.Id, facts.Cleared)
                    : Durable.FoundationRootParkFacts.ProtectionRefusal(command.Message.Id)
                : Durable.Json.Write(new { Outcome = "protected", Disposition = "discarded", Operation = command.Message.Id, Cleared = cleared })
        };
    }

    // An ended root settles a command which names its old Park without a journal entry. A recovered root must
    // also refuse the old selection, but its running checkpoint records the outcome before releasing the caller.
    private Durable.TurnOutcome? RefuseStaleParkCommand(Durable.TurnInput input)
    {
        if (roots is not { RefusesParkedOperators: true } ||
            input.Row is not { Retired: false, Creation: 1, FoundationDispatchHeader: { } header } row ||
            input.Taken is not [{ Durable: true, Message: { Operator.RootParkMark: not null } control }] ||
            !IsRootControl(control) || control.Target.Id != row.Id) return null;
        if (row.Status != Durable.InstanceStatus.Running)
            return header.Terminal is not null ? Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "StaleSelection") : null;
        if (!roots.RecoversPark || header.Terminal is not null ||
            control is not { Kind: Durable.Kinds.Terminate or Durable.Kinds.Delete, Operator.RootParkClear: false }) return null;
        var refused = Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "StaleSelection");
        refused.Revision = checked(row.Revision + 1);
        refused.Expected = row.Revision;
        refused.Row = row with { Revision = refused.Revision };
        refused.Journal = Durable.Json.Write(new { Outcome = "refused", Operation = control.Id, Reason = "selection" });
        return refused;
    }

    private bool CanRecoverRootWaits(Durable.InstanceRow row, Durable.FoundationRootParkFacts facts)
    {
        if (row.Jobs != facts.RecoverableJobRegistry) return false;
        try
        {
            var plan = actor.RestoreWaits(RootHeader(row), row.State);
            if (plan is not { Timers.Count: 0, Subscriptions.Count: 0 }) return false;
            var registered = Durable.JobRegistry.Read(row.Jobs);
            return registered.Count == 0 ? plan.Jobs.Count == 0 :
                registered is [var job] && plan.Jobs is [var recipe] && job.Key == recipe.Key &&
                job.Generation >= 0 && job.Generation < (row.NextJobGeneration ?? 1) &&
                Durable.InMemoryStore.FitsSingleParkJobRecipe(row, new(recipe.Key, recipe.Arguments, recipe.Deadline));
        }
        catch (Exception) { return false; }
    }

    private Durable.TurnOutcome? SettleRootDeletion(Durable.TurnInput input)
    {
        if (input.Taken is not [{ Durable: true, Message: { Operator: { } command } message }] ||
            message.Kind != Durable.Kinds.Delete && !command.RootThenDelete) return null;
        var row = input.Row;
        if (row is not { Creation: 1, Retired: false, Parent: null, Children: null, Jobs: null, FoundationDispatchHeader: { } header } ||
            row.Model != actor.Type || row.Id != message.Target.Id || message.TargetCreation != row.Creation ||
            (command.RootThenDelete ? command.DeleteTarget != header.Reference : header.Terminal is null || header.Terminal != command.QualifiedTerminal))
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "StaleSelection");
        if (row.Status == Durable.InstanceStatus.Running)
            return command.RootThenDelete ? null : Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "StaleSelection");
        if (header.Terminal is null)
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "StaleSelection");

        var current = RootHeader(row);
        var now = input.Services.Now;
        if (command.RetentionDelete && !RetentionElapsed(current, header.Terminal.At, now))
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "NotEligible");
        var protection = DeletionRefusal(current, row.State, now);
        if (protection is not null)
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, protection);
        var marker = Durable.CreationProtocol.Retire(row);
        var outcome = new Durable.TurnOutcome(input.Actor)
        {
            Row = marker, Expected = row.Revision, Revision = marker.Revision, Status = marker.Status,
            Consumed = [.. input.Taken], Journal = """["delete"]""", Delete = true, Unsubscribe = true
        };
        outcome.ReplyTo(input.Taken[0], "deleted");
        return outcome;
    }

    private string? RootRefusal(Durable.TurnInput input)
    {
        if ((input.Row?.Model ?? input.Spec?.Model) != actor.Type) return "UnknownActorType";
        if (input.Row is { Status: not Durable.InstanceStatus.Running }) return "Ended";
        if (input.Row is { } row && (row.Creation != 1 || row.Retired ||
            row.ParkRecovery is not null || row.ParkCohort is not null || row.ParkDiscard is not null || row.Cascade is not null ||
            rootJobHandle is null && row.Jobs is not null || roots?.SupportsChildren != true && (row.Children is not null || row.Parent is not null)))
            return "RootDispatchUnavailable";

        if (input.Row is not null && input.Taken is [var command] && command.Durable && IsRootControl(command.Message) &&
            command.Message.Target.Id == input.Actor) return null;

        if (rootJobHandle is not null && input.Row is { } bound && input.Taken is [var job] &&
            (Durable.RootJobTicket.IsCompletion(job, bound) ||
                rootTimedJobSettlement && job.Durable && Durable.RootJobTicket.IsRecordedResult(job.Message, bound))) return null;

        foreach (var delivery in input.Taken)
        {
            var message = delivery.Message;
            if (issueSelf is not null && issueSelf.Allows(message.Due) && delivery.Durable && input.Row is { } issuer &&
                Durable.FoundationSelfSend.IsSelfInput(message, issuer.Id, issuer.Creation)) continue;
            if (roots?.SupportsChildren == true && message.FoundationRootChildNotice && delivery.Durable)
            {
                if (ChildNoticeError(input.Row, message) is { } error) return error;
                continue;
            }
            if (roots?.SupportsChildren == true && message.FoundationRootChildInput && delivery.Durable)
            {
                if (input.Row is not { Parent: not null } child || message.Target.Id != child.Id ||
                    message.TargetCreation != child.Creation || message.Sender != child.Parent ||
                    message.SenderCreation != child.ParentCreation) return "WrongParent";
                continue;
            }
            var childStart = roots?.SupportsChildren == true && message.BindFoundationChild && message.Start is not null;
            if (!delivery.Durable || message.Target.Id != input.Actor || message.TargetCreation != 1 ||
                message.Sender is not null && !childStart || message.EventMatch is not null ||
                message.ActorAuthored || message.Operator is not null)
                return "RootDispatchUnavailable";
            // A held caller is answered on a start as on a direct input. On a start this check is structural: the poll takes
            // both the caller's tenant and the root's from the root's birth binding. Isolation holds where the route is
            // made and where it lands: admission writes a route only for a request of the root's own tenant, and
            // Requests.TryReceive refuses a reply whose tenant is not its caller's. After the start, the caller's tenant
            // is checked against the checkpoint's header.
            if (message.ReplyTo is not null && (!rootHeldReplies || delivery.ReplyTenant is not { } callerTenant ||
                (message.Start is not null ? message.FoundationRootBinding?.Tenant : input.Row?.FoundationDispatchHeader?.Tenant) != callerTenant))
                return "RootDispatchUnavailable";
            if (message.Start is not null)
            {
                if (input.Row is not null || message.Start is not { Creation: { Generation: 1, Previous: 0 }, Ephemeral: false } ||
                    message.Start.Parent is not null && !childStart || message.InboxScope != 0 || message.Kind != Durable.Kinds.Start || message.StorageId != "root:" + input.Actor)
                    return "RootDispatchUnavailable";
            }
            else if (message.InboxScope != Durable.InboxKey.FoundationOccurrenceScope || message.StorageId is null ||
                message.Kind.StartsWith("kernel.", StringComparison.Ordinal))
                return "RootDispatchUnavailable";
        }
        return null;
    }

    private void RequireRootOutcome(Durable.TurnOutcome outcome, Durable.InstanceRow? previous, Durable.Message input)
    {
        if (roots?.SupportsChildren == true && outcome.Sent.Any(IsChildEffect))
        {
            // The qualified writer commits these rows. Their recipients obtain the stored envelope through polling.
            outcome.FoundationRootChildSends = outcome.Sent.Where(IsChildEffect).ToArray();
            outcome.Sent.RemoveAll(IsChildEffect);
            if (outcome.Row is { } childRow) outcome.Row = childRow with { CustomHandles = null };
        }

        var timedFault = outcome.Row is { Status: Durable.InstanceStatus.Faulted } && rootTimedJobSettlement &&
            previous is not null && !outcome.OpensPark &&
            outcome.Consumed is [{ Durable: true, Message: var result }] &&
            Durable.RootJobTicket.IsRecordedResult(result, previous) &&
            result.Read<Durable.JobFinished>() is { Error: "timeout", Result: null };
        var terminated = previous is not null && outcome.Row is { Status: Durable.InstanceStatus.Cancelled } &&
            (input.Kind == Durable.Kinds.Terminate && IsRootControl(input) ||
                roots?.SupportsChildren == true && input.FoundationRootChildInput);
        // A profile whose state family ends Faulted roots (timed-job settlement) commits a model's Faulted end, as the
        // in-memory reference does: its held callers are answered with that end (Worker.Release).
        var faulted = rootTimedJobSettlement && !outcome.OpensPark && outcome.Row is { Status: Durable.InstanceStatus.Faulted };
        // A Faulted end issues no job: a job its own turn started is dropped before the store sees it (its ticket and deadline
        // were never sent), and so is the generation it took.
        if (faulted && previous?.Jobs is null && outcome.Row is { } ended)
        {
            if (outcome.Sent.Count != 0 && Durable.RootJobTicket.StartsTimedPair(outcome.Sent, ended.Id, ended.Model)) outcome.Sent.Clear();
            if (outcome.Sent.Count == 0) outcome.Row = ended with { Jobs = null, NextJobGeneration = previous?.NextJobGeneration ?? 0 };
        }
        // A Terminate, or any end of the model's own, settles the root's outstanding timed jobs in its terminal commit: its
        // cascade names only jobs and ends in one page, and the store removes the jobs' ticket and deadline rows there.
        var finished = !outcome.OpensPark && outcome.Row is { Status: Durable.InstanceStatus.Finished };
        var settlesJobs = (terminated || faulted || finished) && rootTimedJobSettlement && previous is { Jobs: not null } &&
            outcome.Row is { Jobs: null, Cascade: null, Children: null } &&
            outcome.FoundationCascade is { Entries.Children.IsEmpty: true, Entries.Jobs.IsEmpty: false, Input: null } && outcome.Sent.Count == 0;
        if (outcome.Row is not { } next ||
            next.Status is not (Durable.InstanceStatus.Running or Durable.InstanceStatus.Finished) && !timedFault && !terminated && !faulted ||
            !((rootTimedJobIssuance || rootTimedJobSettlement) && Durable.RootJobTicket.StartsTimedPair(outcome.Sent, next.Id, next.Model)) &&
                outcome.Sent.Any(m => !SupportedSend(m, next)) ||
            outcome.Held.Count != 0 || (issueSelf is null && outcome.Cancelled.Count != 0) ||
            outcome.Subscribed.Count != 0 || outcome.Unsubscribed.Count != 0 || outcome.Replied.Count != 0 ||
            // Emitted output is live: the owner releases it to the callers it holds for the root, whichever turn consumed
            // their delivery, and drops it when it holds none (Worker.Release), as the in-memory reference does.
            outcome.FoundationCascade is not null && !settlesJobs || outcome.InMemory)
            throw new NotSupportedException("This provider supports checkpoint, journal and terminal cleanup for ordinary roots.");
        if (outcome.Cancelled.Count > 0) outcome.FoundationSelfRecalls = outcome.Cancelled.ToHashSet();

        static bool IsChildEffect(Durable.Message message) =>
            message.BindFoundationChild || message.FoundationRootChildInput || message.Kind == Durable.Kinds.ChildFinished;

        bool SupportedSend(Durable.Message message, Durable.InstanceRow row) =>
            rootJobHandle is not null && message.HasIssuedTicket && Durable.RootJobTicket.Read(message, row.Id, row.Model) is not null ||
            issueSelf is { } issuer && issuer.Allows(message.Due) && Durable.FoundationSelfSend.IsSelfInput(message, row.Id, row.Creation);
    }

    private Durable.TurnOutcome? SettleRootTimedJob(Durable.TurnInput input)
    {
        if (!rootTimedJobSettlement || input.Row is not { Status: Durable.InstanceStatus.Running, Creation: 1 } row ||
            input.Taken is not [var item] || !item.Durable) return null;
        if (Durable.RootJobTicket.IsCompletion(item, row, timed: true))
        {
            var original = item.Original!;
            return SettleJob(input, row, item.Message.Read<Durable.JobFinished>(), original.JobGeneration,
                original.Read<Durable.JobTicket>().Timer, cancel: false, root: true);
        }
        if (item.Message is { Kind: Durable.Kinds.JobDeadline, Due: { } due } deadline &&
            deadline.Read<Durable.JobDeadline>() is { Key: var key } && Durable.JobRegistry.Generation(row, key) is { } generation &&
            deadline.WithoutTicketAuthority() == Durable.RootJobTicket.Deadline(row.Id, key, generation, due))
            return SettleJob(input, row, new Durable.JobFinished(key, null, "timeout"), generation, null, cancel: true, root: true);
        return null;
    }
}
