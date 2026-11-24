using Contract = Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Durable;

/// <summary>Qualified read results supplied by the store for a generated root. Reads belong to the
/// same admitted creation or loaded revision as the envelope or checkpoint passed here.</summary>
internal interface IRootDispatchHeaders
{
    (string Tenant, string Reference) Creator(Durable.Message creator);
    Durable.FoundationDispatchHeader Checkpoint(Durable.InstanceRow row);
    bool RequiresCommittedTerminal { get; }
    bool KeepsParkInputs { get; }
    bool KeepsParkArrivals { get; }
    bool RecoversPark { get; }
}

internal sealed partial class ActorAdapter
{
    private Contract.ActorHeader RootStartHeader(Durable.TurnInput input)
    {
        var spec = input.Spec!;
        var (tenant, reference) = roots!.Creator(input.Taken[input.From].Message);
        return new(new(reference), spec.Model, tenant, Contract.ActorStatus.Running, null, null, null,
            spec.Definition, InitialHeader(spec.Definition).Labels);
    }

    private Contract.ActorHeader RootHeader(Durable.InstanceRow row)
    {
        var metadata = row.FoundationDispatchHeader ?? roots!.Checkpoint(row);
        return new(new(metadata.Reference), row.Model, metadata.Tenant, (Contract.ActorStatus)row.Status, row.Fault,
            null, metadata.Terminal?.At, metadata.Header.TypeData, metadata.Header.Labels);
    }

    private static bool IsRootControl(Durable.Message message) => message is
    {
        Kind: Durable.Kinds.Suspend or Durable.Kinds.Resume or Durable.Kinds.Terminate or Durable.Kinds.Delete,
        InboxScope: Durable.InboxKey.FoundationOccurrenceScope, StorageId: not null, TargetCreation: 1, KeepUntil: not null,
        Operator: { Selection: null, RecoverMode: null, RecoverHandles: null },
        Start: null, Payload: null, EventMatch: null, Sender: null, SenderCreation: 0, ActorAuthored: false, JobGeneration: 0
    } && (message.Kind == Durable.Kinds.Delete ? message.Operator.QualifiedTerminal is not null : message.Operator.QualifiedTerminal is null)
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
            DeleteTarget: null, RootThenDelete: false, RetentionDelete: false, RootHeldReply: false, RootParkMark: not null, RootParkFacts: not null }
    };

    // One page of a qualified root's kept set resolves in one fenced commit: the store restores the arrivals and clears the
    // fault input. Any other case keeps the Park, consumes the command and journals a refusal at the next revision.
    private Durable.TurnOutcome RecoverRootPark(Durable.TurnInput input, Durable.InstanceRow row, Durable.Delivery recover)
    {
        var message = recover.Message;
        var facts = message.Operator!.RootParkFacts!;
        var reason = row is not { Status: Durable.InstanceStatus.Faulted, Creation: 1, Retired: false, Suspended: false } ||
            message.Target.Id != row.Id || facts.Mark != message.Operator.RootParkMark ? "selection"
            : !RestoresNothing(row) ? "plan" : !facts.FitsPage ? "page" : null;
        var revision = checked(row.Revision + 1);
        var next = reason is null ? row with { Status = Durable.InstanceStatus.Running, Fault = null, Revision = revision } : row with { Revision = revision };
        return new(input.Actor)
        {
            Expected = row.Revision, Row = next, Revision = revision, Status = next.Status, Consumed = [recover],
            Journal = reason is null
                ? Durable.Json.Write(new { Outcome = "recovered", Operation = message.Id, Restored = facts.Arrived, Cleared = facts.Consumed, AlreadyRearmed = 0 })
                : Durable.Json.Write(new { Outcome = "refused", Operation = message.Id, Reason = reason })
        };
    }

    private bool RestoresNothing(Durable.InstanceRow row)
    {
        try { return actor.RestoreWaits(RootHeader(row), row.State) is { Timers.Count: 0, Jobs.Count: 0, Subscriptions.Count: 0 }; }
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
        if (input.Row is { } row && (row.Creation != 1 || row.Retired || row.Parent is not null ||
            row.ParkRecovery is not null || row.ParkCohort is not null || row.ParkDiscard is not null || row.Cascade is not null ||
            rootJobHandle is null && row.Jobs is not null || row.Children is not null))
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
            if (!delivery.Durable || message.Target.Id != input.Actor || message.TargetCreation != 1 ||
                message.Sender is not null || message.EventMatch is not null ||
                message.ActorAuthored || message.Operator is not null)
                return "RootDispatchUnavailable";
            if (message.ReplyTo is not null && (!rootHeldReplies || message.Start is not null ||
                input.Row?.FoundationDispatchHeader is not { } qualified || delivery.ReplyTenant != qualified.Tenant))
                return "RootDispatchUnavailable";
            if (message.Start is not null)
            {
                if (input.Row is not null || message.Start is not { Parent: null, Creation: { Generation: 1, Previous: 0 }, Ephemeral: false } ||
                    message.InboxScope != 0 || message.Kind != Durable.Kinds.Start || message.StorageId != "root:" + input.Actor)
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
        var timedFault = outcome.Row is { Status: Durable.InstanceStatus.Faulted } && rootTimedJobSettlement &&
            previous is not null && !outcome.OpensPark &&
            outcome.Consumed is [{ Durable: true, Message: var result }] &&
            Durable.RootJobTicket.IsRecordedResult(result, previous) &&
            result.Read<Durable.JobFinished>() is { Error: "timeout", Result: null };
        var terminated = input.Kind == Durable.Kinds.Terminate && IsRootControl(input) && previous is not null &&
            outcome.Row is { Status: Durable.InstanceStatus.Cancelled };
        if (outcome.Row is not { } next ||
            next.Status is not (Durable.InstanceStatus.Running or Durable.InstanceStatus.Finished) && !timedFault && !terminated ||
            !((rootTimedJobIssuance || rootTimedJobSettlement) && Durable.RootJobTicket.StartsTimedPair(outcome.Sent, next.Id, next.Model)) &&
                outcome.Sent.Any(m => !SupportedSend(m, next)) ||
            outcome.Held.Count != 0 || (issueSelf is null && outcome.Cancelled.Count != 0) ||
            outcome.Subscribed.Count != 0 || outcome.Unsubscribed.Count != 0 || outcome.Replied.Count != 0 ||
            outcome.Emitted.Count != 0 && (!rootHeldReplies || !outcome.Consumed.Exists(delivery => delivery.ReplyRoute is not null)) ||
            outcome.FoundationCascade is not null || outcome.InMemory)
            throw new NotSupportedException("This provider supports checkpoint, journal and terminal cleanup for ordinary roots.");
        if (outcome.Cancelled.Count > 0) outcome.FoundationSelfRecalls = outcome.Cancelled.ToHashSet();

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
