using Contract = Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

internal sealed partial class ActorAdapter
{
    Durable.ParkPageCursor? Durable.IActorType.FirstUnheldRecoveryInput(Durable.InstanceRow row, Durable.ParkRelease release,
        Predicate<Durable.InboxKey> isHeld) => store.FirstUnheldRecoveryInput(row, release, isHeld);

    private Durable.TurnOutcome OpenRecover(Durable.TurnInput input)
    {
        var delivery = input.Taken[input.From];
        if (input.Row is not { Status: Durable.InstanceStatus.Faulted, ParkDiscard: null, ParkRecovery: null } row ||
            delivery.Message.Operator is not { Selection: { } selection, RecoverMode: { } mode })
            return Durable.CreationProtocol.Refuse(input.Actor, input.Row, input.Taken, "StaleSelection");
        var checkpoint = store.FoundationTurnHeader(row);
        var header = Header(checkpoint);
        if (checkpoint.Park is not { } park || !store.MatchesFoundationSelection(header.Tenant, header.Ref.Id, selection))
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "StaleSelection");
        var expiry = store.FoundationExpiry(row);
        if (expiry is not null && input.Services.Now >= expiry.At)
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "ParkExpired");

        Contract.RestorePlan plan;
        bool withholds;
        try
        {
            plan = actor.RestoreWaits(header, row.State);
            withholds = actor.Withholds(header with { Status = Contract.ActorStatus.Running, Fault = null, EndedAt = null });
        }
        catch (Exception) { return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "Plan"); }
        if (plan is not { Timers.Count: 0, Jobs.Count: 0, Subscriptions.Count: 0 } || row.Jobs is not null)
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "Plan");

        var next = row with
        {
            Status = Durable.InstanceStatus.Running,
            Fault = null,
            ParkCohort = null,
            ParkResolutionGeneration = checked(row.ParkResolutionGeneration + 1)
        };
        var serial = checked((expiry?.Serial ?? 0) + 1);
        var control = Durable.ParkDrainEnvelope.For(next, park).Issue(next, serial);
        next = next with { ParkRecovery = new(delivery.Message.Id, mode, null, serial, control.InboxKey) };
        var outcome = ParkCheckpoint(input, row, next);
        outcome.FoundationWithholds = withholds;
        outcome.Sent.Add(control);
        if (expiry?.Armed is { } armed) outcome.Cancelled.Add(armed);
        outcome.ReplyTo(delivery, "started");
        return outcome;
    }

    private Durable.TurnOutcome ContinueRecover(Durable.TurnInput input)
    {
        var delivery = input.Taken[input.From];
        if (input.Row is not { ParkRecovery: { } drain } row || store.FoundationTurnHeader(row) is not { Park: { } park } checkpoint)
            return Durable.CreationProtocol.Refuse(input.Actor, input.Row, input.Taken, "InvalidControl");
        var envelope = Durable.ParkDrainEnvelope.For(row, park);
        if (!envelope.Matches(delivery.Message, row, drain.Control))
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "InvalidControl");
        var serial = checked(drain.Serial + 1);
        var credit = store.FoundationRecoveryCredit(row, input.RecoveryHandedOff);
        if (row.Status == Durable.InstanceStatus.Running &&
            (row.Suspended || RecoveryWithholds(Header(checkpoint)) || !credit.CanRead))
            return NextPage(envelope.Issue(row, serial, drainAt: checked(input.Services.Now + 10)), drain, null);

        var release = new Durable.ParkRelease(row.Creation, row.ParkResolutionGeneration);
        var address = new Durable.CreationAddress(row.Id, row.Creation);
        var page = store.ReadFoundationRecoveryPage(address, park, release.Generation, drain.Cursor);
        if (page is not { Count: > 0 })
        {
            if (page is null) return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "InvalidControl");
            if (row.Status != Durable.InstanceStatus.Running)
            {
                var terminal = store.ReadEndedFoundationRecoveryPage(address, park, release.Generation);
                if (terminal is null) return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "InvalidControl");
                if (terminal.Count != 0)
                    return NextPage(envelope.Issue(row, serial), drain with { Gate = null }, new(park, terminal, TerminalInputs: true));
            }
            var cohort = row.Status == Durable.InstanceStatus.Running && store.FirstUnheldRecoveryInput(row, release, _ => false) is not null
                ? release : (Durable.ParkRelease?)null;
            var completed = ParkCheckpoint(input, row, row with { ParkRecovery = null, ParkCohort = cohort });
            completed.Journal = Durable.Json.Write(new { Outcome = "recovered", drain.Operation, drain.Restored, drain.Cleared });
            return completed;
        }

        var admitted = new List<Durable.ParkPageRow>(page.Count);
        var restored = 0;
        var (rows, bytes) = (credit.Rows, credit.Bytes);
        Durable.InboxKey? gate = null;
        foreach (var item in page)
        {
            if (drain.Restores(item, row.Status))
            {
                if (!Durable.ParkRecoveryCredit.IsTicket(item.Kind))
                {
                    var charged = Durable.ParkRecoveryCredit.StoredBytes(item.PayloadBytes);
                    if (rows > 0 && (rows >= Durable.InMemoryStore.ParkPageLimit || bytes + charged > Durable.ParkRecoveryCredit.LimitBytes)) break;
                    rows++;
                    bytes += charged;
                }
                restored++;
                gate = item.Cursor.Key;
            }
            admitted.Add(item);
        }
        if (admitted.Count == 0)
            return NextPage(envelope.Issue(row, serial, drainAt: checked(input.Services.Now + 10)), drain, null);
        var continued = drain with
        {
            Cursor = admitted[^1].Cursor, Restored = checked(drain.Restored + restored),
            Cleared = checked(drain.Cleared + admitted.Count - restored), Gate = gate
        };
        return NextPage(envelope.Issue(row, serial), continued, new(park, admitted));

        Durable.TurnOutcome NextPage(Durable.Message next, Durable.ParkRecoveryDrain state, Durable.ParkRecoveryPage? change)
        {
            var outcome = ParkCheckpoint(input, row, row with { ParkRecovery = state with { Serial = serial, Control = next.InboxKey } });
            outcome.Sent.Add(next);
            outcome.FoundationRecoveryPage = change;
            return outcome;
        }
    }

    private bool RecoveryWithholds(Contract.ActorHeader header)
    {
        try { return actor.Withholds(header); }
        catch (Exception) { return true; }
    }
}
