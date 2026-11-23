using Kernel.Foundation.InMemory;
using Kernel.Runtime;
using Kernel.WorkflowActor;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class MigrationPlanDispatchChecks
{
    internal static async Task RunAsync()
    {
        const long initial = 1_707_000_000_000;
        var now = initial;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var stop = timeout.Token;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("migration");
        var permitted = true;
        var fixture = new WitnessActor();
        var edge = new InMemoryIngress(store, new Requests("migration-dispatch"), "migration-dispatch",
            (bound, kind) => bound.Tenant == scope.Tenant && (kind == Contract.Kinds.Delete || kind == Contract.Kinds.Migrate && permitted));
        var ingress = new WitnessIngress(edge);
        Contract.IActorReads reads = new ActorList(store, 4);
        var worker = new Worker("migration-owner", 0, store, new ActorAdapter(store, fixture),
            new WorkerOptions { Monotonic = () => now - initial });
        await using var owner = await new DurableHost([worker]).ActivateAsync(0, stop)
            ?? throw new InvalidOperationException("The migration actor owner could not activate.");
        var plan = await StartAsync("plan", "plan");
        var first = await StartAsync("first", "source");
        var second = await StartAsync("second", "source");
        var old = await StartAsync("old", "source", "recreate");
        var batch = new MigrationDispatchBatch(1, "source", "target", [first, second, old]);
        var planState = new WitnessPlanState(reads, edge);
        var dispatcher = new MigrationPlanDispatch(ingress, planState);
        await PumpAsync();
        await SendAsync(plan, "select-batch", "select", Json.Write(batch));
        Require(await dispatcher.DispatchAsync(scope, plan, stop) is null && ingress.Calls.Count == 0,
            "The dispatcher waits for a committed batch.");
        await PumpAsync();

        await SendAsync(old, "end", "end");
        await PumpAsync();
        var ended = await reads.GetAsync(scope, old, stop);
        Require(ended is { Header.Status: Contract.ActorStatus.Finished, Selection: not null },
            "The old selected life supplies a qualified end selection.");
        await SendAsync(old, "delete", Contract.Kinds.Delete, Json.Write(new Contract.OperatorCommand(ended!.Selection!)));
        await PumpAsync();
        now += 45;
        await store.SweepAsync(stop);
        var replacement = await StartAsync("replacement", "source", "recreate");
        await PumpAsync();
        Require(replacement != old && await reads.GetAsync(scope, old, stop) is null && await DefinitionAsync(replacement) == "source",
            "The caller key now names a different live actor.");

        permitted = false;
        var denied = false;
        try { await dispatcher.DispatchAsync(scope, plan, stop); }
        catch (InvalidOperationException) { denied = true; }
        Require(denied && planState.Acknowledgments == 0 && (await planState.ReadPendingAsync(scope, plan, stop))?.Number == 1 &&
            await DefinitionAsync(first) == "source", "An authorization refusal leaves the whole batch pending.");
        permitted = true;
        ingress.Calls.Clear();
        ingress.LoseReplyFor = second;
        var lost = false;
        try { await dispatcher.DispatchAsync(scope, plan, stop); }
        catch (UnknownAdmissionException) { lost = true; }
        Require(lost && planState.Acknowledgments == 0 && ingress.Calls.Count == 2 &&
            ingress.Calls.All(call => call.Outcome == Contract.AdmissionOutcome.Accepted),
            "A lost admission answer does not acknowledge the batch.");
        var attempts = ingress.Calls.ToArray();
        await PumpAsync();
        Require(await DefinitionAsync(first) == "target" && await DefinitionAsync(second) == "target" &&
            await DefinitionAsync(replacement) == "source", "Both admitted exact actors apply the migration after the lost reply.");

        ingress.Calls.Clear();
        var resumed = new MigrationPlanDispatch(ingress, new WitnessPlanState(reads, edge));
        var dispatch = await resumed.DispatchAsync(scope, plan, stop);
        Require(dispatch is { Accepted: 0, Duplicate: 2, Unavailable: 1 } && ingress.Calls.Count == 3 &&
            ingress.Calls.Take(2).Select(call => (call.Target, call.Id)).SequenceEqual(attempts.Select(call => (call.Target, call.Id))) &&
            ingress.Calls[2].Target == old, "A fresh runner retries the committed exact refs with the same operation IDs.");
        await PumpAsync();
        Require(await dispatcher.DispatchAsync(scope, plan, stop) is null && await DefinitionAsync(replacement) == "source",
            "The committed acknowledgment clears the batch and leaves the successor at its source definition.");

        var projection = new MigrationPlanOutcomes(reads, scope, plan);
        while (await projection.ProjectNextAsync(stop)) { }
        var view = projection.Read();
        var records = await ReadJournalAsync();
        Require(records.Count(record => record == Json.Write("batch-dispatched")) == 1 &&
            records.Select(MigrationPlanJournal.Read).OfType<MigrationPlanJournal.Answered>().Select(answer => answer.Actor).ToHashSet().SetEquals([first, second]) &&
            records.Select(MigrationPlanJournal.Read).OfType<MigrationPlanJournal.Answered>().Count() == 2 &&
            view.Results.Count == 3 && view.Results[first] == MigrationPlanOutcome.Migrated &&
            view.Results[second] == MigrationPlanOutcome.Migrated && view.Pending.SequenceEqual([old]),
            "The journal attributes one result per migrated life and keeps the deleted selection unanswered.");
        var laterPlan = await StartAsync("later-plan", "plan");
        await PumpAsync();
        await SendAsync(laterPlan, "select-later", "select", Json.Write(new MigrationDispatchBatch(1, "target", "later", [first])));
        await PumpAsync();
        ingress.Calls.Clear();
        var later = await dispatcher.DispatchAsync(scope, laterPlan, stop);
        await PumpAsync();
        Require(later is { Accepted: 1, Duplicate: 0, Unavailable: 0 } && ingress.Calls.Count == 1 &&
            ingress.Calls[0].Target == first && ingress.Calls[0].Id != attempts[0].Id &&
            await DefinitionAsync(first) == "later" && await DefinitionAsync(second) == "target",
            "A distinct plan uses a distinct operation ID for the same actor and batch number.");


        async Task<List<string>> ReadJournalAsync()
        {
            var records = new List<string>();
            Contract.PageToken? next = null;
            do
            {
                var page = await reads.TailAsync(scope, plan, next, stop);
                foreach (var entry in page.Items)
                    records.Add(entry.Entry);
                next = page.Next;
            } while (next is not null);
            return records;
        }

        async Task<string?> DefinitionAsync(Contract.ActorRef actor) =>
            (await reads.GetAsync(scope, actor, stop))?.Header.Labels.GetValueOrDefault("definition");

        async Task<Contract.ActorRef> StartAsync(string id, string definition, string? key = null)
        {
            var spec = new Contract.StartSpec(fixture.Type, Json.Write(new FixtureHeader(definition)));
            var result = await edge.SendAsync(scope, key is null ? new Contract.Target.Start(spec) : new Contract.Target.Key(key, spec),
                id, Contract.Kinds.Start, options: new(KeepFor: TimeSpan.FromMilliseconds(40)), cancellationToken: stop);
            return result.Actor ?? throw new InvalidOperationException($"The start for {id} did not bind an actor.");
        }

        async Task SendAsync(Contract.ActorRef actor, string id, string kind, string? body = null)
        {
            var admission = await edge.SendAsync(scope, new Contract.Target.Ref(actor), id, kind, body, cancellationToken: stop);
            if (admission.Outcome != Contract.AdmissionOutcome.Accepted)
                throw new InvalidOperationException($"Expected accepted {kind}, found {admission.Outcome} ({admission.Reason}).");
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

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine(description);
    }

    private sealed record FixtureHeader(string Definition, MigrationDispatchBatch? Pending = null);

    private sealed class WitnessPlanState(Contract.IActorReads reads, Contract.IIngress ingress) : IMigrationDispatchState
    {
        public int Acknowledgments { get; private set; }

        public async ValueTask<MigrationDispatchBatch?> ReadPendingAsync(Contract.BoundScope scope, Contract.ActorRef plan, CancellationToken stop)
        {
            var view = await reads.GetAsync(scope, plan, stop);
            return view is not null && Json.Read<FixtureHeader>(view.Header.TypeData) is { Definition: "plan" } header ? header.Pending : null;
        }

        public async ValueTask AcknowledgeAsync(Contract.BoundScope scope, Contract.ActorRef plan, long batch, string operationId, CancellationToken stop)
        {
            var sent = await ingress.SendAsync(scope, new Contract.Target.Ref(plan), operationId, "dispatched", Json.Write(batch), cancellationToken: stop);
            if (sent.Outcome is not (Contract.AdmissionOutcome.Accepted or Contract.AdmissionOutcome.Duplicate))
                throw new InvalidOperationException("The fixture did not commit the acknowledgment.");
            Acknowledgments++;
        }
    }

    private sealed class UnknownAdmissionException : Exception;

    private sealed class WitnessIngress(Contract.IIngress inner) : Contract.IIngress
    {
        internal List<(Contract.ActorRef Target, string Id, Contract.AdmissionOutcome Outcome)> Calls { get; } = [];
        internal Contract.ActorRef? LoseReplyFor { get; set; }

        public async ValueTask<Contract.Admission> SendAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind,
            string? payload = null, Contract.AddOptions? options = null, CancellationToken cancellationToken = default)
        {
            var result = await inner.SendAsync(scope, target, id, kind, payload, options, cancellationToken);
            if (kind == Contract.Kinds.Migrate && target is Contract.Target.Ref exact)
            {
                Calls.Add((exact.Actor, id, result.Outcome));
                if (exact.Actor == LoseReplyFor && result.Outcome == Contract.AdmissionOutcome.Accepted)
                {
                    LoseReplyFor = null;
                    throw new UnknownAdmissionException();
                }
            }
            return result;
        }

        public ValueTask<Contract.Reply> RequestAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind, string? payload,
            Contract.AddOptions? add, Contract.RequestOptions request, CancellationToken cancellationToken = default) =>
            inner.RequestAsync(scope, target, id, kind, payload, add, request, cancellationToken);

        public ValueTask SetStartAsync(Contract.BoundScope scope, string key, string wait, Contract.StartSpec? spec, CancellationToken cancellationToken = default) =>
            inner.SetStartAsync(scope, key, wait, spec, cancellationToken);
    }

    // This fixture exercises the public command and the genuine sender identity. Native definition mapping is separate.
    private sealed class WitnessActor : Contract.IActorType
    {
        public string Type => "migration-dispatch-witness";

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var header = Json.Read<FixtureHeader>(turn.Header.TypeData);
            foreach (var input in turn.Inbox)
            {
                switch (input.Kind)
                {
                    case Contract.Kinds.Start:
                        SetHeader(header);
                        break;
                    case Contract.Kinds.Migrate:
                        var request = Json.Read<MigrationRequest>(input.Payload!);
                        var outcome = header.Definition == request.From ? MigrationPlanOutcome.Migrated : MigrationPlanOutcome.NotAtSource;
                        if (outcome == MigrationPlanOutcome.Migrated) SetHeader(header with { Definition = request.To });
                        turn.Send(request.Plan, "migration-reply", Json.Write(outcome));
                        break;
                    case "select" when header.Definition == "plan" && header.Pending is null:
                        var selected = Json.Read<MigrationDispatchBatch>(input.Payload!);
                        SetHeader(header with { Pending = selected });
                        turn.Record(new MigrationPlanJournal.Selected(selected.Actors));
                        break;
                    case "dispatched" when header.Pending is { } pending && pending.Number == Json.Read<long>(input.Payload!):
                        SetHeader(header with { Pending = null });
                        turn.Record("batch-dispatched");
                        break;
                    case "migration-reply" when header.Definition == "plan" && input.Sender is { } sender:
                        turn.Record(new MigrationPlanJournal.Answered(sender, Json.Read<MigrationPlanOutcome>(input.Payload!)));
                        break;
                    case "end":
                        turn.End(Contract.ActorStatus.Finished);
                        break;
                }
            }
            return ValueTask.CompletedTask;

            void SetHeader(FixtureHeader updated)
            {
                header = updated;
                turn.SetHeader(Json.Write(updated), new Dictionary<string, string> { ["definition"] = updated.Definition });
            }
        }

        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string? state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string? state) => 0;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string? state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
    }
}
