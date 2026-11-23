using Contract = Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

internal sealed partial class ActorAdapter
{
    // The provider supplies the bound checkpoint and one admitted, due outside occurrence.
    // This stage can persist a silent header-only turn. The caller owns the physical fence.
    internal static async ValueTask<Durable.FoundationHeaderChange> StageRootSuspensionAsync(Contract.IActorType actor,
        Durable.Worker worker, Contract.ActorHeader header, Durable.InstanceRow row, Durable.Message command, CancellationToken token)
    {
        if (header.Type != actor.Type || header.Type != row.Model || header.Status != Contract.ActorStatus.Running ||
            row.Status != Durable.InstanceStatus.Running || row.Parent is not null || row.Creation != 1 ||
            command is not { Kind: Contract.Kinds.Suspend, Operator: { Selection: null }, Sender: null, Payload: null, Start: null, ReplyTo: null } ||
            command.Target.Id != row.Id || command.TargetCreation != row.Creation || command.InboxScope != Durable.InboxKey.FoundationOccurrenceScope)
            throw new NotSupportedException("This owner only stages a bound one-way suspension of a running root.");
        var receipt = new Durable.Delivery(command, 0) { Durable = true };
        var input = new Durable.TurnInput(worker, new Durable.Slot { Row = row, Loaded = true }, row.Id, [receipt], 0, null, 0);
        var turn = new ActorTurn(null, input, header, null);
        await actor.TurnAsync(turn, token);
        var outcome = turn.Seal();
        var next = outcome.FoundationHeader ?? new Durable.FoundationHeaderChange(header.TypeData, header.Labels);
        if (outcome.Row != row with { Revision = checked(row.Revision + 1) } || outcome.Expected != row.Revision ||
            outcome.Consumed is not [var consumed] || consumed != receipt || outcome.Journal is not null ||
            outcome.Held.Count != 0 || outcome.Sent.Count != 0 || outcome.Cancelled.Count != 0 || outcome.Subscribed.Count != 0 ||
            outcome.Unsubscribed.Count != 0 || outcome.Emitted.Count != 0 || outcome.Replied.Count != 0 || outcome.Unsubscribe ||
            outcome.Delete || outcome.InMemory || outcome.FoundationCascade is not null ||
            !actor.Withholds(header with { TypeData = next.TypeData, Labels = next.Labels }))
            throw new NotSupportedException("The actor did not produce a silent withholding header checkpoint.");
        return next;
    }
}
