using Contract = Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

/// <summary>Qualified read results supplied by the store for a generated root. Reads belong to the
/// same admitted creation or loaded revision as the envelope or checkpoint passed here.</summary>
internal interface IRootDispatchHeaders
{
    (string Tenant, string Reference) Creator(Durable.Message creator);
    Durable.FoundationDispatchHeader Checkpoint(Durable.InstanceRow row);
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
            null, null, metadata.Header.TypeData, metadata.Header.Labels);
    }

    private string? RootRefusal(Durable.TurnInput input)
    {
        if ((input.Row?.Model ?? input.Spec?.Model) != actor.Type) return "UnknownActorType";
        if (input.Row is { Status: not Durable.InstanceStatus.Running }) return "Ended";
        if (input.Row is { } row && (row.Creation != 1 || row.Retired || row.Parent is not null ||
            row.ParkRecovery is not null || row.ParkCohort is not null || row.ParkDiscard is not null || row.Cascade is not null ||
            row.Jobs is not null || row.Children is not null))
            return "RootDispatchUnavailable";

        foreach (var delivery in input.Taken)
        {
            var message = delivery.Message;
            if (issueSelf is not null && delivery.Durable && input.Row is { } issuer &&
                Durable.FoundationSelfSend.IsImmediate(message, issuer.Id, issuer.Creation)) continue;
            if (!delivery.Durable || message.Target.Id != input.Actor || message.TargetCreation != 1 ||
                message.Sender is not null || message.EventMatch is not null ||
                message.ActorAuthored || message.Operator is not null || message.ReplyTo is not null)
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

    private void RequireRootOutcome(Durable.TurnOutcome outcome)
    {
        if (outcome.Row is not { Status: Durable.InstanceStatus.Running or Durable.InstanceStatus.Finished } ||
            outcome.Sent.Any(m => issueSelf is null || !Durable.FoundationSelfSend.IsImmediate(m, outcome.Row.Id, outcome.Row.Creation)) ||
            outcome.Held.Count != 0 || outcome.Cancelled.Count != 0 ||
            outcome.Subscribed.Count != 0 || outcome.Unsubscribed.Count != 0 || outcome.Replied.Count != 0 ||
            outcome.Emitted.Count != 0 || outcome.FoundationCascade is not null || outcome.InMemory)
            throw new NotSupportedException("This provider supports checkpoint, journal and terminal cleanup for ordinary roots.");
    }
}
