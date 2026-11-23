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
        await DirectApprovalPreviewChecks.Run(check);
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
        await CheckRearmedTimer(sweep: false, check, explicitSelection: true);
        await CheckRearmedTimer(sweep: true, check, explicitSelection: true);
        await CheckRearmedTimer(sweep: false, check, explicitSelection: true, keepMarker: true);
        foreach (var variant in Enum.GetValues<RearmedWhenSelected>())
            await CheckRearmedTimer(sweep: true, check, explicitSelection: true, testRepeat: variant);
        await CheckRetryAllSuppressedTimers(check);
    }

    private static async Task CheckRetryAllSuppressedTimers(Action<bool, string> check)
    {
        const long initial = 1_701_800_026_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now)
        {
            ParkedRetentionWindow = TimeSpan.FromMinutes(1), IngressDeduplicationWindow = TimeSpan.FromMilliseconds(100)
        };
        var scope = new Contract.BoundScope("native-retry-all");
        Node Branch(string name) => Wf.Sequence("sequence-" + name, Wf.Delay("delay-" + name, 100), Wf.Set("set-" + name, name, true));
        var model = new NativeExecutionModel(new WorkflowRegistry().Add(Wf.Workflow("timers", Wf.Parallel("pair", null, Branch("one"), Branch("two")))));
        var faultAt = new HashSet<string>(["timers", "suppress-trip", "retry-all-trip"], StringComparer.Ordinal);
        var offeredTimers = new List<string>();
        var offeredTrips = new List<string>();
        var worker = new Worker("native-retry-all", 0, store, new ActorAdapter(store, new PublicWorkflow([model])), new WorkerOptions
        {
            Monotonic = () => now - initial,
            OnTurn = (_, inbox, _) =>
            {
                var timers = inbox.Where(message => message.Kind == NativeKinds.Timer).ToArray();
                if (timers.Length > 0 && faultAt.Remove("timers")) throw new InvalidOperationException("fail with both due timers");
                foreach (var trip in new[] { "suppress-trip", "retry-all-trip" })
                {
                    if (!inbox.Any(message => message.Id == trip)) continue;
                    if (faultAt.Remove(trip)) throw new InvalidOperationException("interrupt " + trip);
                    offeredTrips.Add(trip);
                }
                offeredTimers.AddRange(timers.Select(timer => timer.Id));
            }
        });
        var ingress = new InMemoryIngress(store, new Requests(worker.Node), worker.Node, (_, kind) => kind == Contract.Kinds.Recover);
        var reads = new ActorList(store, 10);
        var runtime = new PublicRuntime(ingress, reads);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var reference = (await runtime.StartAsync(scope, "start", NativeExecutionModel.Start("timers", Modes.Balanced))).Actor!.Value;
        var target = new Contract.Target.Ref(reference);
        await Pump(owner);
        var timers = store.Messages.Values.Where(row => row.Message.Kind == NativeKinds.Timer).OrderBy(row => row.Seq).ToArray();
        if (timers.Length != 2)
        {
            check(false, "the real Native workflow arms both waits before Park");
            return;
        }
        var cleared = timers[0];
        var retained = timers[1];
        now += 100;
        await Pump(owner);
        var parked = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!;
        if (parked.Park is null || timers.Any(row => row is not { Done: true, KeptBy: ParkKeepReason.Consumed }))
        {
            check(false, "the first failed turn keeps both timer inputs Consumed");
            return;
        }
        var selection = (await reads.GetAsync(scope, reference))!.Selection!;

        await ingress.SendAsync(scope, target, "suppress-trip", "act", new string('x', ParkRecoveryCredit.LimitBytes));
        await ingress.SendAsync(scope, target, "page-boundary", "act");
        await Pump(owner);
        // The large released row spends the first page's credit. The other arrival cuts its page before the second timer.
        retained.Seq = store.Messages.Values.Single(row => row.Message.Id == "page-boundary").Seq + 1;
        await Recover("suppress", Contract.RecoverMode.Resume);
        await Pump(owner);
        var suppressed = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!;
        var split = suppressed.Park == parked.Park && suppressed.Row is { Status: InstanceStatus.Faulted, ParkRecovery: null } &&
            cleared is { Done: true, KeptBy: null, Message.KeepUntil: not null } &&
            retained is { Done: true, KeptBy: ParkKeepReason.Consumed };
        check(split, "the interrupted Resume suppresses two real timers, with one before and one after its first page boundary");
        if (!split) return;

        now = cleared.Message.KeepUntil!.Value + 1;
        await store.SweepAsync();
        await ingress.SendAsync(scope, target, "retry-all-trip", "act");
        await Pump(owner);
        await Recover("retry-all", Contract.RecoverMode.RetryAll);
        await Step();
        var current = timers.Select(timer => store.Messages.GetValueOrDefault(timer.Message.InboxKey)).ToArray();
        var rebuilt = store.Instances[parked.Row.Id].ParkRecovery is { Cursor: null } &&
            current.Zip(timers).All(pair => pair.First is { Done: false, ReleasedBy: null, KeptBy: null } rebuiltTimer &&
                rebuiltTimer.Seq > pair.Second.Seq && rebuiltTimer.Message.InboxKey == pair.Second.Message.InboxKey);
        check(rebuilt, "RetryAll re-arms the still-kept timer and the swept timer in the same fenced Open");
        if (!rebuilt) return;

        for (var pass = 0; pass < 4 && store.Instances[parked.Row.Id].Status == InstanceStatus.Running; pass++)
        {
            now += 11;
            await Pump(owner);
        }
        var interrupted = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!;
        if (interrupted.Park != parked.Park || interrupted.Row is not { Status: InstanceStatus.Faulted, ParkRecovery: null } ||
            offeredTimers.Count != 0)
        {
            check(false, "a second interruption leaves both re-armed timers in the same Park before either reaches its frame");
            return;
        }

        await Recover("retry-all-again", Contract.RecoverMode.RetryAll);
        await Step();
        check(store.Instances[parked.Row.Id].ParkRecovery is { Cursor: null } &&
            current.All(timer => ReferenceEquals(timer, store.Messages[timer!.Message.InboxKey])),
            "a second RetryAll opens without replacing or re-arming either timer again");
        for (var pass = 0; pass < 4; pass++)
        {
            now += 11;
            await Pump(owner);
        }
        var completed = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!;
        var state = Json.Read<NativeState>(completed.Row.State);
        check(completed is { Park: null, Row.Status: InstanceStatus.Finished } && state.Waits.Count == 0 &&
            state.Variables.GetValueOrDefault("one")?.GetValue<bool>() == true &&
            state.Variables.GetValueOrDefault("two")?.GetValue<bool>() == true &&
            offeredTimers.Order(StringComparer.Ordinal).SequenceEqual(timers.Select(timer => timer.Message.Id).Order(StringComparer.Ordinal)) &&
            offeredTrips.SequenceEqual(["suppress-trip", "retry-all-trip"]),
            "both Native frames resume exactly once and each interrupted Consumed input is replayed by RetryAll");

        async Task Recover(string operation, Contract.RecoverMode mode) =>
            await ingress.SendAsync(scope, target, operation, Contract.Kinds.Recover, Json.Write(new Contract.OperatorCommand(selection, mode)));

        async Task Step()
        {
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            worker.RunTurn(parked.Row.Id);
            await worker.FlushAsync();
        }
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

    private enum RearmedWhenSelected { Pending, Arrived, Consumed, Stale, StaleResume }

    private static async Task CheckRearmedTimer(bool sweep, Action<bool, string> check, bool explicitSelection = false,
        RearmedWhenSelected? testRepeat = null, bool keepMarker = false, bool preserveAfterSweep = false)
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
        var rearmedFault = false;
        var failRestoredTimer = false;
        var retryArrivalOffers = 0;
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
                if (inbox.Any(message => message.Id is "interrupt-rearm" or "retry-y" or "stale-trip"))
                {
                    if (!rearmedFault)
                    {
                        rearmedFault = true;
                        throw new InvalidOperationException("fault after re-arm");
                    }
                    retryArrivalOffers++;
                }
                if (inbox.Any(message => message.Kind == NativeKinds.Timer))
                {
                    if (failRestoredTimer)
                    {
                        failRestoredTimer = false;
                        throw new InvalidOperationException("fault with a restored timer");
                    }
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
        var keptHandle = store.SealFoundationKeptHandle(scope.Tenant, reference.Id, key)!;
        await ingress.SendAsync(scope, target, "fault-recovery", "act",
            keepMarker ? new string('x', ParkRecoveryCredit.LimitBytes) : null);
        if (keepMarker) await ingress.SendAsync(scope, target, "page-boundary", "act");
        await Pump(owner);
        if (keepMarker)
        {
            // Put the timer after a second released candidate, beyond the first page's credit.
            timer.Seq = store.Messages.Values.Single(row => row.Message.Id == "page-boundary").Seq + 1;
            seq = timer.Seq;
        }
        await ingress.SendAsync(scope, target, "suppress", Contract.Kinds.Recover, Json.Write(new Contract.OperatorCommand(selection)));
        await Pump(owner);
        var interrupted = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!;
        var suppressed = secondFault && interrupted.Park == parked.Park && interrupted.Row is
            { Status: InstanceStatus.Faulted, ParkRecovery: null, ParkTimerDispositions: not null } &&
            timer.Done && timer.KeptBy == (keepMarker ? ParkKeepReason.Consumed : null) && timerOffers == 0;
        check(suppressed, $"Resume suppresses the consumed timer across a real re-park {(keepMarker ? "before" : "after")} clearing its kept row");
        if (!suppressed) return;

        // The finished marker is allowed to retain no payload. The invocation recipe must come from the model.
        if (!keepMarker) timer.Message = timer.Message with { Payload = null };
        if (sweep)
        {
            now = timer.Message.KeepUntil!.Value + 1;
            await store.SweepAsync();
        }
        var recipe = new ParkTimerRecipe(envelope.Id, envelope.Kind, envelope.Payload, envelope.Due!.Value);
        check(store.Messages.ContainsKey(key) != sweep &&
            store.PlanFoundationTimers(interrupted.Row, ParkRecoveryMode.Resume, [recipe]) is { Rearms.Count: 0 } &&
            store.PlanFoundationTimers(interrupted.Row with { Creation = interrupted.Row.Creation + 1 }, ParkRecoveryMode.RetryAll, [recipe]) is null,
            $"the {(sweep ? "swept" : keepMarker ? "still-kept" : "payload-free")} timer fixture is ready and its handle names only the original creation");
        if (testRepeat is not null)
        {
            await ingress.SendAsync(scope, target, "interrupt-rearm", "act");
            await Pump(owner);
        }
        var retainedHandle = store.SealFoundationKeptHandle(scope.Tenant, reference.Id, key);
        var handles = explicitSelection ? new[] { sweep ? retainedHandle! : keptHandle } : null;
        var mode = explicitSelection ? Contract.RecoverMode.Retry : Contract.RecoverMode.RetryAll;
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
            Json.Write(new Contract.OperatorCommand(selection, mode, handles)));
        await Step(worker);
        var opened = admitted.Outcome == Contract.AdmissionOutcome.Accepted && store.Instances[interrupted.Row.Id].ParkRecovery is not null &&
            store.Messages.GetValueOrDefault(key) is { Done: false, ReleasedBy: null, KeptBy: null, Seen: 0, Trial: 0, Attempts: 0 } rebuilt &&
            rebuilt.Seq > seq && rebuilt.Message == envelope;
        check(opened, $"{mode} rebuilds the exact {(sweep ? "swept" : "present")} timer envelope with a fresh sequence in the fenced Open");
        if (!opened) return;

        var rebuiltTimer = store.Messages[key];
        if (!sweep && explicitSelection)
        {
            await store.SweepAsync();
            check(ReferenceEquals(rebuiltTimer, store.Messages.GetValueOrDefault(key)) &&
                store.Messages.Values.All(row => row.KeptBy != ParkKeepReason.Selected),
                "the sweep after Open preserves the re-armed timer without a Selected marking");
        }
        if (testRepeat is { } variant)
        {
            var staleTimer = variant is RearmedWhenSelected.Stale or RearmedWhenSelected.StaleResume;
            var selectInputs = variant != RearmedWhenSelected.StaleResume;
            await WaitForRepark();
            var samePark = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!;
            var retryArrival = store.Messages.Values.Single(row => row.Message.Id == "interrupt-rearm");
            if (samePark.Park != parked.Park || samePark.Row.Status != InstanceStatus.Faulted ||
                retryArrival.KeptBy != ParkKeepReason.Consumed || !rearmedFault || rebuiltTimer.Done)
            {
                check(false, $"R1 re-parks before its freshly re-armed timer reaches the Native frame ({variant})");
                return;
            }

            if (variant != RearmedWhenSelected.Pending)
            {
                await Step(worker);
                if (rebuiltTimer is not { Done: true, KeptBy: ParkKeepReason.Arrived })
                {
                    check(false, $"the re-armed timer is kept Arrived before the next {variant} recovery");
                    return;
                }
            }

            if (variant == RearmedWhenSelected.Consumed)
            {
                failRestoredTimer = true;
                await ingress.SendAsync(scope, target, "fail-restored-timer", Contract.Kinds.Recover,
                    Json.Write(new Contract.OperatorCommand(selection)));
                await Step(worker);
                await WaitForRepark();
                if (store.Instances[interrupted.Row.Id].Status != InstanceStatus.Faulted ||
                    rebuiltTimer is not { Done: true, KeptBy: ParkKeepReason.Consumed })
                {
                    check(false, "a failed Native turn consumes the restored timer under the same Park");
                    return;
                }

                await ingress.SendAsync(scope, target, "retry-y", "act");
                retryArrival = store.Messages.Values.Single(row => row.Message.Id == "retry-y");
                retryArrival.Attempts = 3;
                await Step(worker);
                if (retryArrival.KeptBy != ParkKeepReason.Consumed)
                {
                    check(false, "the Worker keeps the exhausted companion input Consumed under the same Park");
                    return;
                }
            }

            if (staleTimer)
            {
                await ingress.SendAsync(scope, target, "stale-trip", "act");
                await Step(worker);
                var trigger = store.Messages.Values.Single(row => row.Message.Id == "stale-trip");
                trigger.Seq = rebuiltTimer.Seq - 1;
                await ingress.SendAsync(scope, target, "release-before-interruption", Contract.Kinds.Recover,
                    Json.Write(new Contract.OperatorCommand(selection)));
                await Step(worker);
                await Step(worker);
                var released = store.Instances[interrupted.Row.Id];
                var tagged = rebuiltTimer.ReleasedBy is { } release && rebuiltTimer is { Done: false, KeptBy: null } &&
                    release.Generation == released.ParkResolutionGeneration && trigger.ReleasedBy == release;
                check(tagged, "RA-1 the actual bounded page first releases the timer and an earlier input");
                if (!tagged) return;

                trigger.Attempts = 3; // Inject the persisted attempt count before the owner offers the cohort.
                await WaitForRepark();
                var parkedAgain = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!;
                var interruptedBeforeTimer = parkedAgain.Park == parked.Park &&
                    parkedAgain.Row is { Status: InstanceStatus.Faulted, ParkRecovery: null } &&
                    trigger is { Done: true, KeptBy: ParkKeepReason.Consumed } &&
                    rebuiltTimer is { Done: false, KeptBy: null, ReleasedBy: not null } && timerOffers == 0;
                check(interruptedBeforeTimer, "RA-1 an earlier exhausted cohort input re-parks while the re-armed timer still has its old tag");
                if (!interruptedBeforeTimer) return;
                retryArrival = trigger;
            }

            var expectedAlreadyRearmed = selectInputs && variant != RearmedWhenSelected.Consumed ? 1 : 0;
            var operation = selectInputs
                ? new Contract.OperatorCommand(selection, Contract.RecoverMode.Retry,
                    [keptHandle, store.SealFoundationKeptHandle(scope.Tenant, reference.Id, retryArrival.Message.InboxKey)!])
                : new Contract.OperatorCommand(selection, Contract.RecoverMode.Resume);
            var repeat = await ingress.SendAsync(scope, target, "retry-again", Contract.Kinds.Recover, Json.Write(operation));
            if (staleTimer || variant == RearmedWhenSelected.Pending)
                store.Messages.Values.Single(row => row.Message.Id == "retry-again").Seq = rebuiltTimer.Seq - 1;
            await Step(worker);
            var repeated = repeat.Outcome == Contract.AdmissionOutcome.Accepted &&
                store.Instances[interrupted.Row.Id].ParkRecovery is { Cursor: null } drain &&
                drain.AlreadyRearmed == expectedAlreadyRearmed &&
                ReferenceEquals(rebuiltTimer, store.Messages[key]) &&
                retryArrival.KeptBy == (selectInputs ? ParkKeepReason.Selected : ParkKeepReason.Consumed) &&
                (variant switch
                {
                    RearmedWhenSelected.Pending => rebuiltTimer is { Done: false, KeptBy: null, ReleasedBy: null },
                    RearmedWhenSelected.Arrived => rebuiltTimer is { Done: true, KeptBy: ParkKeepReason.Arrived },
                    RearmedWhenSelected.Stale or RearmedWhenSelected.StaleResume => rebuiltTimer is { Done: false, KeptBy: null, ReleasedBy: { } released } &&
                        released.Generation < store.Instances[interrupted.Row.Id].ParkResolutionGeneration,
                    _ => rebuiltTimer is { Done: true, KeptBy: ParkKeepReason.Selected }
                });
            check(repeated, $"R2 opens {operation.Mode} with T {variant} and keeps its original envelope ({repeat.Outcome}; companion {retryArrival.KeptBy})");
            if (!repeated) return;

            InMemoryStore.MessageRow? late = null;
            if (variant == RearmedWhenSelected.Pending)
            {
                await ingress.SendAsync(scope, target, "unreleased-arrival", "act");
                late = store.Messages.Values.Single(row => row.Message.Id == "unreleased-arrival");
            }
            else if (staleTimer)
            {
                await ingress.SendAsync(scope, target, "late-native-cancel", NativeKinds.Cancel);
                late = store.Messages.Values.Single(row => row.Message.Id == "late-native-cancel");
                late.Seq = rebuiltTimer.Seq - 2;
            }
            await Step(worker);
            var gate = store.Instances[interrupted.Row.Id].ParkRecovery?.Gate;
            check(variant switch
                {
                    RearmedWhenSelected.Pending => gate == retryArrival.Message.InboxKey &&
                        rebuiltTimer is { Done: false, Seen: > 0, ReleasedBy: null } && late is { Done: false, Seen: > 0, ReleasedBy: null },
                    RearmedWhenSelected.Arrived => gate == key,
                    RearmedWhenSelected.Stale or RearmedWhenSelected.StaleResume => gate == key && rebuiltTimer is { Done: false, ReleasedBy: { } released } &&
                        released.Generation == store.Instances[interrupted.Row.Id].ParkResolutionGeneration &&
                        late is { Done: false, ReleasedBy: null },
                    _ => rebuiltTimer is { Done: false, ReleasedBy: not null, KeptBy: null }
                }, $"the first R2 page waits only on a released input with T {variant}");
        }

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
            timerOfferedDuringDrain == (testRepeat is RearmedWhenSelected.Arrived or RearmedWhenSelected.Consumed or RearmedWhenSelected.Stale or RearmedWhenSelected.StaleResume) &&
            timerOffers == 1 && state.Waits.Count == 0 &&
            state.Variables.GetValueOrDefault("result")?.GetValue<string>() == "continued" &&
            store.Messages.GetValueOrDefault(key) is null or { Done: true, KeptBy: null },
            $"{mode} offers the re-armed timer in the expected phase and resumes the real Native frame exactly once");
        if (testRepeat is { } finalVariant)
            check(retryArrivalOffers == (finalVariant == RearmedWhenSelected.StaleResume ? 0 : 1) &&
                store.Journal.Values.Any(entry => entry.Instance == checkpoint.Row.Id &&
                entry.Entries.Contains("\"Operation\":\"retry-again\"", StringComparison.Ordinal) &&
                entry.Entries.Contains("\"AlreadyRearmed\":" + (finalVariant is RearmedWhenSelected.Consumed or RearmedWhenSelected.StaleResume ? 0 : 1), StringComparison.Ordinal)),
                $"R2 records the expected replay and already re-armed count with T {finalVariant}");

        async Task Step(Worker current)
        {
            await current.PollAsync();
            await current.LoadAsync();
            await current.FlushAsync();
            current.RunTurn(interrupted.Row.Id);
            await current.FlushAsync();
        }

        async Task WaitForRepark()
        {
            for (var pass = 0; pass < 15 && store.Instances[interrupted.Row.Id].Status != InstanceStatus.Faulted; pass++)
            {
                if (pass % 5 == 4) now += 11;
                await Step(worker);
            }
        }

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
