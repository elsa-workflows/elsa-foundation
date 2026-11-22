using Kernel.Foundation.InMemory;
using Kernel.Native;
using Contract = Kernel.Foundation.Contracts;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

internal static class CascadePageChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        await Run(CascadeProtocol.PageLimit, check);
        await Run(2 * CascadeProtocol.PageLimit + 1, check, delete: true);
        await Run(2, check, jobs: true);
        await Run(10_001, check, loseAfter: true);
        await Run(CascadeProtocol.PageLimit + 1, check, loseAfter: false);
        await Run(10_001, check, delete: true, loseAfter: true);
        await Run(2 * CascadeProtocol.PageLimit + 1, check, delete: true, loseAfter: false);
        await Run(CascadeProtocol.PageLimit + 1, check, keepRunning: true);
        await RunNative(check);
        await RunModelCap(check);
    }

    private static async Task RunNative(Action<bool, string> check)
    {
        const long now = 1_702_300_050_000;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("native-cascade");
        var ingress = new InMemoryIngress(store, new Requests("native-cascade"), "native-cascade", (_, kind) => kind == Contract.Kinds.Terminate);
        var reads = new ActorList(store, 10);
        var runtime = new PublicRuntime(ingress, reads);
        var count = CascadeProtocol.PageLimit + 1;
        var workflows = new WorkflowRegistry()
            .Add(Wf.Workflow("leaf", Wf.Delay("waiting", 100_000)))
            .Add(Wf.Workflow("tree", Wf.Parallel("branches", null,
                Enumerable.Range(0, count).Select(index => Wf.Dispatch($"dispatch-{index}", "leaf", wait: true)).ToArray())));
        var pages = new List<TurnCommit>();
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (landed) pages.AddRange(batch.Turns.Where(turn => turn.FoundationCascade is not null));
        });
        var worker = new Worker("native-cascade", 0, wire, new ActorAdapter(store, new PublicWorkflow([new NativeExecutionModel(workflows)])),
            new WorkerOptions { Monotonic = () => 0 });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var opened = await runtime.StartAsync(scope, "tree", NativeExecutionModel.Start("tree", Modes.Balanced));
        var root = opened.Actor!.Value;
        var address = store.FoundationTarget(scope.Tenant, root.Id)!.Id;
        await Pump();
        var parent = store.Instances[address];
        var recipients = ChildRegistry.Read(parent.Children);
        check(recipients.Count == count && recipients.All(child => store.Instances.GetValueOrDefault(child.Id) is
            { Parent: var id, ParentCreation: var creation, Status: InstanceStatus.Running } && id == address && creation == parent.Creation),
            "Native starts 1001 live children bound to the exact parent creation");
        if (recipients.Count != count) return;
        var selection = (await reads.GetAsync(scope, root))!.Selection!;
        await ingress.SendAsync(scope, new Contract.Target.Ref(root), "end", Contract.Kinds.Terminate, Json.Write(new Contract.OperatorCommand(selection)));
        await Pump();
        check(store.Instances[address] is { Status: InstanceStatus.Cancelled, Cascade: null, Children: null } &&
            recipients.All(child => store.Instances[child.Id].Status == InstanceStatus.Cancelled) && pages.Count == 2 &&
            pages.SelectMany(page => page.Sent).Count(message => message.CascadeOrigin) == count,
            "the real Native cascade ends every live child in two durable pages without repeating a send");

        async Task Pump()
        {
            for (var pass = 0; pass < 24; pass++)
            {
                await owner.WokenAsync(null);
                await owner.RunAsync();
            }
        }
    }

    private static async Task Run(int count, Action<bool, string> check, bool delete = false, bool jobs = false,
        bool? loseAfter = null, bool keepRunning = false)
    {
        const long initial = 1_702_300_000_000;
        var now = initial;
        var store = new InMemoryStore(2, () => now) { IngressDeduplicationWindow = TimeSpan.FromMilliseconds(100) };
        var scope = new Contract.BoundScope("cascade-pages");
        var edge = new InMemoryIngress(store, new Requests("cascade-paging"), "cascade-paging",
            (_, kind) => kind is Contract.Kinds.Delete or Contract.Kinds.Terminate);
        var probe = new Probe();
        var spec = new Contract.StartSpec(probe.Type, "root");
        var key = new Contract.Target.Key("cascade-root", spec);
        var started = await edge.SendAsync(scope, key, "start", Contract.Kinds.Start);
        var root = started.Actor!.Value;
        var target = new Contract.Target.Ref(root);
        var address = store.FoundationTarget(scope.Tenant, root.Id)!.Id;
        var partition = new InstanceRef(address).Partition;
        var other = 1 - partition;

        var landedPages = new List<TurnCommit>();
        var failed = false;
        var canFail = !delete;
        var markedTested = false;
        var markRefused = false;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            foreach (var turn in batch.Turns.Where(turn => turn.FoundationCascade is not null))
            {
                if (delete && !landed && !markedTested && turn.Sent.FirstOrDefault(message => message.Kind == Kinds.Cascade) is { } control)
                {
                    markedTested = true;
                    try { _ = store.CommitAsync(batch with { Seen = [control.InboxKey] }).Result; }
                    catch (StoreException error) when (error.Failure == Failure.Definite) { markRefused = true; }
                }
                if (landed) landedPages.Add(turn);
                if (canFail && !failed && loseAfter == landed && turn.FoundationCascade!.Input is not null)
                {
                    failed = true;
                    throw new StoreException(Failure.Unknown, "cascade page answer unavailable");
                }
            }
        });
        Worker NewWorker() => new("cascade-paging", partition, wire, new ActorAdapter(store, probe), new WorkerOptions { Monotonic = () => 0 });

        var worker = NewWorker();
        await using (var setup = (await new DurableHost([worker]).ActivateAsync(partition))!)
            await Step(worker, address);

        var row = store.Instances[address];
        var children = Enumerable.Range(0, count).Select(index => new CreationAddress(InstanceRef.In(other, $"child-{index:D5}").Id, 1)).ToList();
        var registry = jobs ? new List<JobRegistration> { new("missing", 1), new("present", 2), new("kept", 3), new("completed", 4) } : [];
        store.Instances[address] = row with
        {
            Children = ChildRegistry.Write(children), Jobs = JobRegistry.Write(registry),
            NextJobGeneration = jobs ? 5 : row.NextJobGeneration
        };
        var present = InboxKey.Ticket(address, "present", row.Creation, 2);
        var older = InboxKey.Ticket(address, "present", row.Creation, 1);
        var kept = InboxKey.Ticket(address, "kept", row.Creation, 3);
        var result = InboxKey.JobResult(address, "completed", row.Creation, 4);
        if (jobs)
        {
            InsertTicket(present, "present", 2);
            InsertTicket(older, "present", 1);
            InsertTicket(kept, "kept", 3);
            store.Messages[kept].Done = true;
            store.Messages[kept].KeptBy = ParkKeepReason.Consumed;
            var completed = new Message(result.Id, new(address), Kinds.JobFinished, Json.Write(new JobFinished("completed", "done", null)),
                address, Due: now + 100_000, TargetCreation: row.Creation, SenderCreation: row.Creation)
                { JobGeneration = 4, FoundationJobResult = true };
            store.Messages.Add(result, new(completed.WithInboxScope(result.Scope), 4, 0));
        }

        worker = NewWorker();
        await using var owner = (await new DurableHost([worker]).ActivateAsync(partition))!;
        if (keepRunning)
        {
            var selection = (await new ActorList(store, 10).GetAsync(scope, root))!.Selection!;
            await edge.SendAsync(scope, target, "stop", Contract.Kinds.Terminate, Json.Write(new Contract.OperatorCommand(selection)));
        }
        else await edge.SendAsync(scope, target, "stop", "stop");
        if (jobs)
        {
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            var late = store.Messages[present];
            late.Message = (late.Message with { Due = null }).AsKernelTicket();
            await worker.PollAsync();
            worker.RunTurn(address);
            await worker.FlushAsync();
        }
        else await Step(worker, address);
        var ended = store.Instances[address];
        var expectedStatus = keepRunning ? InstanceStatus.Running : InstanceStatus.Cancelled;
        check(ended is { Children: null, Jobs: null } && ended.Status == expectedStatus && landedPages.Count == 1 &&
            Stops().Count == Math.Min(CascadeProtocol.PageLimit - registry.Count, count),
            keepRunning ? "a consumed Terminate cascades even when the actor keeps its Running status" :
                $"the real ending turn discharges the first bounded page for {count} children");
        if (jobs)
            check(!store.Messages.ContainsKey(present) && !store.Messages.ContainsKey(result) && store.Messages.ContainsKey(older) &&
                store.Messages[kept] is { Done: true, KeptBy: null },
                "the page ignores a missing job, settles the present and kept generation, and preserves the older key");

        if (delete && ended.Cascade is not null)
        {
            check(markRefused, "a newly minted cascade control cannot be marked seen within its issuing batch");
            await Step(worker, address);
            var outstanding = store.Instances[address].Cascade!;
            var control = store.Messages[outstanding.Control];
            var stale = (control.Message with { Id = control.Message.Id + ":wrong-key" }).AsKernelTicket();
            store.Messages.Add(stale.InboxKey, new(stale, control.Seq - 1, 0));
            await Step(worker, address);
            check(!store.Messages.ContainsKey(stale.InboxKey) && store.Instances[address].Cascade == outstanding,
                "a different private control is consumed without advancing the outstanding cascade");
            var selection = (await new ActorList(store, 10).GetAsync(scope, root))!.Selection!;
            await edge.SendAsync(scope, target, "delete", Contract.Kinds.Delete, Json.Write(new Contract.OperatorCommand(selection)));
            // The control and the delete can arrive in either order across delivery providers.
            var deletion = store.Messages.Values.Single(message => message.Message.Kind == Kinds.Delete);
            deletion.Seq = store.Messages[outstanding.Control].Seq - 1;
            await Step(worker, address);
            check(store.Instances[address] is { Retired: true, Cascade: { } retained } && retained == outstanding &&
                Stops().Count == 2 * CascadeProtocol.PageLimit,
                "qualified Delete preserves the live cursor and outstanding control on its retirement marker");
            now += 101;
            var blocked = await edge.SendAsync(scope, key, "replacement", Contract.Kinds.Start);
            var repeat = await edge.SendAsync(scope, key, "replacement", Contract.Kinds.Start);
            var otherOperation = await edge.SendAsync(scope, key, "other-replacement", Contract.Kinds.Start);
            check(new[] { blocked, repeat, otherOperation }.All(receipt => receipt is
                { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.ObligationOutstanding }),
                "a retired caller key with an outstanding cascade refuses a fresh start before issuing a receipt");
            canFail = true;
        }

        if (keepRunning)
        {
            var outstanding = ended.Cascade!;
            await edge.SendAsync(scope, target, "ping", "ping");
            store.Messages.Values.Single(item => item.Message.Kind == "ping").Seq = store.Messages[outstanding.Control].Seq - 1;
            await Step(worker, address);
            check(probe.Pulses == 0 && store.Instances[address] is { Status: InstanceStatus.Running, Cascade: null },
                "the same running actor discharges its remaining old registry before its next authored turn");
            await Step(worker, address);
            check(probe.Pulses == 1, "the running actor accepts its next authored input after the cascade");
        }
        else if (loseAfter.HasValue)
        {
            var beforeLoss = Stops().Count;
            await Step(worker, address);
            check(failed && !worker.Owns && Stops().Count == Math.Min(count, beforeLoss + (loseAfter.Value ? CascadeProtocol.PageLimit : 0)),
                "a lost page answer leaves exactly the sends from pages that committed");
            await owner.DisposeAsync();
            worker = NewWorker();
            await using var successor = (await new DurableHost([worker]).ActivateAsync(partition))!;
            await Finish();
        }
        else await Finish();

        var final = store.Instances[address];
        var sent = Stops();
        check(final.Cascade is null && sent.Count == count && sent.Select(message => message.Target.Id).Distinct().Count() == count &&
            landedPages.Count == (count + registry.Count + CascadeProtocol.PageLimit - 1) / CascadeProtocol.PageLimit &&
            landedPages.All(page => page.Sent.Count <= CascadeProtocol.PageLimit + 1),
            "every exact child receives one stored terminate and the final page clears its obligation");
        if (delete)
        {
            var accepted = await edge.SendAsync(scope, key, "replacement", Contract.Kinds.Start);
            var blocked = await edge.SendAsync(scope, key, "other-replacement", Contract.Kinds.Start);
            check(accepted is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } replacement } && replacement != root,
                "the same replacement operation is accepted after the retired cascade finishes");
            check(blocked is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.Repeat },
                "another operation on the replacement key meets the new creation's start");
        }

        List<Message> Stops() => store.Messages.Values.Where(item => item.Message is { CascadeOrigin: true, Kind: Kinds.Terminate })
            .Select(item => item.Message).ToList();

        async Task Finish()
        {
            for (var page = 0; page < 12 && store.Instances[address].Cascade is not null; page++)
                await Step(worker, address);
        }

        void InsertTicket(InboxKey ticket, string name, long generation)
        {
            var message = new Message(ticket.Id, new(address), Kinds.Job, Json.Write(new JobTicket(probe.Type, name, null)),
                address, Due: now + 100_000, TargetCreation: row.Creation, SenderCreation: row.Creation)
                { JobGeneration = generation };
            store.Messages.Add(ticket, new(message.WithInboxScope(ticket.Scope).AsKernelTicket(), generation, 0));
        }
    }

    private static async Task RunModelCap(Action<bool, string> check)
    {
        const long now = 1_702_300_070_000;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("cascade-send-cap");
        var probe = new Probe();
        var edge = new InMemoryIngress(store, new Requests("model-cap"), "model-cap");
        var actor = (await edge.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "root")), "start", Contract.Kinds.Start)).Actor!.Value;
        var address = store.FoundationTarget(scope.Tenant, actor.Id)!.Id;
        var worker = new Worker("model-cap", 0, store, new ActorAdapter(store, probe), new WorkerOptions { Monotonic = () => 0 });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Step(worker, address);
        await edge.SendAsync(scope, new Contract.Target.Ref(actor), "oversize", "oversize");
        await Step(worker, address);
        check(store.Instances[address] is { Status: InstanceStatus.Faulted, Cascade: null } &&
            store.Messages.Values.All(row => row.Message.Kind != "surplus"),
            "the model's 10001-send turn still parks without committing any of its sends");
    }

    private static async Task Step(Worker worker, string actor)
    {
        await worker.PollAsync();
        await worker.LoadAsync();
        await worker.FlushAsync();
        worker.RunTurn(actor);
        await worker.FlushAsync();
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "cascade-pages";
        public int Pulses { get; private set; }
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Inbox.Any(message => message.Kind == "stop")) turn.End(Contract.ActorStatus.Cancelled);
            if (turn.Inbox.Any(message => message.Kind == "ping")) Pulses++;
            if (turn.Inbox.Any(message => message.Kind == "oversize"))
                for (var i = 0; i < 10_001; i++) turn.Send(turn.Header.Ref, "surplus");
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
    }
}
