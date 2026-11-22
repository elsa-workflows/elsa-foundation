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
        await Run(CascadeProtocol.PageLimit + 1, check, terminalFollowup: true);
        await RunAuthoredTerminate(check);
        await RefuseLegacyRebinding(check);
        await RunNative(check);
        await RunNative(check, thenDelete: true);
        await CheckDirectThenDelete(alreadyEnded: true, hasChild: false, protect: false, check);
        await CheckDirectThenDelete(alreadyEnded: true, hasChild: true, protect: false, check);
        await CheckDirectThenDelete(alreadyEnded: false, hasChild: true, protect: true, check);
        await CheckMixedThenDelete(asChild: false, alreadyEnded: false, protect: false, check);
        await CheckMixedThenDelete(asChild: true, alreadyEnded: false, protect: false, check);
        await CheckMixedThenDelete(asChild: true, alreadyEnded: false, protect: true, check);
        await CheckMixedThenDelete(asChild: false, alreadyEnded: true, protect: false, check);
        await CheckMixedThenDelete(asChild: true, alreadyEnded: true, protect: false, check);
        await RunModelCap(check);
    }

    private static async Task RunAuthoredTerminate(Action<bool, string> check)
    {
        var store = new InMemoryStore(1, () => 1_702_300_030_000);
        var scope = new Contract.BoundScope("authored-then-end");
        var probe = new Probe();
        var ingress = new InMemoryIngress(store, new Requests("authored-then-end"), "authored-then-end");
        var root = (await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "root")), "start", Contract.Kinds.Start)).Actor!.Value;
        var address = store.FoundationTarget(scope.Tenant, root.Id)!.Id;
        var worker = new Worker("authored-then-end", 0, store, new ActorAdapter(store, probe), new WorkerOptions { Monotonic = () => 0 });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Step(worker, address);
        await ingress.SendAsync(scope, new Contract.Target.Ref(root), "register", "register-child");
        await Step(worker, address);
        var child = ChildRegistry.Read(store.Instances[address].Children).Single();
        await ingress.SendAsync(scope, new Contract.Target.Ref(root), "terminate", "end-after-explicit");
        await Step(worker, address);
        var terminates = store.Messages.Values.Select(item => item.Message).Where(message =>
            message.Kind == Kinds.Terminate && message.Target.Id == child.Id && message.TargetCreation == child.Creation).ToArray();
        check(store.Instances[address] is { Status: InstanceStatus.Cancelled, Cascade: null } &&
            terminates.Count(message => message.ActorAuthored) == 1 && terminates.Count(message => message.CascadeOrigin) == 1,
            "an authored Terminate stays valid when End(Cancelled) moves that child's registration into the cascade");
    }

    private static async Task RefuseLegacyRebinding(Action<bool, string> check)
    {
        var store = new InMemoryStore(1, () => 100);
        var foreign = InstanceRef.In(0, "foreign");
        var branch = InstanceRef.In(0, "branch");
        var leaf = InstanceRef.In(0, "leaf");
        var before = new InstanceRow(branch.Id, "probe", "branch", InstanceStatus.Running, "null", 1, "0/parent",
            Children: ChildRegistry.Write([new(leaf.Id, 0)]));
        store.Instances[branch.Id] = before;
        store.Instances[foreign.Id] = new(foreign.Id, "probe", "foreign", InstanceStatus.Running, "null", 1, null);
        var lease = (await store.TakeAsync(0, "cascade-rebinding", Defaults.Lease))!;
        var command = new Message("foreign-command", branch, Kinds.Terminate, Sender: foreign.Id).AsIssued().AsCascadeTerminate();
        var batch = new CommitBatch(0, lease.Epoch, lease.Position, Defaults.Lease, false,
            [new(foreign.Id, null, null, [], [], [command], [], [], null)], [], []);
        var sent = await store.CommitAsync(batch);

        var registry = CascadeRegistry.From(before);
        foreach (var status in new[] { InstanceStatus.Running, InstanceStatus.Cancelled })
        {
            var next = before with { Parent = foreign.Id, Revision = 2, Children = null, Status = status };
            var page = CascadeProtocol.Page(next, registry, 0, next.Revision);
            var proposed = new TurnCommit(branch.Id, next, before.Revision, [command.InboxKey], [], page.Sent, [], [], null)
                { FoundationCascade = new(registry) };
            var refused = false;
            try { await store.CommitAsync(batch with { Turns = [proposed] }); }
            catch (StoreException error) when (error.Failure == Failure.Definite) { refused = true; }
            check(sent.Landed && refused && store.Instances[branch.Id] == before && store.Messages.ContainsKey(command.InboxKey) &&
                page.Sent.All(message => !store.Messages.ContainsKey(message.InboxKey)),
                $"a legacy cascade cannot authenticate its terminate by replacing the recorded parent and writing {status}");
        }
    }

    private static async Task RunNative(Action<bool, string> check, bool thenDelete = false)
    {
        const long now = 1_702_300_050_000;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("native-cascade");
        var allowDelete = false;
        var ingress = new InMemoryIngress(store, new Requests("native-cascade"), "native-cascade",
            (_, kind) => kind == Contract.Kinds.Terminate || allowDelete && kind == Contract.Kinds.Delete);
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
        var target = new Contract.Target.Ref(root);
        if (thenDelete)
        {
            var attempt = new Contract.OperatorCommand(selection, ThenDelete: true, ExpectedTarget: root);
            var refused = await ingress.SendAsync(scope, target, "requires-delete", Contract.Kinds.Terminate, Json.Write(attempt));
            var noDeletePermission = refused.Outcome == Contract.AdmissionOutcome.Refused;
            allowDelete = true;
            var wrong = attempt with { ExpectedTarget = new Contract.ActorRef("another-target") };
            var mismatched = await ingress.SendAsync(scope, target, "wrong-target", Contract.Kinds.Terminate, Json.Write(wrong));
            check(noDeletePermission && mismatched is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.TargetMismatch } &&
                store.Instances[address] == parent && pages.Count == 0,
                "PC-5 ThenDelete needs two permissions and pins the exact target before a model turn");
        }
        var command = new Contract.OperatorCommand(selection, ThenDelete: thenDelete, ExpectedTarget: thenDelete ? root : null);
        var accepted = await ingress.SendAsync(scope, target, "end", Contract.Kinds.Terminate, Json.Write(command));
        await Pump();
        var marker = store.Instances[address];
        check(accepted.Outcome == Contract.AdmissionOutcome.Accepted &&
            marker is { Status: InstanceStatus.Cancelled, Cascade: null, Children: null } && marker.Retired == thenDelete &&
            recipients.All(child => store.Instances[child.Id].Status == InstanceStatus.Cancelled) && pages.Count == 2 &&
            pages.SelectMany(page => page.Sent).Count(message => message.CascadeOrigin) == count,
            thenDelete ? "PC-5 the real Native ThenDelete sends two fenced cascade pages and cancels every child"
                : "the real Native cascade ends every live child in two durable pages without repeating a send");
        if (thenDelete)
            check(pages.FirstOrDefault() is { Delete: true, Row: { Retired: true, Cascade.Cursor: CascadeProtocol.PageLimit } } &&
                store.Journal.Values.Where(row => row.Instance == address).ToArray() is [{ Revision: -1 }],
                "PC-5 ThenDelete retires with page one's durable cursor and one deletion marker in the same batch");

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
        bool? loseAfter = null, bool keepRunning = false, bool terminalFollowup = false)
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
        var ordinaryEndTested = false;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            foreach (var turn in batch.Turns.Where(turn => turn.FoundationCascade is not null))
            {
                if (!landed && !ordinaryEndTested && count == CascadeProtocol.PageLimit && !keepRunning)
                {
                    ordinaryEndTested = true;
                    foreach (var status in new[] { InstanceStatus.Finished, InstanceStatus.Faulted })
                    {
                        var invalid = turn with { Row = turn.Row! with { Status = status } };
                        var turns = batch.Turns.Select(candidate => ReferenceEquals(candidate, turn) ? invalid : candidate).ToArray();
                        var refused = false;
                        try { _ = store.CommitAsync(batch with { Turns = turns }).Result; }
                        catch (StoreException error) when (error.Failure == Failure.Definite) { refused = true; }
                        check(refused, $"the store refuses a cascade on an ordinary {status} without consuming Terminate");
                    }
                }
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

        CreationAddress? laterChild = null;
        if (terminalFollowup)
        {
            await edge.SendAsync(scope, target, "followup", "register-child");
            var control = ended.Cascade!.Control;
            store.Messages.Values.Single(item => item.Message.Kind == "register-child").Seq = store.Messages[control].Seq - 1;
            await Step(worker, address);
            laterChild = ChildRegistry.Read(store.Instances[address].Children).Single();
        }

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
        if (laterChild is not null)
            check(final.Cascade is null && ChildRegistry.Read(final.Children).SequenceEqual([laterChild]) &&
                sent.All(message => message.Target.Id != laterChild.Id),
                "a continuation clears its original obligation and preserves a terminal actor's later child registration");
        if (delete)
        {
            var accepted = await edge.SendAsync(scope, key, "replacement", Contract.Kinds.Start);
            var blocked = await edge.SendAsync(scope, key, "other-replacement", Contract.Kinds.Start);
            check(accepted is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } replacement } && replacement != root,
                "the same replacement operation is accepted after the retired cascade finishes");
            check(blocked is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.Repeat },
                "another operation on the replacement key meets the new creation's start");
            if (!loseAfter.HasValue)
            {
                probe.CancelOnStart = true;
                for (var pass = 0; pass < 5; pass++) await Step(worker, address);
                var replacementCreation = checked(row.Creation + 1);
                check(store.Instances[address] is { Status: InstanceStatus.Cancelled, Retired: false } successor &&
                    successor.Creation == replacementCreation &&
                    Stops().Count(message => message.Sender == address && message.SenderCreation == replacementCreation) == 1,
                    "a successor can start and terminate an attached child in its birth turn over a retired predecessor");
            }
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

    private static async Task CheckDirectThenDelete(bool alreadyEnded, bool hasChild, bool protect, Action<bool, string> check)
    {
        const long now = 1_702_300_060_000;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("then-delete");
        var probe = new Probe { Protect = protect };
        var edge = new InMemoryIngress(store, new Requests("then-delete"), "then-delete",
            (_, kind) => kind is Contract.Kinds.Terminate or Contract.Kinds.Delete);
        var birth = await edge.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "root")), "start", Contract.Kinds.Start);
        var actor = birth.Actor!.Value;
        var target = new Contract.Target.Ref(actor);
        var address = store.FoundationTarget(scope.Tenant, actor.Id)!.Id;
        var worker = new Worker("then-delete", 0, store, new ActorAdapter(store, probe), new WorkerOptions { Monotonic = () => 0 });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Step(worker, address);
        if (hasChild)
        {
            await edge.SendAsync(scope, target, "child", "register-child");
            await Step(worker, address);
        }
        var recipients = ChildRegistry.Read(store.Instances[address].Children);
        if (alreadyEnded)
        {
            await edge.SendAsync(scope, target, "finish-with-registry", "finish");
            await Step(worker, address);
        }
        var before = store.Instances[address];
        var admission = await edge.SendAsync(scope, target, "then-delete-command", Contract.Kinds.Terminate,
            Json.Write(new Contract.OperatorCommand(null, ThenDelete: true, ExpectedTarget: actor)));
        await Step(worker, address);
        var after = store.Instances[address];
        var journal = store.Journal.Values.Where(row => row.Instance == address).ToArray();
        var sent = recipients.Count(child => store.Messages.Values.Any(row => row.Message is { CascadeOrigin: true } message &&
            message.Target.Id == child.Id && message.TargetCreation == child.Creation));
        check(admission.Outcome == Contract.AdmissionOutcome.Accepted && (before.Status == InstanceStatus.Finished) == alreadyEnded &&
            recipients.Count == (hasChild ? 1 : 0) && sent == recipients.Count && after.Retired == !protect &&
            after.Status == (alreadyEnded ? InstanceStatus.Finished : InstanceStatus.Cancelled) &&
            (protect ? journal.All(row => row.Revision != -1) : journal is [{ Revision: -1 }]),
            $"PC-5 ThenDelete preserves the correct disposition and child binding for ended={alreadyEnded}, child={hasChild}, protected={protect}");
    }

    private static async Task CheckMixedThenDelete(bool asChild, bool alreadyEnded, bool protect, Action<bool, string> check)
    {
        const long now = 1_702_300_065_000;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("mixed-then-delete");
        var probe = new Probe { Protect = protect, HonourTerminate = true };
        var scheduler = new HeldScheduler();
        var edge = new InMemoryIngress(store, new Requests("mixed-then-delete"), "mixed-then-delete",
            (_, kind) => kind is Contract.Kinds.Terminate or Contract.Kinds.Delete);
        var pages = new List<TurnCommit>();
        var writes = new List<TurnCommit>();
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (!landed) return;
            writes.AddRange(batch.Turns);
            pages.AddRange(batch.Turns.Where(turn => turn.FoundationCascade is not null));
        });
        var worker = Owner("mixed-first");
        await using var first = await Activate(worker);
        var created = await edge.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "root")), "start", Contract.Kinds.Start);
        var root = new Contract.Target.Ref(created.Actor!.Value);
        var rootId = Physical(root);
        await Step(worker, rootId);
        var target = root;
        if (asChild)
        {
            await edge.SendAsync(scope, root, "branch", "register-child");
            await Step(worker, rootId);
            target = new Contract.Target.Ref(new(store.Instances[rootId].State!));
        }
        var id = Physical(target);
        await edge.SendAsync(scope, target, "register-mixed", "register-mixed");
        await Pump(first);
        var previous = store.Instances[id];
        var entries = CascadeRegistry.From(previous);
        var ticket = InboxKey.Ticket(id, "cascade-job", previous.Creation);
        var guard = entries.Children.Length == CascadeProtocol.PageLimit && entries.Jobs is [{ Key: "cascade-job" }] &&
            store.Messages.ContainsKey(ticket) &&
            entries.Children.All(child => store.Instances.GetValueOrDefault(child.Id) is { Status: InstanceStatus.Running });
        var label = $"child={asChild}, ended={alreadyEnded}, protected={protect}";
        check(guard, $"PC-5 {label}: the real Foundation holds 1,000 child registrations and one pending job");
        if (!guard) return;
        if (alreadyEnded)
        {
            await edge.SendAsync(scope, target, "finish", "finish");
            await Step(worker, id);
        }

        var registryAtCommand = CascadeRegistry.From(store.Instances[id]);
        var ticketAtCommand = store.Messages.ContainsKey(ticket);
        var command = new Contract.OperatorCommand(null, ThenDelete: true, ExpectedTarget: target.Actor);
        var admission = await edge.SendAsync(scope, target, "end-delete", Contract.Kinds.Terminate, Json.Write(command));
        for (var pass = 0; pass < 8 && !pages.Any(turn => turn.Instance == id); pass++)
            await Step(worker, id);
        var firstPage = store.Instances[id];
        var disposition = alreadyEnded ? InstanceStatus.Finished : InstanceStatus.Cancelled;
        var bounded = admission.Outcome == Contract.AdmissionOutcome.Accepted &&
            registryAtCommand.Count == CascadeProtocol.PageLimit + 1 && ticketAtCommand && firstPage.Status == disposition &&
            firstPage.Retired == !protect && firstPage.Cascade is { Cursor: CascadeProtocol.PageLimit } &&
            !store.Messages.ContainsKey(ticket) &&
            pages.Where(turn => turn.Instance == id).SelectMany(turn => turn.Sent).Count(message => message.CascadeOrigin) ==
                CascadeProtocol.PageLimit - 1;
        check(bounded, $"PC-5 {label}: the first fenced commit discharges the job and 999 children with its stored cursor" +
            (bounded ? "" : $" (at command: {registryAtCommand.Count} entries, ticket={ticketAtCommand}; admission={admission.Outcome}; " +
                $"after: {firstPage.Status}, retired={firstPage.Retired}, cursor={firstPage.Cascade?.Cursor})"));
        if (!bounded) return;

        await first.DisposeAsync();
        scheduler.RunAll();
        await using var cold = await Activate(Owner("mixed-cold"));
        await Pump(cold);
        var journal = store.Journal.Values.Where(row => row.Instance == id).ToArray();
        var sent = pages.Where(turn => turn.Instance == id).SelectMany(turn => turn.Sent).Where(message => message.CascadeOrigin).ToArray();
        var notices = writes.SelectMany(turn => turn.Sent).Count(message =>
            message.Kind == Kinds.ChildFinished && message.Sender == id && message.Read<FoundationChildNotice>().Final);
        check(store.Instances[id] is { Cascade: null } && sent.Length == CascadeProtocol.PageLimit &&
            sent.Select(message => message.Target.Id).Distinct().Count() == CascadeProtocol.PageLimit &&
            entries.Children.All(child => store.Instances[child.Id].Status == InstanceStatus.Cancelled) &&
            (protect ? journal.All(row => row.Revision >= 0) : journal is [{ Revision: -1 }]) &&
            notices == (asChild ? 1 : 0) && probe.JobEntries == 0,
            $"PC-5 {label}: the new owner finishes the suffix once with the correct terminal journal and parent notice");

        string Physical(Contract.Target.Ref reference) => store.FoundationTarget(scope.Tenant, reference.Actor.Id)!.Id;
        Worker Owner(string node) => new(node, 0, wire, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => 0, JobScheduler = scheduler });
        static async Task<IScopeActivation> Activate(Worker owner) => (await new DurableHost([owner]).ActivateAsync(0))!;
        static async Task Pump(IScopeActivation owner)
        {
            for (var pass = 0; pass < 24; pass++)
            {
                await owner.WokenAsync(null);
                await owner.RunAsync();
            }
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
        public bool CancelOnStart { get; set; }
        public bool Protect { get; init; }
        public bool HonourTerminate { get; init; }
        internal int JobEntries { get; private set; }
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Inbox.Any(message => message.Kind == "register-child")) turn.State = turn.Start(new(Type, "leaf")).Id;
            if (HonourTerminate && turn.Header.Status == Contract.ActorStatus.Running &&
                turn.Inbox is [{ Kind: Contract.Kinds.Terminate }]) turn.End(Contract.ActorStatus.Cancelled);
            if (turn.Inbox.Any(message => message.Kind == "register-mixed"))
            {
                for (var index = 0; index < CascadeProtocol.PageLimit; index++) turn.Start(new(Type, "leaf"));
                turn.StartJob("cascade-job", timeout: TimeSpan.FromMinutes(1));
            }

            if (turn.Inbox.Any(message => message.Kind == "end-after-explicit"))
            {
                turn.Send(new(turn.State!), Contract.Kinds.Terminate);
                turn.End(Contract.ActorStatus.Cancelled);
            }
            if (CancelOnStart && turn.Header.TypeData == "root" && turn.Inbox.Any(message => message.Kind == Contract.Kinds.Start))
            {
                turn.Start(new(Type, "leaf"));
                turn.End(Contract.ActorStatus.Cancelled);
            }
            if (turn.Inbox.Any(message => message.Kind == "finish")) turn.End(Contract.ActorStatus.Finished);
            if (turn.Inbox.Any(message => message.Kind == "stop")) turn.End(Contract.ActorStatus.Cancelled);
            if (turn.Inbox.Any(message => message.Kind == "ping")) Pulses++;
            if (turn.Inbox.Any(message => message.Kind == "oversize"))
                for (var i = 0; i < 10_001; i++) turn.Send(turn.Header.Ref, "surplus");
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => Protect;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
    }
}
