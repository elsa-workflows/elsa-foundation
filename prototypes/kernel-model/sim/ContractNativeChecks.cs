using Kernel.Foundation.InMemory;
using Kernel.Native;
using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;

namespace Kernel.Sim;

internal static class ContractNativeChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var direct = typeof(NativeExecutionModel).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();
        var graph = typeof(Kernel.Graph.Models.FlowchartConnection).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name);
        check(direct.Contains("Kernel.Foundation.Contracts") && direct.Contains("Kernel.Model.Contracts") &&
            direct.All(name => name is "Kernel.Foundation.Contracts" or "Kernel.Model.Contracts" or "Kernel.Graph" or "Kernel.Native.Activities" ||
                name?.StartsWith("System.", StringComparison.Ordinal) == true) &&
            graph.All(name => name?.StartsWith("System.", StringComparison.Ordinal) == true),
            "the compiled Native execution model uses the public contracts and its layer-three libraries");
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
        {
            await Run((ok, label) => check(ok, $"{mode}: {label}"), mode);
            await ReceiptLifetime((ok, label) => check(ok, $"{mode}: {label}"), mode);
        }
        await CompletedJobRestore(check);
    }

    private static async Task Run(Action<bool, string> check, string mode)
    {
        const long initial = 1_701_800_000_000;
        const long due = initial + 100;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("acme");
        Contract.IIngress ingress = new InMemoryIngress(store, new Requests("native-contract"), "native-contract");
        Contract.IActorReads reads = new ActorList(store, 32);
        var runtime = new PublicRuntime(ingress, reads);
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("nested", Wf.Set("nested-output", "output", "child value")))
            .Add(Wf.Workflow("main", Wf.Sequence("sequence",
                Wf.Delay("delay", 100),
                Wf.Call("lookup", "pricing"),
                Wf.Dispatch("child", "nested", wait: true),
                Wf.Event("signal", "approval"),
                Wf.Http("send", "https://partner.invalid/notify"))));
        var sent = new List<NativeOutbound>();
        var model = new NativeExecutionModel(registry, outbound: sent.Add);
        var reference = (await runtime.StartAsync(scope, "start-native", NativeExecutionModel.Start("main", mode))).Actor!.Value;
        var target = new Contract.Target.Ref(reference);
        var owner = await Activate("native-first");
        await Pump(owner);

        var saved = State();
        var (wait, timer) = saved.Waits.Single(item => item.Value.Kind == "timer");
        var inputs = await runtime.InboxAsync(scope, reference);
        var restore = model.RestoreWaits(new(reference, scope.Tenant, model.Key, "main", null), saved);
        check(timer.Key.StartsWith('~') && timer.Due == due && inputs.Items is [{ Id: var id, Due: due }] && id == timer.Key &&
            restore.Timers is [{ Id: var restored, Kind: NativeKinds.Timer, Due: due, Payload: var payload }] &&
            restored == timer.Key && Json.Read<TimerFired>(payload!).Wait == wait,
            "Native commits the original timer recipe and the same opaque handle returned to the model");

        await ingress.SendAsync(scope, target, "forged-timer", NativeKinds.Timer, Json.Write(new TimerFired(wait)));
        await Pump(owner);
        check(State().Waits.ContainsKey(wait) && sent.Count == 0,
            "an outside timer-shaped message cannot release the Native wait");
        await ingress.SendAsync(scope, target, "malformed-timer", NativeKinds.Timer, "{");
        await Pump(owner);
        check(await reads.GetAsync(scope, reference) is { Header.Status: Contract.ActorStatus.Running, Park: null } &&
            State().Waits.ContainsKey(wait), "an unauthenticated malformed timer is ignored before decoding");
        await owner.DisposeAsync();

        now = due - 1;
        await using var successor = await Activate("native-next");
        await Pump(successor);
        check(State().Waits.ContainsKey(wait), "the Native timer remains dormant on the successor before its due reading");

        now = due;
        await Pump(successor);
        var after = State();
        check(after.Results.GetValueOrDefault("lookup")?.GetValue<string>() == "answer of pricing" &&
            after.Results.GetValueOrDefault("child")?["Output"]?.GetValue<string>() == "child value" &&
            after.Waits.Values.Any(item => item is { Kind: "event", Key: "event:approval" }),
            "the same Native engine runs the timed resume, background activity and exact child completion through the contract");

        await ingress.SendAsync(scope, new Contract.Target.Event("event:approval"), "approved", "published", "yes");
        await Pump(successor);
        var key = Models.EffectKey.For(reference.Id, "send", Models.EffectKey.RootPath, mode == Modes.Durable ? 1 : 0);
        check(await reads.GetAsync(scope, reference) is { Header.Status: Contract.ActorStatus.Finished } &&
            State().Results.GetValueOrDefault("signal")?["Payload"]?.GetValue<string>() == "yes" &&
            sent is [{ Actor: var sender, EffectKey: var effect, Effect: HttpSendEffect http }] &&
            sender == reference && effect == key && http.Request.Headers[SendHttpRequest.IdempotencyKeyHeader] == key,
            "Native finishes from the contract event and its HTTP activity derives the effect key from the opaque actor ref");

        async Task<IScopeActivation> Activate(string node)
        {
            var worker = new Worker(node, 0, store, new ActorAdapter(store, new PublicWorkflow([model])),
                new WorkerOptions { Monotonic = () => now - initial });
            return (await new DurableHost([worker]).ActivateAsync(0))!;
        }

        NativeState State() => Json.Read<NativeState>(store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row.State);
    }

    private static async Task ReceiptLifetime(Action<bool, string> check, string mode)
    {
        const long initial = 1_701_800_000_000;
        const string node = "native-receipts";
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("acme");
        var requests = new Requests(node);
        Contract.IIngress ingress = new InMemoryIngress(store, requests, node,
            (bound, kind) => bound == scope && kind == Contract.Kinds.Terminate);
        Contract.IActorReads reads = new ActorList(store, 16);
        var runtime = new PublicRuntime(ingress, reads);
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("receipt", Wf.Node<ReceiptReceiver>("receiver").With(Wf.Event("wait", "signal"))));
        var model = new NativeExecutionModel(registry);
        var actor = new PublicWorkflow([model]);
        var worker = new Worker(node, 0, store, new ActorAdapter(store, actor, (requests, node)),
            new WorkerOptions { Monotonic = () => now - initial, Transport = new LocalReplies(requests) });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var reference = (await runtime.StartAsync(scope, "start", NativeExecutionModel.Start("receipt", mode))).Actor!.Value;
        var target = new Contract.Target.Ref(reference);
        await Pump(owner);
        var frame = State().Inboxes!.Keys.Single();
        await ingress.SendAsync(scope, target, "admit", InvocationInput.Kind,
            Json.Write(new InvocationInput(frame, "input", "request", "body")));
        await Pump(owner);
        var admitted = State().Inboxes![frame].Receipts["input"];
        await runtime.TerminateAsync(scope, "end", (await reads.GetAsync(scope, reference))!);
        await Pump(owner);

        var terminal = await reads.GetAsync(scope, reference);
        var state = State();
        now += 4;
        var present = await Ask("present");
        var protectedBeforeExpiry = model.ProtectsDeletion(state, terminal?.Header.EndedAt, now);
        now++;
        var expired = await Ask("expired");
        check(admitted.Status == "Queued" && terminal is { Header: { Status: Contract.ActorStatus.Cancelled, EndedAt: initial } } &&
            present is { Result: "Found", Status: "NotProcessed", FinalizedAt: initial, ValidUntil: initial + 5 } &&
            protectedBeforeExpiry && expired?.Result == "MissingOrExpired" && !model.ProtectsDeletion(state, terminal.Header.EndedAt, now),
            "ended receipt queries and deletion protection share the committed expiry boundary");

        NativeState State() => Json.Read<NativeState>(store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row.State);

        async Task<Kernel.Native.Admission?> Ask(string id)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var reply = ingress.RequestAsync(scope, target, id, ReceiptQuery.Kind, Json.Write(new ReceiptQuery(frame, "input")),
                null, new Contract.RequestOptions(null, now + 1_000), timeout.Token).AsTask();
            await Pump(owner);
            return (await reply).Item as Kernel.Native.Admission;
        }
    }

    internal static async Task CheckRecoveryTimers(Action<bool, string> check)
    {
        await CheckRecoveredTimer(check);
        await CheckRearmedTimer(sweep: false, check);
        await CheckRearmedTimer(sweep: true, check);
        await CheckRearmedTimer(sweep: true, check, preserveAfterSweep: true);
    }

    private static async Task CheckRecoveredTimer(Action<bool, string> check)
    {
        const long initial = 1_701_800_015_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now) { ParkedRetentionWindow = TimeSpan.FromMinutes(1) };
        var scope = new Contract.BoundScope("native-recovery");
        var registry = new WorkflowRegistry().Add(Wf.Workflow("timer", Wf.Sequence("sequence",
            Wf.Delay("delay", 100), Wf.Set("finished-delay", "result", "continued"))));
        var model = new NativeExecutionModel(registry);
        Contract.IActorReads reads = new ActorList(store, 10);
        var timerOffers = 0;
        var worker = new Worker("native-recovery", 0, store, new ActorAdapter(store, new PublicWorkflow([model])),
            new WorkerOptions
            {
                Monotonic = () => now - initial,
                OnTurn = (_, inbox, _) => timerOffers += inbox.Count(message => message.Kind == NativeKinds.Timer)
            });
        var ingress = new InMemoryIngress(store, new Requests(worker.Node), worker.Node, (_, kind) => kind == Contract.Kinds.Recover);
        var runtime = new PublicRuntime(ingress, reads);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var reference = (await runtime.StartAsync(scope, "start", NativeExecutionModel.Start("timer", Modes.Balanced))).Actor!.Value;
        var target = new Contract.Target.Ref(reference);
        await Pump(owner);
        var timer = store.Messages.Values.Single(row => row.Message.Kind == NativeKinds.Timer);
        var timerKey = timer.Message.InboxKey;
        await ingress.SendAsync(scope, target, "invalid-resume", NativeKinds.Resume, "{");
        await Pump(owner);
        var parked = await reads.GetAsync(scope, reference);
        now += 100;
        await Pump(owner);
        var kept = parked is { Header.Status: Contract.ActorStatus.Faulted, Selection: not null } &&
            timer is { Done: true, KeptBy: ParkKeepReason.Arrived, Message.Payload: not null };
        check(kept, "a real Native Delay arrives with its original timer envelope while the workflow is parked");
        if (!kept) return;
        var saved = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row;
        var original = timer.Message;
        var recipes = new[] { new ParkTimerRecipe(original.Id, original.Kind, original.Payload, original.Due!.Value) };
        timer.KeptBy = null;
        timer.Message = original with { Payload = null };
        var marker = store.PlanFoundationTimers(saved, ParkRecoveryMode.Resume, recipes);
        store.Messages.Remove(timerKey);
        var absent = store.PlanFoundationTimers(saved, ParkRecoveryMode.Resume, recipes);
        timer.Message = original;
        timer.KeptBy = ParkKeepReason.Arrived;
        store.Messages.Add(timerKey, timer);
        check(saved.ParkTimerDispositions is null && marker?.Rearms is [var present] && absent?.Rearms is [var swept] &&
            present.DoneSequence == timer.Seq && swept.DoneSequence is null && present.Envelope == original && swept.Envelope == original,
            "the first Open rebuilds the same timer from a done marker or an absent row without a prior disposition");

        var admitted = await ingress.SendAsync(scope, target, "resume", Contract.Kinds.Recover,
            Json.Write(new Contract.OperatorCommand(parked!.Selection!)));
        for (var step = 0; step < 4; step++)
        {
            now += 11;
            await Pump(owner);
        }
        var checkpoint = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!;
        var state = Json.Read<NativeState>(checkpoint.Row.State);
        check(admitted.Outcome == Contract.AdmissionOutcome.Accepted && checkpoint is { Park: null, Row.Status: InstanceStatus.Finished } &&
            state.Waits.Count == 0 && timerOffers == 1 && state.Variables.GetValueOrDefault("result")?.GetValue<string>() == "continued" &&
            store.Messages.GetValueOrDefault(timerKey) is { Done: true, KeptBy: null } &&
            store.Messages.Values.Count(row => row.Message.Kind == NativeKinds.Timer) == 1,
            "qualified Resume delivers the kept timer under its original key and the Native handler advances its frame once");
    }

    private static async Task CheckRearmedTimer(bool sweep, Action<bool, string> check, bool preserveAfterSweep = false)
    {
        const long initial = 1_701_800_025_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now)
        {
            ParkedRetentionWindow = TimeSpan.FromMinutes(1), IngressDeduplicationWindow = TimeSpan.FromMilliseconds(100)
        };
        var scope = new Contract.BoundScope("native-rearm");
        var registry = new WorkflowRegistry().Add(Wf.Workflow("timer", Wf.Sequence("sequence",
            Wf.Delay("delay", 100), Wf.Set("finished-delay", "result", "continued"))));
        var model = new NativeExecutionModel(registry);
        var firstFault = false;
        var secondFault = false;
        var timerOffers = 0;
        var timerOfferedDuringDrain = false;
        Message? siblingTimer = null;
        var siblingRefused = false;
        Worker MakeWorker(string node) => new(node, 0, store, new ActorAdapter(store, new PublicWorkflow([model])), new WorkerOptions
        {
            Monotonic = () => now - initial,
            OnPreparedCheckpoint = (opening, _, _) =>
            {
                if (siblingTimer is not { } collision || opening.FoundationRecoveryTimers is null) return;
                siblingTimer = null;
                var prior = store.Instances[opening.Instance];
                var fence = store.Partitions[0];
                var sibling = new TurnCommit(opening.Instance, null, null, [], [], [collision], [], [], null);
                try
                {
                    store.CommitAsync(new(0, fence.Epoch, fence.Position, Defaults.Lease, false, [opening, sibling], [], []))
                        .GetAwaiter().GetResult();
                }
                catch (StoreException error) when (error.Failure == Failure.Definite)
                {
                    siblingRefused = store.Instances[opening.Instance] == prior && !store.Messages.ContainsKey(collision.InboxKey);
                }
            },
            OnTurn = (actor, inbox, _) =>
            {
                if (!firstFault && inbox.Any(message => message.Kind == NativeKinds.Timer))
                {
                    firstFault = true;
                    throw new InvalidOperationException("fault with due timer");
                }
                if (!secondFault && inbox.Any(message => message.Id == "fault-recovery"))
                {
                    secondFault = true;
                    throw new InvalidOperationException("fault after suppression");
                }
                if (inbox.Any(message => message.Kind == NativeKinds.Timer))
                {
                    timerOffers++;
                    timerOfferedDuringDrain |= store.Instances[actor.Id].ParkRecovery is not null;
                }
            }
        });
        var worker = MakeWorker("native-rearm");
        var ingress = new InMemoryIngress(store, new Requests(worker.Node), worker.Node, (_, kind) => kind == Contract.Kinds.Recover);
        var reads = new ActorList(store, 10);
        var runtime = new PublicRuntime(ingress, reads);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var reference = (await runtime.StartAsync(scope, "start", NativeExecutionModel.Start("timer", Modes.Balanced))).Actor!.Value;
        var target = new Contract.Target.Ref(reference);
        await Pump(owner);
        var timer = store.Messages.Values.Single(row => row.Message.Kind == NativeKinds.Timer);
        var envelope = timer.Message;
        var key = envelope.InboxKey;
        var seq = timer.Seq;
        now += 100;
        await Pump(owner);
        var parked = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!;
        var firstPark = firstFault && timer is { Done: true, KeptBy: ParkKeepReason.Consumed } && timerOffers == 0 && parked.Park is not null;
        check(firstPark, "a failed Native turn keeps its due timer consumed and leaves the saved frame waiting");
        if (!firstPark) return;

        var selection = (await reads.GetAsync(scope, reference))!.Selection!;
        await ingress.SendAsync(scope, target, "fault-recovery", "act");
        await Pump(owner);
        await ingress.SendAsync(scope, target, "suppress", Contract.Kinds.Recover, Json.Write(new Contract.OperatorCommand(selection)));
        await Pump(owner);
        var interrupted = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!;
        var suppressed = secondFault && interrupted.Park == parked.Park && interrupted.Row is
            { Status: InstanceStatus.Faulted, ParkRecovery: null, ParkTimerDispositions: not null } &&
            timer is { Done: true, KeptBy: null } && timerOffers == 0;
        check(suppressed, "Resume suppresses the consumed timer across a real re-park after clearing its kept row");
        if (!suppressed) return;

        // The finished marker is allowed to retain no payload. The invocation recipe must come from the model.
        timer.Message = timer.Message with { Payload = null };
        if (sweep)
        {
            now = timer.Message.KeepUntil!.Value + 1;
            await store.SweepAsync();
        }
        var recipe = new ParkTimerRecipe(envelope.Id, envelope.Kind, envelope.Payload, envelope.Due!.Value);
        check(store.Messages.ContainsKey(key) != sweep &&
            store.PlanFoundationTimers(interrupted.Row, ParkRecoveryMode.Resume, [recipe]) is { Rearms.Count: 0 } &&
            store.PlanFoundationTimers(interrupted.Row with { Creation = interrupted.Row.Creation + 1 }, ParkRecoveryMode.RetryAll, [recipe]) is null,
            $"the {(sweep ? "swept" : "payload-free")} timer fixture is ready and its handle names only the original creation");
        if (preserveAfterSweep)
        {
            siblingTimer = envelope;
            await ingress.SendAsync(scope, target, "resume-swept", Contract.Kinds.Recover,
                Json.Write(new Contract.OperatorCommand(selection, Contract.RecoverMode.Resume)));
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            worker.RunTurn(interrupted.Row.Id);
            await worker.FlushAsync();
            check(siblingRefused, "a sibling send cannot replace the absence of a swept suppressed timer during Resume");
            if (!siblingRefused) return;
            await owner.DisposeAsync();
            var successor = MakeWorker("native-suppressed-takeover");
            await using var fresh = (await new DurableHost([successor]).ActivateAsync(0))!;
            await Finish(fresh);
            var restored = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!;
            check(restored is { Park: null, Row: { ParkRecovery: null, Status: InstanceStatus.Running } } &&
                timerOffers == 0 && !store.Messages.ContainsKey(key),
                "Resume and takeover leave the suppressed timer unarmed and its Native frame waiting");
            return;
        }
        var admitted = await ingress.SendAsync(scope, target, "retry", Contract.Kinds.Recover,
            Json.Write(new Contract.OperatorCommand(selection, Contract.RecoverMode.RetryAll)));
        await worker.PollAsync();
        await worker.LoadAsync();
        await worker.FlushAsync();
        worker.RunTurn(interrupted.Row.Id);
        await worker.FlushAsync();
        var opened = admitted.Outcome == Contract.AdmissionOutcome.Accepted && store.Instances[interrupted.Row.Id].ParkRecovery is not null &&
            store.Messages.GetValueOrDefault(key) is { Done: false, ReleasedBy: null, KeptBy: null, Seen: 0, Trial: 0, Attempts: 0 } rebuilt &&
            rebuilt.Seq > seq && rebuilt.Message == envelope;
        check(opened, "RetryAll rebuilds the exact timer envelope with a fresh sequence in the fenced Open");
        if (!opened) return;

        if (sweep)
        {
            var epoch = worker.Fences.Single().Epoch;
            await owner.DisposeAsync();
            var successor = MakeWorker("native-rearm-takeover");
            await using var next = (await new DurableHost([successor]).ActivateAsync(0))!;
            check(successor.Fences.Single().Epoch > epoch, "the successor takes the partition after the re-arm and before the drain completes");
            await Finish(next);
        }
        else await Finish(owner);
        var checkpoint = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!;
        var state = Json.Read<NativeState>(checkpoint.Row.State);
        check(checkpoint is { Park: null, Row: { Status: InstanceStatus.Finished, ParkTimerDispositions: null } } &&
            !timerOfferedDuringDrain && timerOffers == 1 && state.Waits.Count == 0 &&
            state.Variables.GetValueOrDefault("result")?.GetValue<string>() == "continued" && !store.Messages.ContainsKey(key),
            "the re-armed timer waits for Complete and resumes the real Native frame exactly once");

        async Task Finish(IScopeActivation activation)
        {
            for (var step = 0; step < 4; step++)
            {
                now += 11;
                await Pump(activation);
            }
        }
    }

    internal static async Task CheckRecoveredSubscription(Action<bool, string> check)
    {
        const long initial = 1_701_800_035_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("native-wait-recovery");
        var registry = new WorkflowRegistry().Add(Wf.Workflow("waiting", Wf.Sequence("sequence",
            Wf.Event("wait", "approval"), Wf.Set("continue", "outcome", "matched"))));
        var model = new NativeExecutionModel(registry);
        var siblingRefused = false;
        var worker = new Worker("recover-subscription", 0, store, new ActorAdapter(store, new PublicWorkflow([model])),
            new WorkerOptions
            {
                Monotonic = () => now - initial,
                OnPreparedCheckpoint = (opening, _, _) =>
                {
                    if (opening.FoundationRecoverySubscriptions is not { Count: 1 } || opening.Subscribed is not [var wait]) return;
                    var row = store.Instances[opening.Instance];
                    var sibling = new TurnCommit(opening.Instance, null, null, [], [], [], [], [wait], null);
                    var fence = store.Partitions[0];
                    try
                    {
                        store.CommitAsync(new(0, fence.Epoch, fence.Position, Defaults.Lease, false, [opening, sibling], [], []))
                            .GetAwaiter().GetResult();
                    }
                    catch (StoreException error) when (error.Failure == Failure.Definite)
                    {
                        siblingRefused = store.Instances[opening.Instance] == row && store.Subscriptions.Count == 0;
                    }
                }
            });
        var ingress = new InMemoryIngress(store, new Requests(worker.Node), worker.Node, (_, kind) => kind == Contract.Kinds.Recover);
        var reads = new ActorList(store, 10);
        var runtime = new PublicRuntime(ingress, reads);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var reference = (await runtime.StartAsync(scope, "start", NativeExecutionModel.Start("waiting", Modes.Balanced))).Actor!.Value;
        var target = new Contract.Target.Ref(reference);
        var eventTarget = new Contract.Target.Event("event:approval");
        await Pump(owner);
        var initialWait = store.Subscriptions.Single();
        await ingress.SendAsync(scope, target, "fault", NativeKinds.Resume, "{");
        await Pump(owner);
        var parked = await reads.GetAsync(scope, reference);
        await ingress.SendAsync(scope, eventTarget, "while-parked", "event", "before",
            new Contract.AddOptions(KeepFor: TimeSpan.FromMinutes(1)));
        await Pump(owner);
        var removed = parked is { Header.Status: Contract.ActorStatus.Faulted, Selection: not null } &&
            store.Subscriptions.Count == 0 && store.FoundationEvents.Count == 1 &&
            store.Messages.Values.All(item => item.Message.Kind != Kinds.Event);
        check(removed, "Park removes the Native subscription and a publication during Park has no matching delivery");
        if (!removed) return;

        var admitted = await ingress.SendAsync(scope, target, "resume", Contract.Kinds.Recover,
            Json.Write(new Contract.OperatorCommand(parked!.Selection!)));
        for (var pass = 0; pass < 3; pass++)
        {
            now += 11;
            await Pump(owner);
        }
        var resumed = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!;
        check(siblingRefused, "the fenced Open refuses a sibling unsubscribe of the Native wait it restores");
        if (!siblingRefused) return;
        var maximum = Enumerable.Range(0, 3_511).Select(index => new ParkSubscriptionRecipe("approval-" + index, "wait", false)).ToArray();
        check(store.PlanFoundationSubscriptions(resumed.Row, scope.Tenant, maximum[..^1], bytes: 0) is { Count: 3510 } &&
            store.PlanFoundationSubscriptions(resumed.Row, scope.Tenant, maximum, bytes: 0) is null &&
            store.PlanFoundationSubscriptions(resumed.Row, scope.Tenant, maximum[..^1], bytes: 6145) is null,
            "the plan charges all restored subscriptions against the byte allowance shared with timers and jobs");
        var invalidWait = new string((char)0xD800, 1);
        check(store.PlanFoundationSubscriptions(resumed.Row, scope.Tenant, [new("approval", invalidWait, false)], bytes: 0) is null,
            "restore refuses a wait that ordinary subscription admission cannot round-trip");
        var keeping = maximum[..2_578].Select(wait => wait with { Keep = true }).ToArray();
        check(store.PlanFoundationSubscriptions(resumed.Row, scope.Tenant, keeping[..^1], bytes: 0) is { Count: 2577 } &&
            store.PlanFoundationSubscriptions(resumed.Row, scope.Tenant, keeping, bytes: 0) is null &&
            store.PlanFoundationSubscriptions(resumed.Row, scope.Tenant, keeping[..^1], bytes: 6657) is null,
            "keeping waits share one catch-up ticket charge and each pay the coverage allowance");
        var folded = new InMemoryStore(1, () => now, Flaw.None, StringComparer.OrdinalIgnoreCase);
        check(folded.PlanFoundationSubscriptions(resumed.Row, scope.Tenant,
                [new("topic", "wait", false), new("Topic", "WAIT", false)], bytes: 0) is null,
            "the restore plan refuses waits that collide under the store's ordinary subscription comparison");
        var restored = admitted.Outcome == Contract.AdmissionOutcome.Accepted && resumed is { Park: null, Row.Status: InstanceStatus.Running } &&
            store.Subscriptions.SetEquals([initialWait]) &&
            store.Subscriptions.Single() is { FoundationTenant: var tenant, Creation: var creation } && tenant == scope.Tenant && creation == resumed.Row.Creation &&
            store.Messages.Values.All(item => item.Message.Kind is not (Kinds.Event or Kinds.Meet));
        check(restored, "Resume restores the Native event wait and leaves earlier matched publications outside a non-keeping subscription");
        if (!restored) return;

        await ingress.SendAsync(scope, eventTarget, "after-resume", "event", "after");
        await Pump(owner);
        var final = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!;
        var state = Json.Read<NativeState>(final.Row.State);
        check(final.Row.Status == InstanceStatus.Finished && state.Variables.GetValueOrDefault("outcome")?.GetValue<string>() == "matched" &&
            state.Results.GetValueOrDefault("wait")?["Payload"]?.GetValue<string>() == "after",
            "a publication after Resume reaches the restored Native wait and completes its frame");
    }

    private static async Task CompletedJobRestore(Action<bool, string> check)
    {
        const long now = 1_701_800_000_000;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("acme");
        Contract.IIngress ingress = new InMemoryIngress(store, new Requests("native-job-restore"), "native-job-restore");
        Contract.IActorReads reads = new ActorList(store, 16);
        var runtime = new PublicRuntime(ingress, reads);
        var model = new NativeExecutionModel(new WorkflowRegistry().Add(Wf.Workflow("job",
            Wf.Parallel("parallel", null, Wf.Call("lookup", "pricing"), Wf.Set("first", "a", 1),
                Wf.Set("second", "b", 2), Wf.Event("wait", "signal")))));
        var reference = (await runtime.StartAsync(scope, "start", NativeExecutionModel.Start("job", Modes.Durable))).Actor!.Value;
        var instance = new Models.InstanceInfo(reference, scope.Tenant, model.Key, "job", null);
        var pendingWasRestorable = false;
        var queuedWasRestorable = false;
        var sawQueuedCompletion = false;
        var worker = new Worker("native-job-restore", 0, store, new ActorAdapter(store, new PublicWorkflow([model])),
            new WorkerOptions { Monotonic = () => 0 });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        for (var pass = 0; pass < 30; pass++)
        {
            await Pump(owner, 1);
            Observe();
        }
        var settled = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row;
        var applied = Json.Read<NativeState>(settled.State).Results.GetValueOrDefault("lookup")?.GetValue<string>();
        check(pendingWasRestorable && sawQueuedCompletion && !queuedWasRestorable && applied == "answer of pricing",
            $"a Durable job restores while pending and its committed agenda result cannot restart it " +
            $"(pending recipe {pendingWasRestorable}, queued result {sawQueuedCompletion}, queued recipe {queuedWasRestorable}, result {applied})");

        void Observe()
        {
            if (store.ReadFoundationActor(scope.Tenant, reference.Id).Actor is not { Row: var row }) return;
            var snapshot = Json.Read<NativeState>(row.State);
            var job = snapshot.Waits.SingleOrDefault(wait => wait.Value.Kind == "job").Key;
            if (job is null) return;
            var restored = model.RestoreWaits(instance, snapshot).Jobs.Any(recipe => recipe.Key == job);
            var hasTicket = row.Jobs is not null && DurableKeyJson.ReadKeys(row.Jobs).Contains(job);
            if (hasTicket) pendingWasRestorable |= restored;
            if (!hasTicket && snapshot.Agenda.Any(work => work is { Resume: true } && work.Wait == job))
            {
                sawQueuedCompletion = true;
                queuedWasRestorable |= restored;
            }
        }
    }

    public sealed class ReceiptReceiver : CompositeActivity
    {
        protected override void Execute(CompositeContext context)
        {
            context.RegisterInputInbox(2, 2, TimeSpan.FromMilliseconds(5));
            context.Schedule(context.Children.Single());
        }

        protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) => context.Complete();
    }

    private sealed class LocalReplies(Requests requests) : IReplyTransport
    {
        public void Send(string node, Reply reply) => requests.Receive(reply);
    }

    private static async Task Pump(IScopeActivation owner, int passes = 25)
    {
        for (var step = 0; step < passes; step++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
            await Task.Yield();
        }
    }
}
