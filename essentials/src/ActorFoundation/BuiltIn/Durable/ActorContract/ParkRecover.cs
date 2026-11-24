using Contract = Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Durable;

internal sealed partial class ActorAdapter
{
    Durable.ParkPageCursor? Durable.IActorType.FirstUnheldRecoveryInput(Durable.InstanceRow row, Durable.ParkRelease release,
        Predicate<Durable.InboxKey> isHeld) => store.FirstUnheldRecoveryInput(row, release, isHeld);
    bool Durable.IActorType.OwnsParkCatchUp(Durable.InstanceRow row, Durable.Message ticket) => store.OwnsParkCatchUp(row, ticket);

    private Durable.TurnOutcome OpenRecover(Durable.TurnInput input)
    {
        var delivery = input.Taken[input.From];
        if (input.Row is not { Status: Durable.InstanceStatus.Faulted, ParkDiscard: null, ParkRecovery: null } row ||
            delivery.Message.Operator is not { Selection: { } selection, RecoverMode: { } mode } request)
            return Durable.CreationProtocol.Refuse(input.Actor, input.Row, input.Taken, "StaleSelection");
        var checkpoint = store.FoundationTurnHeader(row);
        var header = Header(checkpoint);
        if (checkpoint.Park is not { } park || !store.MatchesFoundationSelection(header.Tenant, header.Ref.Id, selection))
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "StaleSelection");
        var expiry = store.FoundationExpiry(row);

        Durable.ParkTimerRecipe[] timerRecipes;
        Durable.ParkJobRecipe[] jobRecipes;
        Durable.ParkSubscriptionRecipe[] subscriptionRecipes;
        bool withholds;
        try
        {
            var plan = actor.RestoreWaits(header, row.State);
            const int sample = Durable.InMemoryStore.MaxRestoreWaits + 1;
            subscriptionRecipes = plan.Subscriptions.Take(sample).Select(wait => new Durable.ParkSubscriptionRecipe(wait.Key, wait.Wait, wait.Keep)).ToArray();
            timerRecipes = plan.Timers.Take(sample).Select(timer => new Durable.ParkTimerRecipe(timer.Id, timer.Kind, timer.Payload, timer.Due)).ToArray();
            jobRecipes = plan.Jobs.Take(sample).Select(job => new Durable.ParkJobRecipe(job.Key, job.Arguments, job.Deadline)).ToArray();
            withholds = actor.Withholds(header with { Status = Contract.ActorStatus.Running, Fault = null, EndedAt = null });
        }
        catch (Exception) { return InvalidPlan(); }
        if (timerRecipes.Length + jobRecipes.Length + subscriptionRecipes.Length > Durable.InMemoryStore.MaxRestoreWaits) return InvalidPlan();
        Durable.ParkRetryOpen? retry = null;
        if (mode == Durable.ParkRecoveryMode.Retry)
        {
            retry = store.PlanFoundationRetry(row, header.Tenant, header.Ref.Id, park, request.RecoverHandles, timerRecipes);
            if (retry is null)
            {
                var refusal = Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "InvalidRetrySelection");
                refusal.Journal = Durable.Json.Write(new { Outcome = "refused", Operation = delivery.Message.Id, Reason = "selection" });
                return refusal;
            }
        }
        var timers = store.PlanFoundationTimers(row, mode, timerRecipes, retry);
        if (timers is null) return InvalidPlan();
        var jobs = store.PlanFoundationJobs(row, mode, jobRecipes, timers.Bytes, retry);
        if (jobs is null) return InvalidPlan();
        var subscriptions = store.PlanFoundationSubscriptions(row, header.Tenant, subscriptionRecipes, jobs.Bytes);
        if (subscriptions is null) return InvalidPlan();

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
        next = next with
        {
            ParkRecovery = new(delivery.Message.Id, mode, null, serial, control.InboxKey)
            { AlreadyRearmed = retry?.AlreadyRearmed.Count ?? 0 }
        };
        var outcome = ParkCheckpoint(input, row, next);
        outcome.FoundationRetry = retry;
        outcome.FoundationRecoveryTimers = timers;
        outcome.FoundationRecoveryJobs = jobs;
        if (subscriptionRecipes.Length != 0) outcome.FoundationRecoverySubscriptions = subscriptionRecipes;
        outcome.Subscribed.AddRange(subscriptions);
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
            var retire = drain.Deletion is not null &&
                DeletionRefusal(Header(checkpoint), row.State, input.Services.Now) is null;
            if (retire)
            {
                completed.Row = Durable.CreationProtocol.Retire(row, Durable.CascadeRegistry.ForRetirement(row));
                completed.Delete = completed.Unsubscribe = true;
            }
            completed.Journal = Durable.InMemoryStore.RecoveryCompleteJournal(drain, retire);
            return completed;
        }

        var admitted = new List<Durable.ParkPageRow>(page.Count);
        var restored = 0;
        var written = 0;
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
            written++;
            if (item.PairedJobInput is null) continue;
            written++;
            if (running) restored++;
        }
        if (admitted.Count == 0)
            return NextPage(envelope.Issue(row, serial, drainAt: checked(input.Services.Now + 10)), drain, null);
        var continued = drain with
        {
            Cursor = admitted[^1].Cursor, Restored = checked(drain.Restored + restored),
            Cleared = checked(drain.Cleared + written - restored), Gate = gate
        };
        return NextPage(envelope.Issue(row, serial), continued, new(park, admitted));

        Durable.TurnOutcome NextPage(Durable.Message next, Durable.ParkRecoveryDrain state, Durable.ParkRecoveryPage? change)
        {
            var outcome = ParkCheckpoint(input, row, row with
            {
                ParkRecovery = state with { Serial = serial, Control = next.InboxKey },
                Jobs = Durable.InMemoryStore.JobsAfterRecoveryPage(row, change)
            });
            outcome.Sent.Add(next);
            outcome.FoundationRecoveryPage = change;
            return outcome;
        }
    }

    private Durable.TurnOutcome TakeOverEndedRecovery(Durable.TurnInput input, Durable.InstanceRow row, string? selection,
        bool thenDelete = false)
    {
        var delivery = input.Taken[input.From];
        var drain = row.ParkRecovery!;
        var authorized = thenDelete && store.AuthorizesEndedRecoverThenDelete(row, delivery.Message);
        if (drain.Deletion is { } prior)
        {
            var refusal = Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "InProgress", prior.Operation);
            if (!authorized || row.Cascade is not null) return refusal;
            var registry = Durable.CascadeRegistry.From(row);
            if (registry.Count == 0) return refusal;
            refusal.Row = row with { Revision = checked(row.Revision + 1) };
            refusal.Expected = row.Revision;
            refusal.Revision = refusal.Row.Revision;
            return Durable.CascadeProtocol.Open(refusal, registry);
        }
        if (thenDelete && !authorized)
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "StaleSelection");
        var protection = DeletionRefusal(Header(store.FoundationTurnHeader(row)), row.State, input.Services.Now);
        if (protection is not null)
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, protection);

        var next = row with { ParkRecovery = drain with { Deletion = new(delivery.Message.Id, selection, thenDelete) } };
        var outcome = ParkCheckpoint(input, row, next);
        outcome.ReplyTo(delivery, "started");
        return thenDelete && row.Cascade is null ? Durable.CascadeProtocol.Open(outcome, Durable.CascadeRegistry.From(row)) : outcome;
    }

    private bool RecoveryWithholds(Contract.ActorHeader header)
    {
        try { return actor.Withholds(header); }
        catch (Exception) { return true; }
    }
}
