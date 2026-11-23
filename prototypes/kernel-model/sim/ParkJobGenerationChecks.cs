using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkJobGenerationChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const long initial = 1_703_010_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("same-key-job");
        var probe = new Probe(() => store.Instances.Values.Single().ParkRecovery is not null);
        ParkJobOpen? latestPlan = null;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (landed)
                foreach (var turn in batch.Turns)
                    if (turn.FoundationRecoveryJobs is { } plan) latestPlan = plan;
        });
        Worker NewWorker(string node) => new(node, 0, wire, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => now - initial });
        var edge = new InMemoryIngress(store, new Requests("job-generation"), "job-generation", (_, kind) => kind == Contract.Kinds.Recover);
        var first = NewWorker("before-Park");
        await using var original = (await new DurableHost([first]).ActivateAsync(0))!;
        var created = await edge.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "ready")), "birth", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(created.Actor!.Value);
        var id = store.FoundationTarget(scope.Tenant, target.Actor.Id)!.Id;
        await Pump(original);
        var creation = Row().Creation;
        var originalGeneration = JobRegistry.Generation(Row(), Probe.Key)!.Value;
        await edge.SendAsync(scope, target, "first-fault", "fault");
        await Pump(original);
        await original.DisposeAsync();

        var next = NewWorker("recover");
        await using var owner = (await new DurableHost([next]).ActivateAsync(0))!;
        await Pump(owner);
        if (!Check(store.Messages.GetValueOrDefault(Ticket(originalGeneration))?.KeptBy == ParkKeepReason.Arrived &&
            probe.Args.SequenceEqual(["first"]), "the successor retains the obsolete original ticket")) return;
        await Open("first-recovery");
        var restarted = originalGeneration + 1;
        if (!Check(Row().ParkRecovery is not null && Row().ParkRestartedJobs is not null &&
            JobRegistry.Generation(Row(), Probe.Key) == restarted && store.Messages[Ticket(restarted)] is { Done: false, ReleasedBy: null } &&
            probe.Args.Count == 1, "Open creates the restarted ticket and withholds its entry")) return;
        for (var i = 0; i < 8 && Row().ParkRecovery is not null; i++) await Step();
        if (Row().ParkRecovery is null && probe.Args.Count == 1) await Step();
        if (!Check(Row().ParkRecovery is null && Row().ParkRestartedJobs is null && !probe.EnteredInRecovery &&
            probe.Args.SequenceEqual(["first", "first"]), "the first eligible driver turn after Complete enters the restarted generation")) return;

        var firstDelivery = next.JobCompletion(id, Probe.Key, creation)!;
        probe.First.SetResult();
        await firstDelivery.WaitAsync(TimeSpan.FromSeconds(5));
        await Pump(owner);
        var successor = restarted + 1;
        var successorKey = Ticket(successor);
        var successorTicket = store.Messages.GetValueOrDefault(successorKey);
        if (!Check(JobRegistry.Generation(Row(), Probe.Key) == successor && successorTicket is { Done: false } &&
            successorTicket.Message.Read<JobTicket>().Arguments == "second" && !store.Messages.ContainsKey(Ticket(restarted)) &&
            Row().State == "first applied" && probe.Args.SequenceEqual(["first", "first", "second"]),
            "the result turn applies the first output and starts the same key at a higher generation")) return;

        await edge.SendAsync(scope, target, "second-fault", "fault");
        // Stop at the Park commit before the next parked poll can keep the outstanding ticket.
        await Step();
        if (!Check(Row().Status == InstanceStatus.Faulted && JobRegistry.Generation(Row(), Probe.Key) == successor &&
            successorTicket is { Done: false }, "the new Park pins the higher generation while its ticket is pending")) return;
        await Open("second-recovery", earlierThan: successorTicket!.Seq);
        if (!Check(Row().ParkRecovery is not null && Row().ParkRestartedJobs is null &&
            JobRegistry.Generation(Row(), Probe.Key) == successor && latestPlan?.Pending is [var pending] && pending == successorKey &&
            latestPlan.Writes.Count == 0 && store.Messages.GetValueOrDefault(successorKey) == successorTicket &&
            successorTicket is { Done: false }, "the next Open reads the higher generation's pending ticket and allocates no replacement")) return;
        for (var i = 0; i < 8 && Row().ParkRecovery is not null; i++) await Step();
        if (!Check(Row().ParkRecovery is null && JobRegistry.Generation(Row(), Probe.Key) == successor,
            "Complete preserves the same-key successor")) return;
        probe.Second.SetResult();
        for (var i = 0; i < 8 && Row().Jobs is not null; i++) await Pump(owner);
        Check(Row().Jobs is null && Row().State == "second applied" && probe.Results.SequenceEqual(["first", "second"]) &&
            !probe.EnteredInRecovery, "both results apply once, in generation order");

        InstanceRow Row() => store.Instances[id];
        InboxKey Ticket(long g) => InboxKey.Ticket(id, Probe.Key, creation, g);
        bool Check(bool ok, string label) { check(ok, "D-2(a) " + label); return ok; }
        async Task Open(string operation, long? earlierThan = null)
        {
            var selected = store.SealFoundationSelection(scope.Tenant, store.FoundationTurnHeader(Row()));
            await edge.SendAsync(scope, target, operation, Contract.Kinds.Recover, Json.Write(new Contract.OperatorCommand(selected)));
            // Model an operator whose independent allocation precedes the still-pending ticket.
            if (earlierThan is { } sequence) store.Messages.Values.Single(r => r.Message.Id == operation).Seq = sequence - 1;
            latestPlan = null;
            await Step();
        }
        async Task Step()
        {
            await next.PollAsync();
            await next.LoadAsync();
            await next.FlushAsync();
            next.RunTurn(id);
            await next.FlushAsync();
            now += 11;
        }
        static async Task Pump(IScopeActivation active)
        {
            for (var i = 0; i < 8; i++) { await active.WokenAsync(null); await active.RunAsync(); await Task.Yield(); }
        }
    }

    private sealed class Probe(Func<bool> recoveryOpen) : Contract.IActorType
    {
        internal const string Key = "shared";
        public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Second { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public System.Collections.Concurrent.ConcurrentQueue<string?> Args { get; } = new();
        public List<string> Results { get; } = [];
        public bool EnteredInRecovery { get; private set; }
        public string Type => "same-key-job";
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) =>
            new([], [new(Key, state == "first applied" ? "second" : "first")], []);
        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            EnteredInRecovery |= recoveryOpen();
            Args.Enqueue(job.Arguments);
            await (job.Arguments == "first" ? First : Second).Task.WaitAsync(cancellationToken);
            return job.Arguments;
        }
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Contract.Kinds.Start) turn.StartJob(Key, "first");
                if (message.Kind == "fault") throw new InvalidOperationException("Park the running job.");
                if (message.Kind != Contract.Kinds.JobFinished) continue;
                var result = Json.Read<Contract.JobFinished>(message.Payload!);
                var value = Json.Read<string>(result.Result!);
                Results.Add(value);
                turn.State = value + " applied";
                if (value == "first") turn.StartJob(Key, "second");
            }
            return default;
        }
    }
}
