using Kernel.Foundation.InMemory;
using Kernel.WorkflowActor;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class RetentionPassChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const long endedAt = 1_702_720_000_000;
        const int pageSize = 2;
        var now = endedAt;
        var extended = TimeSpan.FromMilliseconds(10);
        var blue = new Contract.BoundScope("retention-blue");
        var red = new Contract.BoundScope("retention-red");
        var store = new InMemoryStore(1, () => now) { IngressDeduplicationWindow = TimeSpan.FromMilliseconds(100) };
        var actorType = new Probe();
        var ingress = new InMemoryIngress(store, new Requests("retention-pass"), "retention-pass");
        Contract.IActorReads reads = new ActorList(store, pageSize);
        var retention = new RetentionPass(reads, actorType.Type, Window, ingress.AdmitRetentionDeleteAsync);
        var owner = new Worker("retention-pass-owner", 0, store, new ActorAdapter(store, actorType, retentionWindow: Window),
            new WorkerOptions { Monotonic = () => now - endedAt });
        await using var active = await new DurableHost([owner]).ActivateAsync(0)
            ?? throw new InvalidOperationException("Expected a live retention fixture owner.");

        var finished = await Start(blue, "finished", "ordinary", "finish");
        var alsoFinished = await Start(blue, "finished-second", "ordinary", "finish");
        var cancelled = await Start(blue, "cancelled", "ordinary", "cancel");
        var faulted = await Start(blue, "faulted", "ordinary", "fault");
        var stale = await Start(blue, "stale", "changing", "finish");
        var forever = await Start(blue, "forever", "keep", "finish");
        var parked = await Start(blue, "parked", "ordinary", "park");
        var running = await Start(blue, "running", "ordinary");
        var redFinished = await Start(red, "finished", "ordinary", "finish");
        var redFaulted = await Start(red, "faulted", "ordinary", "fault");
        now++;
        var tooNew = await Start(blue, "not-yet", "ordinary", "finish");

        var ready = new[] { finished, alsoFinished, cancelled, faulted, stale };
        var expectedBlue = ready.ToHashSet();
        var expectedRed = new HashSet<Contract.ActorRef> { redFaulted };
        var parkedView = await reads.GetAsync(blue, parked);
        var faultedView = await reads.GetAsync(blue, faulted);
        check(parkedView is { Park: not null, Header.Status: Contract.ActorStatus.Faulted } &&
            faultedView is { Park: null, Header.Status: Contract.ActorStatus.Faulted, Header.EndedAt: endedAt, Selection.Length: > 0 },
            "live owner produces a Park and a distinct terminal Faulted actor in the public read model");

        now = endedAt + 10;
        Contract.PageToken? cursor = null;
        var passes = 0;
        var accepted = 0;
        var examined = 0;
        var replayed = false;
        do
        {
            var from = cursor;
            var page = await retention.RunAsync(blue, from);
            cursor = page.Next;
            passes++;
            accepted += page.Accepted;
            examined += page.Examined;
            if (!replayed && page.Accepted > 0)
            {
                var pendingBefore = Pending().ToArray();
                var replay = await retention.RunAsync(blue, from);
                check(replay.Accepted == 0 && replay.Duplicates == page.Accepted && Pending().SequenceEqual(pendingBefore),
                    "rereading the same page with freshly sealed selections reuses the reserved operations");
                replayed = true;
            }
            if (passes == 1)
            {
                try
                {
                    await retention.RunAsync(red, cursor);
                    check(false, "a blue continuation cannot open a red retention page");
                }
                catch (ArgumentException error) when (error.ParamName == "from")
                {
                    check(true, "a blue continuation cannot open a red retention page");
                }
            }
            if (page.Examined > pageSize || passes > 4)
                throw new InvalidOperationException("The configured retention page bound did not hold.");
        } while (cursor is not null);

        check(passes == 4 && examined == 8 && accepted == expectedBlue.Count && replayed &&
            Pending().Select(row => row.Actor).ToHashSet().SetEquals(expectedBlue),
            "four bounded passes select every eligible blue actor and skip younger, retained, running and parked actors");
        using var interrupt = new CancellationTokenSource();
        var interrupting = new RetentionPass(reads, actorType.Type, Window, async (scope, actor, selection, token) =>
        {
            var receipt = await ingress.AdmitRetentionDeleteAsync(scope, actor, selection, token);
            interrupt.Cancel();
            return receipt;
        });
        try
        {
            await interrupting.RunAsync(red, cancellationToken: interrupt.Token);
            check(false, "interruption after an admission yields no continuation");
        }
        catch (OperationCanceledException error) when (error.CancellationToken == interrupt.Token)
        {
            check(true, "interruption after an admission yields no continuation");
        }
        var foreignPage = await retention.RunAsync(red);
        check(foreignPage is { Next: null, Examined: 2, Accepted: 0, Duplicates: 1 } &&
            Pending().Where(row => row.Tenant == red.Tenant).Select(row => row.Actor).ToHashSet().SetEquals(expectedRed),
            "the second tenant replays its interrupted page and applies its own status windows");

        extended = TimeSpan.FromMilliseconds(100);
        await Pump();
        var remaining = new[] { stale, tooNew, forever, parked, running };
        check((await Task.WhenAll(remaining.Select(async reference => await reads.GetAsync(blue, reference)))).All(view => view is not null) &&
            await reads.GetAsync(red, redFinished) is not null && await reads.GetAsync(red, redFaulted) is null,
            "the owner honors the extended policy at delivery and preserves the excluded actors in both tenants");
        var removed = new[] { finished, alsoFinished, cancelled, faulted };
        check((await Task.WhenAll(removed.Select(async reference => await reads.GetAsync(blue, reference)))).All(view => view is null) &&
            removed.All(reference => Markers(reference, blue) == 1) && Markers(redFaulted, red) == 1 &&
            Markers(stale, blue) == 0 && !Pending().Any(),
            "qualified deletes retire exactly the expired lives, including the exact threshold, and settle the stale offer");

        now++;
        check(await SweepBlue() == 1 && Pending().SingleOrDefault().Actor == tooNew,
            "the next sweep selects the previously younger actor at its own threshold");
        await Pump();
        check(await reads.GetAsync(blue, tooNew) is null && await reads.GetAsync(blue, stale) is not null &&
            await reads.GetAsync(blue, parked) is { Park: not null },
            "later passes preserve the unresolved Park and the life protected by the current window");

        now = endedAt + 111;
        check(await SweepBlue() == 1 && Pending().SingleOrDefault().Actor == stale,
            "a later sweep reoffers the same terminal transition after the extended window and receipt expire");
        await Pump();
        check(await reads.GetAsync(blue, stale) is null && Markers(stale, blue) == 1,
            "a once-stale retention candidate retires exactly once after it becomes eligible");

        async Task<int> SweepBlue()
        {
            Contract.PageToken? from = null;
            var count = 0;
            for (var pass = 0; pass < 3; pass++)
            {
                var page = await retention.RunAsync(blue, from);
                count += page.Accepted;
                if (page.Next is not { } next) return count;
                from = next;
            }
            throw new InvalidOperationException("The next retention sweep did not finish.");
        }

        TimeSpan? Window(Contract.ActorHeader header) => header.Labels.GetValueOrDefault("definition") switch
        {
            "keep" => null,
            "changing" => extended,
            _ when header.Tenant == red.Tenant && header.Status == Contract.ActorStatus.Finished => TimeSpan.FromMilliseconds(20),
            _ => TimeSpan.FromMilliseconds(10)
        };

        async Task<Contract.ActorRef> Start(Contract.BoundScope scope, string operation, string definition, string? outcome = null)
        {
            var result = await ingress.SendAsync(scope, new Contract.Target.Start(new(actorType.Type, definition, History: Contract.History.Minimal)),
                operation, Contract.Kinds.Start);
            if (result is not { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } reference })
                throw new InvalidOperationException("Expected a live fixture actor.");
            await Pump();
            if (outcome is not null)
            {
                var sent = await ingress.SendAsync(scope, new Contract.Target.Ref(reference), "outcome", outcome);
                if (sent.Outcome != Contract.AdmissionOutcome.Accepted) throw new InvalidOperationException("Fixture input was refused.");
                await Pump();
            }
            return reference;
        }

        async Task Pump()
        {
            for (var i = 0; i < 8; i++)
            {
                await active.WokenAsync(null);
                await active.RunAsync();
                await Task.Yield();
            }
        }

        IEnumerable<(string Tenant, Contract.ActorRef Actor, string Id)> Pending() => store.Messages.Values
            .Where(row => !row.Done && row.Message.Operator?.RetentionDelete == true)
            .Select(row => (row.Tenant!, new Contract.ActorRef(store.FoundationIdentity(row.Message.Target.Id, row.Message.TargetCreation!.Value).Reference),
                row.Message.Id));

        int Markers(Contract.ActorRef reference, Contract.BoundScope scope)
        {
            var id = store.FoundationTarget(scope.Tenant, reference.Id)?.Id;
            return store.Journal.Values.Count(row => row.Instance == id && row.Revision == -1);
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "retention-pass-probe";
        public bool Withholds(Contract.ActorHeader header) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var kind = turn.Inbox[0].Kind;
            switch (kind)
            {
                case Contract.Kinds.Start:
                    turn.SetHeader("opaque workflow data", new Dictionary<string, string> { ["definition"] = turn.Header.TypeData });
                    break;
                case "finish": turn.End(Contract.ActorStatus.Finished); break;
                case "cancel": turn.End(Contract.ActorStatus.Cancelled); break;
                case "fault": turn.End(Contract.ActorStatus.Faulted); break;
                case "park": throw new InvalidOperationException("Park the fixture actor.");
            }
            turn.State = "fixture state";
            return ValueTask.CompletedTask;
        }
    }
}
