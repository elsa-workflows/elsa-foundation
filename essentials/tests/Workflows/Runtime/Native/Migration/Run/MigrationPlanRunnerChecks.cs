using System.Security.Cryptography;
using System.Text;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Kernel.WorkflowActor;
using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Elsa.Workflows.Runtime.Native.Migration;

internal static class MigrationPlanRunnerChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        const long initial = 1_701_800_000_000;
        const string source = MigrationPlanModel.ModelKey;
        const string target = "target";
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = ContractScopes.Bind("acme");
        var other = ContractScopes.Bind("other");
        var key = RandomNumberGenerator.GetBytes(32);
        var authority = new MigrationPlanAuthority(key);
        var foundation = new InMemoryActorFixture(store, "plan-runner", 1,
            (_, kind) => kind is Contract.Kinds.Migrate or Contract.Kinds.Terminate or Contract.Kinds.Delete);
        var edge = foundation.Ingress;
        var observed = new SelectionReceipts(edge);
        var reads = foundation.Reads;
        var runtime = new PublicRuntime(edge, reads);
        var registry = new WorkflowRegistry().Add(Definition(source, "old")).Add(Definition(target, "new"))
            .Add(Definition("absent", "empty")).Add(Definition("selected-for-delete", "gone"))
            .Add(Definition("large-source", "old")).Add(Definition("large-target", "new", "different-event"));
        var model = new MigrationPlanModel(authority, (executionModel, from, to) =>
            executionModel is NativeExecutionModel.ModelKey or OtherExecutionModel.ModelKey &&
            registry.TryGet(from, out _) && registry.TryGet(to, out _));
        var actorType = new PublicWorkflow([new NativeExecutionModel(registry), model, new OtherExecutionModel()]);
        var allowed = true;
        var creatorsReady = false;
        var service = NewOperator();
        var expires = initial + 600_000;
        check(await Denied(async () => { await service.StartAsync(other, "wrong-tenant", new(source, target, expires, NativeExecutionModel.ModelKey)); }),
            "the operator service checks the tenant permission before admitting a plan");
        var versionRefused = false;
        try { await StartPlan(service, "mixed-creators", source); }
        catch (InvalidOperationException) { versionRefused = true; }
        check(versionRefused && observed.Starts == 0,
            "a mixed-version owner set prevents model-bound creation before any public start admission");
        creatorsReady = true;
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

        var customPlan = (await service.StartAsync(scope, "custom-model", new(source, target, expires, OtherExecutionModel.ModelKey))).Actor!.Value;
        var customDriver = service.Open(scope, customPlan);
        await Pump(successor);
        for (var step = 0; step < 10 && (await reads.GetAsync(scope, customPlan))?.Header.Status == Contract.ActorStatus.Running; step++)
        {
            await customDriver.AdvanceAsync();
            await Pump(successor);
        }
        check((await customDriver.ReadOutcomesAsync()) is { Closed: true, Results.Count: 1 } custom &&
            custom.Results.GetValueOrDefault(collidingModel) == MigrationPlanOutcome.Migrated &&
            (await reads.GetAsync(scope, collidingModel))?.Header.Labels.GetValueOrDefault("version") == "0",
            "automatic selection includes a registered model with stored state version zero");

        var closing = await StartPlan(service, "close-after-birth", "absent");
        var closeDriver = service.Open(scope, closing);
        var premature = await closeDriver.CloseAsync();
        await Pump(successor);
        var admittedClose = await closeDriver.CloseAsync();
        await Pump(successor);
        check(!premature && admittedClose && (await closeDriver.ReadOutcomesAsync()) is { Closed: true },
            "an operator close reports no admission before birth and succeeds after a retry");

        var empty = await StartPlan(service, "empty", "absent");
        var emptyDriver = service.Open(scope, empty);
        await Pump(successor);
        var emptySelected = await emptyDriver.AdvanceAsync();
        await Pump(successor);
        var behind = new MigrationPlanOutcomes(reads, scope, empty);
        var caughtUp = behind.Completion() is not null;
        while (await behind.ProjectNextAsync()) { }
        check(!caughtUp && behind.Completion() is { Batch: 1 },
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
        var closedOnRequest = await waiting.CloseAsync();
        await Pump(successor);
        check(closedOnRequest && (await waiting.ReadOutcomesAsync()) is { Closed: true } closed && closed.NoAnswer.SequenceEqual([missing]),
            "an operator close reports the exact missing life with no answer");

        var wideReads = foundation.OpenReads(256);
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
        await CheckLegacyTerminalGrowth(check);

        MigrationPlanOperator NewOperator(Contract.IActorReads? sourceReads = null) =>
            new(observed, sourceReads ?? reads, model, new MigrationPlanAuthority(key),
                selected => allowed && selected.Tenant == scope.Tenant, () => creatorsReady);
        async Task<Contract.ActorRef> StartPlan(MigrationPlanOperator operatorHost, string id, string from, string to = target) =>
            (await operatorHost.StartAsync(scope, id, new(from, to, expires, NativeExecutionModel.ModelKey))).Actor!.Value;
        async Task<Contract.ActorRef> StartActor(string id, string definition, string mode) =>
            (await runtime.StartAsync(scope, id, NativeExecutionModel.Start(definition, mode))).Actor!.Value;
        async Task<IScopeActivation> Open(string node) => (await new DurableHost(
            [new Worker(node, 0, store, foundation.Bind(actorType), new WorkerOptions { Monotonic = () => now - initial })])
            .ActivateAsync(0))!;
        bool FinishedUnderTarget(Contract.ActorRef actor) =>
            foundation.ReadCheckpoint(scope.Tenant, actor.Id) is { Status: InstanceStatus.Finished } row &&
            Json.Read<NativeState>(row.State).Variables["output"]?.GetValue<string>() == "new";
    }

    private static async Task CheckLegacyTerminalGrowth(Action<bool, string> check)
    {
        const long initial = 1_701_900_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = ContractScopes.Bind("legacy-plan");
        var foundation = new InMemoryActorFixture(store, "legacy-plan", 8,
            (_, kind) => kind == Contract.Kinds.Terminate);
        var ingress = foundation.Ingress;
        var reads = foundation.Reads;
        var runtime = new PublicRuntime(ingress, reads);
        var key = MigrationPlanModel.ModelKey;
        var deadline = initial + 600_000;
        var request = new V1Request("source", "target", deadline);
        var actor = (await runtime.StartAsync(scope, "legacy-pending", new(key, key, Json.Write(request)))).Actor!.Value;
        await using (var predecessor = await Open("legacy", new PublicWorkflow([new V1PlanFixture()])))
            await Pump(predecessor);

        var before = foundation.ReadCheckpoint(scope.Tenant, actor.Id)!.State;
        var header = (await reads.GetAsync(scope, actor))!.Header;
        var latest = new PublicWorkflow([new MigrationPlanModel(new(RandomNumberGenerator.GetBytes(32)), (_, _, _) => true)]);
        var reserved = latest.TerminalGrowth(header, before);
        now += 20_000;
        await using var successor = await Open("latest", latest);
        await ingress.SendAsync(scope, new Contract.Target.Ref(actor), "terminate", Contract.Kinds.Terminate);
        await Pump(successor);
        var after = foundation.ReadCheckpoint(scope.Tenant, actor.Id)!.State;
        var ended = (await reads.GetAsync(scope, actor))!.Header;
        check(header.Labels.GetValueOrDefault("version") == "1" && ended.Labels.GetValueOrDefault("version") == "2" &&
            ended.Status == Contract.ActorStatus.Cancelled && Json.Read<V1PendingPlan>(before).Pending == 1 &&
            !after.Contains("\"Model\"", StringComparison.Ordinal) &&
            Encoding.UTF8.GetByteCount(after) - Encoding.UTF8.GetByteCount(before) == reserved && reserved == 2,
            "terminating a persisted version-1 pending checkpoint stays within its two-byte reservation");

        async Task<IScopeActivation> Open(string node, PublicWorkflow type) => (await new DurableHost(
            [new Worker(node, 0, store, foundation.Bind(type), new WorkerOptions { Monotonic = () => now - initial })])
            .ActivateAsync(0))!;
    }

    private sealed record V1Request(string From, string To, long Deadline);
    private sealed record V1PendingPlan(V1Request Request, long Number = 1, string? DeadlineTimer = null, long? Pending = 1,
        Contract.PageToken? Cursor = null, bool AllSelected = true, bool Closed = false, long Migrated = 0, long Refused = 0, long NotAtSource = 0);

    private sealed class V1PlanFixture : Models.IExecutionModel
    {
        public string Key => MigrationPlanModel.ModelKey;
        public Type StateType => typeof(V1PendingPlan);
        public int StateVersion => 1;
        public object CreateState(Models.WorkflowStart start) => new V1PendingPlan(Json.Read<V1Request>(start.Input!));
        public object UpgradeState(string state, int fromVersion) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Models.InstanceInfo instance, object state) => new([], [], []);
        public long TerminalGrowth(Models.InstanceInfo instance, object state) => 2;
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask TurnAsync(Models.ITurn turn, CancellationToken cancellationToken)
        {
            var plan = (V1PendingPlan)turn.State;
            turn.State = plan with { DeadlineTimer = turn.Send(turn.Instance.Ref, MigrationPlanControl.Deadline, dueAt: plan.Request.Deadline) };
            return ValueTask.CompletedTask;
        }
    }

    private static Workflow Definition(string name, string output, string eventName = "approval") => Wf.Workflow(name,
        Wf.Sequence("sequence", Wf.Event("wait", eventName), Wf.Set("tail", "output", output)), ("output", null));

    private sealed class OtherExecutionModel : Models.IExecutionModel, Models.ISimpleModelMigration
    {
        internal const string ModelKey = "other-model";
        public string Key => ModelKey;
        public Type StateType => typeof(int);
        public int StateVersion => 0;
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
        internal int Starts { get; private set; }
        internal List<(string Id, Contract.AdmissionOutcome Outcome)> Selections { get; } = [];

        public async ValueTask<Contract.Admission> SendAsync(Contract.BoundScope scope, Contract.Target target,
            string id, string kind, string? payload = null, Contract.AddOptions? options = null, CancellationToken cancellationToken = default)
        {
            var receipt = await inner.SendAsync(scope, target, id, kind, payload, options, cancellationToken);
            if (kind == Contract.Kinds.Start) Starts++;
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
