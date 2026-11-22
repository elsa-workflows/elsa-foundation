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

        Durable.ParkTimerRecipe[] timerRecipes;
        Durable.ParkJobRecipe[] jobRecipes;
        bool withholds;
        try
        {
            var plan = actor.RestoreWaits(header, row.State);
            const int sample = Durable.InMemoryStore.MaxRestoreWaits + 1;
            if (plan.Subscriptions.Any()) return InvalidPlan();
            timerRecipes = plan.Timers.Take(sample).Select(timer => new Durable.ParkTimerRecipe(timer.Id, timer.Kind, timer.Payload, timer.Due)).ToArray();
            jobRecipes = plan.Jobs.Take(sample).Select(job => new Durable.ParkJobRecipe(job.Key, job.Arguments, job.Deadline)).ToArray();
            withholds = actor.Withholds(header with { Status = Contract.ActorStatus.Running, Fault = null, EndedAt = null });
        }
        catch (Exception) { return InvalidPlan(); }
        if (timerRecipes.Length + jobRecipes.Length > Durable.InMemoryStore.MaxRestoreWaits) return InvalidPlan();
        var timers = store.PlanFoundationTimers(row, mode, timerRecipes);
        if (timers is null) return InvalidPlan();
        var jobs = store.PlanFoundationJobs(row, mode, jobRecipes, timers.Bytes);
        if (jobs is null) return InvalidPlan();

        Durable.TurnOutcome InvalidPlan() => Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "Plan");

        var next = row with
        {
            Status = Durable.InstanceStatus.Running,
            Fault = null,
            ParkCohort = null,
            ParkTimerDispositions = timers.Dispositions,
            ParkRestartedJobs = jobs.Restarts,
            Jobs = jobs.Registry,
            NextJobGeneration = jobs.NextGeneration,
            ParkResolutionGeneration = checked(row.ParkResolutionGeneration + 1)
        };
        var serial = checked((expiry?.Serial ?? 0) + 1);
        var control = Durable.ParkDrainEnvelope.For(next, park).Issue(next, serial);
        next = next with { ParkRecovery = new(delivery.Message.Id, mode, null, serial, control.InboxKey) };
        var outcome = ParkCheckpoint(input, row, next);
        outcome.FoundationRecoveryTimers = timers;
        outcome.FoundationRecoveryJobs = jobs;
        outcome.FoundationWithholds = withholds;
        outcome.Sent.Add(control);
        outcome.Cancelled.AddRange(jobs.RecalledDeadlines);
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
        var running = row.Status == Durable.InstanceStatus.Running;
        var credit = running ? store.FoundationRecoveryCredit(row, input.RecoveryHandedOff) : default;
        if (running && (row.Suspended || RecoveryWithholds(Header(checkpoint)) || !credit.CanRead))
            return NextPage(envelope.Issue(row, serial, drainAt: checked(input.Services.Now + 10)), drain, null);

        var release = new Durable.ParkRelease(row.Creation, row.ParkResolutionGeneration);
        var address = new Durable.CreationAddress(row.Id, row.Creation);
        var page = store.ReadFoundationRecoveryPage(address, park, release.Generation, drain.Cursor);
        if (page is not { Count: > 0 })
        {
            if (page is null) return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "InvalidControl");
            if (!running)
            {
                var terminal = store.ReadEndedFoundationRecoveryPage(address, park, release.Generation);
                if (terminal is null) return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "InvalidControl");
                if (terminal.Count != 0)
                    return NextPage(envelope.Issue(row, serial), drain with { Gate = null }, new(park, terminal, TerminalInputs: true));
            }
            var cohort = running && store.FirstUnheldRecoveryInput(row, release, _ => false) is not null
                ? release : (Durable.ParkRelease?)null;
            var completed = ParkCheckpoint(input, row, row with
            {
                ParkRecovery = null, ParkTimerDispositions = null, ParkRestartedJobs = null, ParkCohort = cohort, EndedDuringParkRecovery = !running
            });
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
                if (running && !Durable.ParkRecoveryCredit.IsTicket(item.Kind))
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
