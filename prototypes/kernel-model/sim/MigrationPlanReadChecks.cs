using Kernel.Foundation.InMemory;
using Kernel.Runtime;
using Kernel.WorkflowActor;
using Contract = Kernel.Foundation.Contracts;
using Outcome = Kernel.WorkflowActor.MigrationPlanOutcome;

namespace Kernel.Sim;

internal static class MigrationPlanReadChecks
{
    internal static async Task RunAsync()
    {
        const long initial = 1_705_200_000_000;
        var now = initial;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stop = timeout.Token;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("migration");
        Contract.IIngress ingress = new InMemoryIngress(store, new Requests("migration-plan"), "migration-plan");
        Contract.IActorReads reads = new ActorList(store, 2);
        var type = new JournalActor();
        var worker = new Worker("migration-owner", 0, store, new ActorAdapter(store, type),
            new WorkerOptions { Monotonic = () => now - initial });
        await using var owner = await new DurableHost([worker]).ActivateAsync(0, stop)
            ?? throw new InvalidOperationException("The migration fixture could not activate.");

        var plan = await StartAsync("plan");
        var first = await StartAsync("first");
        var second = await StartAsync("second");
        var last = await StartAsync("last");
        var unrelated = await StartAsync("unrelated");
        await PumpAsync();

        var projection = new MigrationPlanOutcomes(reads, scope, plan);
        await SendAsync(plan, "select-first", "selected", new[] { first, second });
        await SendAsync(plan, "select-second", "selected", new[] { second, last });
        await ProjectAsync(projection);
        Require(projection.Read().Results.Count == 0, "Admission alone does not publish the selected set.");

        await PumpAsync();
        now++;
        await ProjectAsync(projection);
        var selected = projection.Read();
        Require(selected.Pending.ToHashSet().SetEquals([first, second, last]) && selected.AsOf == now &&
            !selected.Closed && !selected.AllAnswered, "The journal projection records distinct selected lives and its database clock.");

        await AnswerAsync(first, "not-at-source", Outcome.NotAtSource);
        await AnswerAsync(second, "refused", Outcome.Refused);
        await AnswerAsync(unrelated, "unrelated-outcome", Outcome.Migrated);
        await PumpAsync();
        await ProjectAsync(projection);
        var open = projection.Read();
        Require(open.Results.Count == 3 && open.Results[first] == Outcome.NotAtSource &&
            open.Results[second] == Outcome.Refused && open.Pending.SequenceEqual([last]) && !open.AllAnswered,
            "Only selected lives count, including provisional and decisive answers.");

        await AnswerAsync(first, "migrated", Outcome.Migrated);
        await PumpAsync();
        await AnswerAsync(first, "replayed-not-at-source", Outcome.NotAtSource);
        await AnswerAsync(second, "replayed-migrated", Outcome.Migrated);
        await SendAsync(plan, "replayed-selection", "selected", new[] { first, second });
        await SendAsync(plan, "close-plan", "closed");
        await PumpAsync();
        await ProjectAsync(projection);
        var closed = projection.Read();
        Require(closed.Closed && closed.Results[first] == Outcome.Migrated && closed.Results[second] == Outcome.Refused &&
            closed.NoAnswer.SequenceEqual([last]) && !closed.Pending.Any() && !closed.AllAnswered,
            "Close identifies missing answers and replay preserves the first decisive outcome.");

        now++;
        await SendAsync(plan, "selection-after-close", "selected", new[] { first, unrelated });
        await AnswerAsync(last, "late-answer", Outcome.Migrated);
        await PumpAsync();
        await ProjectAsync(projection);
        var final = projection.Read();
        Require(final.Closed && final.AllAnswered && !final.NoAnswer.Any() && final.Results[last] == Outcome.Migrated &&
            final.AsOf == now && !final.Results.ContainsKey(unrelated) && closed.NoAnswer.SequenceEqual([last]),
            "The closed plan fixes membership, accepts its late answer and preserves earlier snapshots.");

        var reopened = new MigrationPlanOutcomes(reads, scope, plan);
        var replayPages = await ProjectAsync(reopened);
        var rebuilt = reopened.Read();
        Require(replayPages >= 3 && rebuilt.Closed && rebuilt.AllAnswered &&
            rebuilt.Results.Count == final.Results.Count && rebuilt.Results.All(pair => final.Results[pair.Key] == pair.Value) &&
            !await reopened.ProjectNextAsync(stop), "A fresh projector reads multiple pages and reconstructs the same plan.");

        var emptyPlan = await StartAsync("empty-plan");
        var empty = new MigrationPlanOutcomes(reads, scope, emptyPlan);
        await PumpAsync();
        await ProjectAsync(empty);
        Require(!empty.Read().Closed && !empty.Read().AllAnswered, "An open plan without projected selections is incomplete.");
        await SendAsync(emptyPlan, "close-empty", "closed");
        await PumpAsync();
        await ProjectAsync(empty);
        Require(empty.Read() is { Closed: true, AllAnswered: true, Results.Count: 0 },
            "A closed plan with no selections has no missing answers.");

        using var cancel = new CancellationTokenSource();
        var cancelledRead = new CancelledTail(reads, cancel);
        var retry = new MigrationPlanOutcomes(cancelledRead, scope, plan);
        var cancelled = false;
        try { await retry.ProjectNextAsync(cancel.Token); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { cancelled = true; }
        Require(cancelled && retry.Read() is { AsOf: 0, Results.Count: 0, Closed: false },
            "Cancellation after the journal read leaves the projected page unapplied.");
        await ProjectAsync(retry);
        var afterRetry = retry.Read();
        Require(cancelledRead.From.Take(2).SequenceEqual([null, null]) && afterRetry.Closed &&
            afterRetry.Results.All(pair => final.Results[pair.Key] == pair.Value) && afterRetry.Results.Count == final.Results.Count,
            "A retry reads the same bookmark and rebuilds the complete view.");

        var foreign = new MigrationPlanOutcomes(reads, new Contract.BoundScope("other"), plan);
        Require(await ProjectAsync(foreign) == 0 && foreign.Read().Results.Count == 0 && !foreign.Read().Closed,
            "The same opaque plan reference has no projection in another tenant.");

        async Task<Contract.ActorRef> StartAsync(string id)
        {
            var admitted = await ingress.SendAsync(scope, new Contract.Target.Start(new(type.Type, id)),
                id, Contract.Kinds.Start, cancellationToken: stop);
            return admitted.Actor ?? throw new InvalidOperationException("The fixture did not receive an issued reference.");
        }

        async Task SendAsync(Contract.ActorRef target, string id, string kind, object? payload = null)
        {
            var accepted = await ingress.SendAsync(scope, new Contract.Target.Ref(target), id, kind,
                payload is null ? null : Json.Write(payload), cancellationToken: stop);
            if (accepted.Outcome != Contract.AdmissionOutcome.Accepted)
                throw new InvalidOperationException($"Unexpected admission result for '{id}'.");
        }

        Task AnswerAsync(Contract.ActorRef from, string id, Outcome outcome) => SendAsync(from, id, "send-answer", new Reply(plan, outcome));

        async Task<int> ProjectAsync(MigrationPlanOutcomes target)
        {
            var pageCount = 0;
            while (await target.ProjectNextAsync(stop)) pageCount++;
            return pageCount;
        }

        async Task PumpAsync()
        {
            for (var pass = 0; pass < 16; pass++)
            {
                await owner.WokenAsync(null, stop);
                await owner.RunAsync(stop);
            }
        }
    }

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
        Console.WriteLine(message);
    }

    private sealed class CancelledTail(Contract.IActorReads inner, CancellationTokenSource cancel) : Contract.IActorReads
    {
        internal List<Contract.PageToken?> From { get; } = [];

        public async ValueTask<Contract.Page<Contract.JournalEntry>> TailAsync(Contract.BoundScope scope, Contract.ActorRef actor,
            Contract.PageToken? from = null, CancellationToken cancellationToken = default)
        {
            From.Add(from);
            var page = await inner.TailAsync(scope, actor, from, cancellationToken);
            if (From.Count == 1) cancel.Cancel();
            return page;
        }

        public ValueTask<Contract.ActorView?> GetAsync(Contract.BoundScope scope, Contract.ActorRef actor, CancellationToken cancellationToken = default) =>
            inner.GetAsync(scope, actor, cancellationToken);

        public ValueTask<Contract.Page<Contract.ActorView>> ListAsync(Contract.BoundScope scope, Contract.ActorQuery query,
            Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
            inner.ListAsync(scope, query, from, cancellationToken);

        public ValueTask<Contract.Page<Contract.DeliveryView>> InboxAsync(Contract.BoundScope scope, Contract.ActorRef actor,
            Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
            inner.InboxAsync(scope, actor, from, cancellationToken);
    }

    private sealed record Reply(Contract.ActorRef Plan, Outcome Outcome);

    // A journal producer fixture. The migration plan actor and its operator dispatch remain K11 work.
    private sealed class JournalActor : Contract.IActorType
    {
        public string Type => "migration-plan-witness";

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var input in turn.Inbox)
            {
                switch (input.Kind)
                {
                    case "selected":
                        turn.Record(new MigrationPlanJournal.Selected(Json.Read<Contract.ActorRef[]>(input.Payload!)));
                        break;
                    case "closed":
                        turn.Record(new MigrationPlanJournal.Closed());
                        break;
                    case "send-answer":
                        var request = Json.Read<Reply>(input.Payload!);
                        turn.Send(request.Plan, "answer", Json.Write(request.Outcome));
                        break;
                    case "answer" when input.Sender is { } sender:
                        turn.Record(new MigrationPlanJournal.Answered(sender, Json.Read<Outcome>(input.Payload!)));
                        break;
                }
            }
            return ValueTask.CompletedTask;
        }

        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string? state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string? state) => 0;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string? state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
    }
}
