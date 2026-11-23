using System.Security.Cryptography;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Kernel.WorkflowActor;
using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

internal static class MigrationPlanRunnerChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        const long initial = 1_701_800_000_000;
        const string source = MigrationPlanModel.ModelKey;
        const string target = "target";
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("acme");
        var other = new Contract.BoundScope("other");
        var key = RandomNumberGenerator.GetBytes(32);
        var authority = new MigrationPlanAuthority(key);
        Contract.IIngress edge = new InMemoryIngress(store, new Requests("plan-runner"), "plan-runner",
            (_, kind) => kind is Contract.Kinds.Migrate or Contract.Kinds.Terminate or Contract.Kinds.Delete);
        var observed = new SelectionReceipts(edge);
        Contract.IActorReads reads = new ActorList(store, 1);
        var runtime = new PublicRuntime(edge, reads);
        var registry = new WorkflowRegistry().Add(Definition(source, "old")).Add(Definition(target, "new"))
            .Add(Definition("absent", "empty")).Add(Definition("selected-for-delete", "gone"))
            .Add(Definition("large-source", "old")).Add(Definition("large-target", "new", "different-event"));
        var model = new MigrationPlanModel(authority, (executionModel, from, to) =>
            executionModel == NativeExecutionModel.ModelKey && registry.TryGet(from, out _) && registry.TryGet(to, out _));
        var actorType = new PublicWorkflow([new NativeExecutionModel(registry), model, new OtherExecutionModel()]);
        var allowed = true;
        var service = NewOperator();
        var expires = initial + 600_000;
        check(await Denied(async () => { await service.StartAsync(other, "wrong-tenant", new(source, target, expires, NativeExecutionModel.ModelKey)); }),
            "the operator service checks the tenant permission before admitting a plan");
        var first = await StartActor("balanced", source, Modes.Balanced);
        var second = await StartActor("durable", source, Modes.Durable);
        var unrelated = await StartActor("excluded", "selected-for-delete", Modes.Balanced);
        var collidingModel = (await runtime.StartAsync(scope, "colliding-model", new(OtherExecutionModel.ModelKey, source))).Actor!.Value;
        var plan = await StartPlan(service, "automated", source);
        var driver = service.Open(scope, plan);
        await using var owner = await Open("initial-plan-owner");
        await Pump(owner);
        allowed = false;
        check(await Denied(async () => { await driver.AdvanceAsync(); }),
            "an opened runner still checks current operator permission before selecting");
        allowed = true;

        observed.DropNext = true;
        var lost = false;
        try { await driver.AdvanceAsync(); }
        catch (IOException) { lost = true; }
        now += 20_000;
        await using var successor = await Open("next-plan-owner");
        service = NewOperator();
        driver = service.Open(scope, plan);
        var recovered = await driver.AdvanceAsync();
        check(lost && recovered == MigrationPlanStep.SelectionAdmitted && observed.Selections is
            [(var firstId, Contract.AdmissionOutcome.Accepted), (var reusedId, Contract.AdmissionOutcome.Duplicate)] &&
            firstId == reusedId, "a cold runner retries the identical selection ID after an admission answer is lost");
        await Pump(successor);

        var completedBeforeCommit = false;
        for (var step = 0; step < 24; step++)
        {
            var advanced = await driver.AdvanceAsync();
            if (advanced == MigrationPlanStep.CompletionAdmitted)
                completedBeforeCommit = (await reads.GetAsync(scope, plan))?.Header.Status == Contract.ActorStatus.Running;
            await Pump(successor);
            if ((await reads.GetAsync(scope, plan))?.Header.Status == Contract.ActorStatus.Finished) break;
        }
        var results = await driver.ReadOutcomesAsync();
        check(completedBeforeCommit && results is { Closed: true } && results.Results.Count == 2 &&
            results.Results.GetValueOrDefault(first) == MigrationPlanOutcome.Migrated &&
            results.Results.GetValueOrDefault(second) == MigrationPlanOutcome.Migrated &&
            (await reads.GetAsync(scope, collidingModel))?.Header.Labels.GetValueOrDefault("definition") == source &&
            (await reads.GetAsync(scope, unrelated))?.Header.Labels.GetValueOrDefault("definition") == "selected-for-delete",
            "automatic selection excludes both the plan actor and the other execution model sharing the definition name");
        check(await driver.AdvanceAsync() == MigrationPlanStep.Idle, "a committed closed plan admits no more selections or work");
        await edge.SendAsync(scope, new Contract.Target.Ref(collidingModel), "exact-model-recheck", Contract.Kinds.Migrate,
            Json.Write(new MigrationRequest(source, target, plan, NativeExecutionModel.ModelKey)));
        await Pump(successor);
        check((await reads.GetAsync(scope, collidingModel))?.Header.Labels.GetValueOrDefault("definition") == source,
            "the actor rejects a bound migration for another model even when targeting its exact reference");

        await edge.SendAsync(scope, new Contract.Target.Event("event:approval"), "approval", "publish", "go");
        await Pump(successor);
        check(FinishedUnderTarget(first) && FinishedUnderTarget(second),
            "both original Native waits resume under the target definition after automatic migration");

        var empty = await StartPlan(service, "empty", "absent");
        var emptyDriver = service.Open(scope, empty);
        await Pump(successor);
        var emptySelected = await emptyDriver.AdvanceAsync();
        await Pump(successor);
        var behind = new MigrationPlanOutcomes(reads, scope, empty);
        var caughtUp = behind.CanComplete(1);
        while (await behind.ProjectNextAsync()) { }
        check(!caughtUp && behind.CanComplete(1),
            "an empty result projection cannot complete until it reads the plan's actual final selection");
        var emptyCompleted = await emptyDriver.AdvanceAsync();
        await Pump(successor);
        check(emptySelected == MigrationPlanStep.SelectionAdmitted && emptyCompleted == MigrationPlanStep.CompletionAdmitted &&
            (await emptyDriver.ReadOutcomesAsync()) is { Closed: true, Results.Count: 0 },
            "a committed empty final page can complete a plan without manufacturing any outcome");

        var missing = await StartActor("removed-life", "selected-for-delete", Modes.Balanced);
        var unfinished = await StartPlan(service, "unfinished", "selected-for-delete");
        var waiting = service.Open(scope, unfinished);
        await Pump(successor);
        // The earlier unrelated actor finished on the approval. Only the new life is selected.
        await waiting.AdvanceAsync();
        await Pump(successor);
        var exact = new Contract.Target.Ref(missing);
        await edge.SendAsync(scope, exact, "terminate", Contract.Kinds.Terminate);
        await Pump(successor);
        var ended = (await reads.GetAsync(scope, missing))?.Selection
            ?? throw new InvalidOperationException("The witness needs the terminal selection.");
        await edge.SendAsync(scope, exact, "delete", Contract.Kinds.Delete, Json.Write(new Contract.OperatorCommand(ended)));
        await Pump(successor);
        var dispatched = await waiting.AdvanceAsync();
        await Pump(successor);
        var awaiting = await waiting.AdvanceAsync();
        check(dispatched == MigrationPlanStep.DispatchAdmitted && awaiting == MigrationPlanStep.WaitingForOutcomes &&
            (await waiting.ReadOutcomesAsync()) is { Closed: false } open && open.Pending.SequenceEqual([missing]),
            "a deleted selected life leaves the plan open after its last dispatch has been acknowledged");
        await waiting.CloseAsync();
        await Pump(successor);
        check((await waiting.ReadOutcomesAsync()) is { Closed: true } closed && closed.NoAnswer.SequenceEqual([missing]),
            "an operator close reports the exact missing life with no answer");

        var wideReads = new ActorList(store, 256);
        var wideService = NewOperator(wideReads);
        var positions = new List<Contract.ActorRef>();
        for (var index = 0; index <= MigrationPlanModel.BatchLimit; index++)
            positions.Add(await StartActor(FormattableString.Invariant($"large-wait-{index}"), "large-source", Modes.Balanced));
        await Pump(successor);
        var widePlan = await StartPlan(wideService, "wide-plan", "large-source", "large-target");
        var wide = wideService.Open(scope, widePlan);
        await Pump(successor);
        for (var step = 0; step < 10; step++)
        {
            await wide.AdvanceAsync();
            await Pump(successor);
            if (step == 1) wide = NewOperator(wideReads).Open(scope, widePlan);
            if ((await reads.GetAsync(scope, widePlan))?.Header.Status == Contract.ActorStatus.Finished) break;
        }
        var wideResult = await wide.ReadOutcomesAsync();
        var counts = new List<int>();
        Contract.PageToken? after = null;
        while (true)
        {
            var page = await wideReads.TailAsync(scope, widePlan, after);
            if (page.Items.Count == 0) break;
            counts.AddRange(page.Items.Select(row => MigrationPlanJournal.Read(row.Entry))
                .OfType<MigrationPlanJournal.Selected>().Select(batch => batch.Actors.Length));
            after = page.Next;
        }
        check(wideResult is { Closed: true } && wideResult.Results.Count == positions.Count &&
            positions.All(actor => wideResult.Results.GetValueOrDefault(actor) == MigrationPlanOutcome.Refused) &&
            counts.SequenceEqual([MigrationPlanModel.BatchLimit, 1]),
            "a 129-actor provider page dispatches as 128 plus one even when all refused actors remain at source");

        MigrationPlanOperator NewOperator(Contract.IActorReads? sourceReads = null) =>
            new(observed, sourceReads ?? reads, model, new MigrationPlanAuthority(key), selected => allowed && selected.Tenant == scope.Tenant);
        async Task<Contract.ActorRef> StartPlan(MigrationPlanOperator operatorHost, string id, string from, string to = target) =>
            (await operatorHost.StartAsync(scope, id, new(from, to, expires, NativeExecutionModel.ModelKey))).Actor!.Value;
        async Task<Contract.ActorRef> StartActor(string id, string definition, string mode) =>
            (await runtime.StartAsync(scope, id, NativeExecutionModel.Start(definition, mode))).Actor!.Value;
        async Task<IScopeActivation> Open(string node) => (await new DurableHost(
            [new Worker(node, 0, store, new ActorAdapter(store, actorType), new WorkerOptions { Monotonic = () => now - initial })])
            .ActivateAsync(0))!;
        bool FinishedUnderTarget(Contract.ActorRef actor) =>
            store.ReadFoundationActor(scope.Tenant, actor.Id).Actor is { Row: { Status: InstanceStatus.Finished } row } &&
            Json.Read<NativeState>(row.State).Variables["output"]?.GetValue<string>() == "new";
    }

    private static Workflow Definition(string name, string output, string eventName = "approval") => Wf.Workflow(name,
        Wf.Sequence("sequence", Wf.Event("wait", eventName), Wf.Set("tail", "output", output)), ("output", null));

    private sealed class OtherExecutionModel : Models.IExecutionModel, Models.ISimpleModelMigration
    {
        internal const string ModelKey = "other-model";
        public string Key => ModelKey;
        public Type StateType => typeof(int);
        public int StateVersion => 1;
        public object CreateState(Models.WorkflowStart start) => 0;
        public object UpgradeState(string state, int fromVersion) => throw new NotSupportedException();
        public string? ValidateMigration(Models.InstanceInfo source, object state, string target) => null;
        public ValueTask TurnAsync(Models.ITurn turn, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public Contract.RestorePlan RestoreWaits(Models.InstanceInfo instance, object state) => new([], [], []);
        public long TerminalGrowth(Models.InstanceInfo instance, object state) => 0;
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static async Task<bool> Denied(Func<Task> action)
    {
        try { await action(); return false; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private static async Task Pump(IScopeActivation owner)
    {
        for (var pass = 0; pass < 30; pass++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
            await Task.Yield();
        }
    }

    private sealed class SelectionReceipts(Contract.IIngress inner) : Contract.IIngress
    {
        internal bool DropNext { get; set; }
        internal List<(string Id, Contract.AdmissionOutcome Outcome)> Selections { get; } = [];

        public async ValueTask<Contract.Admission> SendAsync(Contract.BoundScope scope, Contract.Target target,
            string id, string kind, string? payload = null, Contract.AddOptions? options = null, CancellationToken cancellationToken = default)
        {
            var receipt = await inner.SendAsync(scope, target, id, kind, payload, options, cancellationToken);
            if (kind != MigrationPlanControl.Select) return receipt;
            Selections.Add((id, receipt.Outcome));
            if (!DropNext) return receipt;
            DropNext = false;
            throw new IOException("The selected batch admission answer was lost.");
        }

        public ValueTask<Contract.Reply> RequestAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind,
            string? payload, Contract.AddOptions? add, Contract.RequestOptions request, CancellationToken cancellationToken = default) =>
            inner.RequestAsync(scope, target, id, kind, payload, add, request, cancellationToken);

        public ValueTask SetStartAsync(Contract.BoundScope scope, string key, string wait, Contract.StartSpec? spec,
            CancellationToken cancellationToken = default) => inner.SetStartAsync(scope, key, wait, spec, cancellationToken);
    }
}
