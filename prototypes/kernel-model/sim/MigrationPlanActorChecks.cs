using System.Security.Cryptography;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Kernel.WorkflowActor;
using Contract = Kernel.Foundation.Contracts;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

internal static class MigrationPlanActorChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        const long initial = 1_701_800_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("acme");
        var key = RandomNumberGenerator.GetBytes(32);
        var authority = new MigrationPlanAuthority(key);
        Contract.IIngress ingress = new InMemoryIngress(store, new Requests("real-plan"), "real-plan",
            (_, kind) => kind == Contract.Kinds.Migrate);
        Contract.IActorReads reads = new ActorList(store, 1);
        var runtime = new PublicRuntime(ingress, reads);
        var registry = new WorkflowRegistry().Add(Definition("source", "old")).Add(Definition("target", "new"));
        var plans = new MigrationPlanModel(authority, (from, to) => registry.TryGet(from, out _) && registry.TryGet(to, out _));
        var actorType = new PublicWorkflow([new NativeExecutionModel(registry), plans]);
        var deadline = now + 600_000;
        var invalid = false;
        try { _ = plans.Start(new("missing", "target", deadline)); }
        catch (ArgumentException) { invalid = true; }
        check(invalid, "an unavailable source definition is refused before plan admission");

        var plan = await StartPlan("main-plan", deadline);
        await StartWorkflow("first-workflow");
        await StartWorkflow("second-workflow");
        await using var owner = await Open("first-plan-owner");
        await Pump(owner);
        var query = new Contract.ActorQuery(PublicWorkflow.ActorType, Contract.ActorStatus.Running,
            new Dictionary<string, string> { ["definition"] = "source" });
        var firstPage = await runtime.ListAsync(scope, query);
        var first = firstPage.Items.Single().Header.Ref;
        var cursor = firstPage.Next ?? throw new InvalidOperationException("The source selection should have another page.");
        var firstControl = new MigrationPlanControl(1, [first], After: cursor);
        var checkpoint = NewReader(plan);
        var target = new Contract.Target.Ref(plan);
        await ingress.SendAsync(scope, target, "first-page", MigrationPlanControl.Select, Json.Write(firstControl));
        var copied = authority.Protect(scope.Tenant, first, MigrationPlanControl.Select, "wrong-plan", firstControl);
        await ingress.SendAsync(scope, target, "wrong-plan", MigrationPlanControl.Select, copied);
        var tooMany = Enumerable.Range(0, MigrationPlanModel.BatchLimit + 1)
            .Select(index => new Contract.ActorRef($"selected-{index}")).ToArray();
        await checkpoint.SendAsync(MigrationPlanControl.Select, "oversize", firstControl with { Actors = tooMany });
        await Pump(owner);
        check(await checkpoint.ReadPendingAsync(scope, plan, default) is null,
            "raw, cross-plan and oversized controls leave the batch slot empty");

        await checkpoint.SendAsync(MigrationPlanControl.Select, "first-page", firstControl);
        await Pump(owner);
        var secondPage = await runtime.ListAsync(scope, query, cursor);
        var second = secondPage.Items.Single().Header.Ref;
        var finalControl = new MigrationPlanControl(2, [second], Before: cursor, AllSelected: true);
        await checkpoint.SendAsync(MigrationPlanControl.Select, "early-page", finalControl);
        await checkpoint.SendAsync(MigrationPlanControl.Complete, "early-close", new(1));
        await Pump(owner);
        var pending = await checkpoint.ReadPendingAsync(scope, plan, default);
        check(pending is { Number: 1, Actors: [var selected] } && selected == first &&
            (await reads.GetAsync(scope, plan))?.Header.Status == Contract.ActorStatus.Running,
            "the real plan keeps one pending batch and rejects a premature page or completion");

        now += 20_000;
        await using var successor = await Open("second-plan-owner");
        var nextReader = NewReader(plan, new MigrationPlanAuthority(key));
        var dispatcher = new MigrationPlanDispatch(ingress, nextReader);
        var dispatched = await dispatcher.DispatchAsync(scope, plan);
        var replay = await dispatcher.DispatchAsync(scope, plan);
        check(dispatched is { Accepted: 1 } && replay is { Duplicate: 1 } &&
            await nextReader.ReadPendingAsync(scope, plan, default) is { Number: 1 },
            "after takeover a fresh runner reconstructs the same batch and deduplicates both admissions before the acknowledgment commits");
        await Pump(successor);
        check(await nextReader.ReadPendingAsync(scope, plan, default) is null, "committed acknowledgment clears the exact pending batch");

        await nextReader.SendAsync(MigrationPlanControl.Select, "incorrect-cursor", finalControl with { Before = null });
        await Pump(successor);
        check(await nextReader.ReadPendingAsync(scope, plan, default) is null, "a stale page cursor cannot advance the plan");
        await nextReader.SendAsync(MigrationPlanControl.Select, "next-page", finalControl);
        await Pump(successor);
        var last = await dispatcher.DispatchAsync(scope, plan);
        await Pump(successor);
        check(last is { Accepted: 1 } && await nextReader.ReadPendingAsync(scope, plan, default) is null,
            "the follow-on page dispatches its own exact actor and clears after acknowledgment");
        await ingress.SendAsync(scope, target, "unsigned-answer", MigrationPlanResult.Kind,
            Json.Write(new MigrationPlanResult(MigrationPlanOutcome.Refused)));
        await Pump(successor);
        var outcomes = new MigrationPlanOutcomes(reads, scope, plan);
        await Refresh(outcomes);
        var opened = outcomes.Read();
        check(opened is { Closed: false, AllAnswered: true } && opened.Results.Count == 2 &&
            opened.Results[first] == MigrationPlanOutcome.Migrated && opened.Results[second] == MigrationPlanOutcome.Migrated,
            "the journal projects both actual Native outcomes and ignores an outside result with no actor sender");
        await nextReader.SendAsync(MigrationPlanControl.Complete, "complete-plan", new(2));
        await Pump(successor);
        await Refresh(outcomes);
        var saved = store.ReadFoundationActor(scope.Tenant, plan.Id).Actor!.Row.State;
        check(outcomes.Read().Closed && (await reads.GetAsync(scope, plan))?.Header.Status == Contract.ActorStatus.Finished &&
            !saved.Contains(first.Id, StringComparison.Ordinal) && !saved.Contains(second.Id, StringComparison.Ordinal),
            "completion closes the plan while the model state retains counters and the cursor without per-actor outcomes");

        await ingress.SendAsync(scope, new Contract.Target.Event("event:approval"), "approve", "publish", "yes");
        await Pump(successor);
        check(FinishedUnderTarget(first) && FinishedUnderTarget(second), "both migrated Native waits resume under the target definition");

        var expiry = now + 200;
        var expiring = await StartPlan("expiring-plan", expiry);
        var waiting = await StartWorkflow("late-workflow");
        await Pump(successor);
        var expiryReader = NewReader(expiring);
        await expiryReader.SendAsync(MigrationPlanControl.Select, "late-selection", new(1, [waiting], AllSelected: true));
        await Pump(successor);
        await ingress.SendAsync(scope, new Contract.Target.Ref(waiting), "delayed-migration", Contract.Kinds.Migrate,
            Json.Write(new MigrationRequest("source", "target", expiring)), new(DueAt: expiry + 100));
        var lateOutcomes = new MigrationPlanOutcomes(reads, scope, expiring);
        now = expiry;
        await Pump(successor);
        await Refresh(lateOutcomes);
        check(lateOutcomes.Read() is { Closed: true } expired && expired.NoAnswer.SequenceEqual([waiting]) &&
            await expiryReader.ReadPendingAsync(scope, expiring, default) is null,
            "the plan's own scheduled deadline closes the undispatched page and preserves its no-answer view");
        now = expiry + 100;
        await Pump(successor);
        await Refresh(lateOutcomes);
        check(lateOutcomes.Read() is { Closed: true } answered && answered.NoAnswer.Count() == 0 &&
            answered.Results.GetValueOrDefault(waiting) == MigrationPlanOutcome.Migrated,
            "a late Native result updates the same closed plan from the exact selected life");

        async Task<Contract.ActorRef> StartPlan(string id, long expires) =>
            (await runtime.StartAsync(scope, id, plans.Start(new("source", "target", expires)))).Actor!.Value;
        async Task<Contract.ActorRef> StartWorkflow(string id) =>
            (await runtime.StartAsync(scope, id, NativeExecutionModel.Start("source"))).Actor!.Value;
        async Task<IScopeActivation> Open(string node) => (await new DurableHost(
            [new Worker(node, 0, store, new ActorAdapter(store, actorType), new WorkerOptions { Monotonic = () => now - initial })])
            .ActivateAsync(0))!;
        JournalMigrationDispatchState NewReader(Contract.ActorRef reference, MigrationPlanAuthority? authenticator = null) =>
            new(ingress, reads, authenticator ?? authority, scope, reference);
        bool FinishedUnderTarget(Contract.ActorRef reference) =>
            store.ReadFoundationActor(scope.Tenant, reference.Id).Actor is { Row: { Status: InstanceStatus.Finished } row } &&
            Json.Read<NativeState>(row.State).Variables["output"]?.GetValue<string>() == "new";
    }

    private static Workflow Definition(string name, string output) => Wf.Workflow(name,
        Wf.Sequence("sequence", Wf.Event("wait", "approval"), Wf.Set("tail", "output", output)), ("output", null));

    private static async Task Pump(IScopeActivation owner)
    {
        for (var step = 0; step < 30; step++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
            await Task.Yield();
        }
    }

    private static async Task Refresh(MigrationPlanOutcomes projection)
    {
        while (await projection.ProjectNextAsync()) { }
    }
}
