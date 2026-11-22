using Contract = Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

internal sealed partial class ActorAdapter
{
    private Durable.TurnOutcome OpenDiscard(Durable.TurnInput input)
    {
        var message = input.Taken[input.From].Message;
        if (input.Row is not { Status: Durable.InstanceStatus.Faulted } row ||
            message.Operator?.Selection is not { } selection ||
            store.FoundationTurnHeader(row).Park is not { } park)
            return Durable.CreationProtocol.Refuse(input.Actor, input.Row, input.Taken, "StaleSelection");
        var identity = store.FoundationIdentity(row.Id, row.Creation);
        if (!store.MatchesFoundationSelection(identity.Tenant, identity.Reference, selection))
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "StaleSelection");

        var next = row with { ParkResolutionGeneration = checked(row.ParkResolutionGeneration + 1) };
        var outcome = AdvanceDiscard(input, row, next, park, message.Id, null, 0, 1);
        outcome.ReplyTo(input.Taken[input.From], "started");
        return outcome;
    }

    private Durable.TurnOutcome ContinueDiscard(Durable.TurnInput input)
    {
        var message = input.Taken[input.From].Message;
        if (input.Row is not { ParkDiscard: { } drain } row ||
            store.FoundationTurnHeader(row).Park is not { } park ||
            !Durable.ParkDrainEnvelope.For(row, park).Matches(message, row, drain.Control))
            return Durable.CreationProtocol.Refuse(input.Actor, input.Row, input.Taken, "InvalidControl");

        var page = store.ReadFoundationKeptPage(new(row.Id, row.Creation), park, drain.Cursor);
        if (page is null)
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "InvalidControl");
        if (page.Count > 0)
            return AdvanceDiscard(input, row, row, park, drain.Operation, page, checked(drain.Cleared + page.Count), checked(drain.Serial + 1));

        var completed = row with { ParkDiscard = null, ParkDiscarded = true };
        var outcome = DiscardCheckpoint(input, row, completed);
        outcome.Journal = Durable.Json.Write(new { Outcome = "discarded", drain.Operation, drain.Cleared });
        if (outcome.Row!.Parent is not null)
            outcome.Sent.Add(Notice(outcome.Row, null, final: true));
        return outcome;
    }

    private static Durable.TurnOutcome AdvanceDiscard(Durable.TurnInput input, Durable.InstanceRow row,
        Durable.InstanceRow next, Durable.InMemoryStore.FoundationParkMark park, string operation, IReadOnlyList<Durable.ParkPageRow>? page,
        long cleared, long serial)
    {
        var control = Durable.ParkDrainEnvelope.For(next, park).Issue(next, serial);
        var drain = new Durable.ParkDiscardDrain(operation, page is null ? null : page[^1].Cursor, cleared, serial, control.InboxKey);
        var outcome = DiscardCheckpoint(input, row, next with { ParkDiscard = drain });
        outcome.Sent.Add(control);
        if (page is not null)
            outcome.FoundationParkClear = new(park, page);
        return outcome;
    }

    private static Durable.TurnOutcome DiscardCheckpoint(Durable.TurnInput input, Durable.InstanceRow row,
        Durable.InstanceRow next)
    {
        var revision = checked(row.Revision + 1);
        return new(row.Id)
        {
            Row = next with { Revision = revision }, Expected = row.Revision, Revision = revision, Status = row.Status,
            Consumed = [.. input.Taken]
        };
    }
}
