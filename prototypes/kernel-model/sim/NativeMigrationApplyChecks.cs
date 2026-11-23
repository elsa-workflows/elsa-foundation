using System.Text;
using System.Text.Json;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Kernel.WorkflowActor;
using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;

namespace Kernel.Sim;

internal static class NativeMigrationApplyChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
        foreach (var loseAfterLanding in new[] { false, true })
            await VerifyAsync(mode, loseAfterLanding, (ok, label) =>
                check(ok, $"{mode}, {(loseAfterLanding ? "after landing" : "before landing")}: {label}"));
        await VerifyVersionHoldAsync(check);
        await VerifyHeaderLimitAsync(check);
    }

    private static async Task VerifyVersionHoldAsync(Action<bool, string> check)
    {
        const long initial = 1_701_800_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("acme");
        Contract.IIngress edge = new InMemoryIngress(store, new Requests("version-hold"), "version-hold",
            (_, kind) => kind == Contract.Kinds.Migrate);
        Contract.IActorReads reads = new ActorList(store, 32);
        var runtime = new PublicRuntime(edge, reads);
        var native = new NativeExecutionModel(new WorkflowRegistry()
            .Add(Definition("source", "ready", "source tail")).Add(Definition("target", "ready", "target tail")));
        var current = new PublicWorkflow([native, new PlanSink()]);
        var actor = (await runtime.StartAsync(scope, "version-start", NativeExecutionModel.Start("source"))).Actor!.Value;
        var plan = (await runtime.StartAsync(scope, "hold-plan", new(PlanSink.Key, "hold-plan"))).Actor!.Value;
        var stalePlan = (await runtime.StartAsync(scope, "hold-stale-plan", new(PlanSink.Key, "hold-stale-plan"))).Actor!.Value;
        await using var first = await ActivateAsync("version-first", current);
        await PumpAsync(first);
        var state = store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!.Row.State;

        now += 20_000;
        var oldModel = new OldNative(native.StateVersion - 1);
        await using var older = await ActivateAsync("version-older", new PublicWorkflow([oldModel, new PlanSink()]));
        var target = new Contract.Target.Ref(actor);
        await edge.SendAsync(scope, target, "wait-for-stale", Contract.Kinds.Migrate,
            Json.Write(new MigrationRequest("different source", "target", stalePlan)));
        await edge.SendAsync(scope, target, "wait-for-version", Contract.Kinds.Migrate,
            Json.Write(new MigrationRequest("source", "target", plan)));
        await PumpAsync(older);
        var held = await reads.InboxAsync(scope, actor);
        check(held.Items is [{ Id: "wait-for-stale", State: Contract.DeliveryState.Held },
                { Id: "wait-for-version", State: Contract.DeliveryState.Pending }] &&
            store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!.Row.State == state &&
            (await reads.InboxAsync(scope, plan)).Items.Count == 0 &&
            (await reads.InboxAsync(scope, stalePlan)).Items.Count == 0 &&
            (await reads.TailAsync(scope, actor)).Items.All(row => row.Entry != "\"migrate\""),
            "an older owner holds the stale-source migration ahead of the next command, without a result or marker");

        now += 20_000;
        await using var latest = await ActivateAsync("version-latest", current);
        // A fresh input can release a durable Hold. It does not change the Native wait.
        await edge.SendAsync(scope, new Contract.Target.Ref(actor), "model-available", "wake");
        await PumpAsync(latest);
        var answers = Json.Read<PlanAnswer[]>(store.ReadFoundationActor(scope.Tenant, plan.Id).Actor!.Row.State);
        var staleAnswers = Json.Read<PlanAnswer[]>(store.ReadFoundationActor(scope.Tenant, stalePlan.Id).Actor!.Row.State);
        check((await reads.GetAsync(scope, actor))?.Header.Labels.GetValueOrDefault("definition") == "target" &&
            answers is [{ Actor: var sender, Result.Outcome: MigrationPlanOutcome.Migrated }] && sender == actor &&
            staleAnswers is [{ Actor: var staleSender, Result.Outcome: MigrationPlanOutcome.NotAtSource }] && staleSender == actor,
            "a newer owner replays both original commands and classifies their source definitions");

        async Task<IScopeActivation> ActivateAsync(string node, PublicWorkflow model)
        {
            var worker = new Worker(node, 0, store, new ActorAdapter(store, model), new WorkerOptions { Monotonic = () => now - initial });
            return await new DurableHost([worker]).ActivateAsync(0)
                ?? throw new InvalidOperationException("The version witness could not acquire its owner.");
        }
    }

    private static async Task VerifyAsync(string mode, bool loseAfterLanding, Action<bool, string> check)
    {
        const long initial = 1_701_800_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("acme");
        var otherScope = new Contract.BoundScope("other");
        Contract.IIngress edge = new InMemoryIngress(store, new Requests("migration-apply"), "migration-apply",
            (_, kind) => kind is Contract.Kinds.Migrate or Contract.Kinds.Suspend or Contract.Kinds.Resume);
        Contract.IActorReads reads = new ActorList(store, 64);
        var runtime = new PublicRuntime(edge, reads);
        var registry = new WorkflowRegistry()
            .Add(Definition("source", "ready", "source tail"))
            .Add(Definition("target", "ready", "target tail"))
            .Add(Definition("bad-wait", "other", "unexpected"));
        var type = new PublicWorkflow([new NativeExecutionModel(registry), new PlanSink()]);
        var loseMigration = false;
        var lost = 0;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (!loseMigration || landed != loseAfterLanding ||
                !batch.Turns.Any(turn => turn.Sent.Any(send => send.Kind == MigrationPlanResult.Kind)))
                return;
            lost++;
            throw new StoreException(Failure.Unknown, "the migration's first commit answer was lost");
        });
        var history = loseAfterLanding
            ? mode == Modes.Balanced ? Contract.History.State : Contract.History.Minimal
            : Contract.History.Full;
        var actor = await StartAsync("workflow", NativeExecutionModel.Start("source", mode) with { History = history });
        var target = new Contract.Target.Ref(actor);
        var refuse = await StartPlanAsync("refuse");
        var apply = await StartPlanAsync("apply");
        var wrong = await StartPlanAsync("wrong");
        var ended = await StartPlanAsync("ended");
        var foreign = (await runtime.StartAsync(otherScope, "other-plan", new(PlanSink.Key, "other-plan"))).Actor!.Value;
        await using var owner = await ActivateAsync("migration-first");
        await PumpAsync(owner);
        var source = Checkpoint(actor);
        var waits = Json.Read<NativeState>(source).Waits;
        check(waits.Values.Single().Key == "event:ready" && await BoundDefinition() == "source",
            "the real model awaits the original event under the source definition");

        await MigrateAsync("cross-tenant", "source", "target", foreign);
        await PumpAsync(owner);
        check(await BoundDefinition() == "source" && Checkpoint(actor) == source &&
            Json.Read<PlanAnswer[]>(Checkpoint(foreign)).Length == 0,
            "a plan reference outside the tenant changes neither the checkpoint nor the other plan");

        await MigrateAsync("incompatible", "source", "bad-wait", refuse);
        await PumpAsync(owner);
        check(await BoundDefinition() == "source" && Checkpoint(actor) == source &&
            Answers(refuse) is [{ Actor: var first, Result: { Outcome: MigrationPlanOutcome.Refused, Finding: "AuthoredNode" } }]
            && first == actor, "an incompatible waiting definition yields one value-free refusal from this exact life");
        await MigrateAsync("incompatible-after-retention", "source", "bad-wait", refuse);
        await PumpAsync(owner);
        check(Answers(refuse).Length == 1, "a fresh operation ID for the last answered plan adds no second outcome");

        await edge.SendAsync(scope, target, "suspend", Contract.Kinds.Suspend);
        await PumpAsync(owner);
        loseMigration = true;
        await MigrateAsync("apply", "source", "target", apply);
        await PumpAsync(owner, until: () => lost > 0);
        var pending = await reads.InboxAsync(scope, actor);
        if (loseAfterLanding)
        {
            var queued = await reads.InboxAsync(scope, apply);
            check(lost == 1 && await BoundDefinition() == "target" && Checkpoint(actor) == source &&
                pending.Items.All(item => item.Id != "apply") &&
                queued.Items is [{ Kind: MigrationPlanResult.Kind, Sender: var answeringLife }] && answeringLife == actor,
                "a landed commit with a lost answer keeps the new definition and one pending plan result");
        }
        else
        {
            check(lost == 1 && await BoundDefinition() == "source" && Checkpoint(actor) == source &&
                Answers(apply).Length == 0 && pending.Items.Any(item => item.Id == "apply"),
                "an unlanded commit leaves the old checkpoint and original command with no plan outcome");
        }
        loseMigration = false;
        now += 20_000;
        await using var successor = await ActivateAsync("migration-next");
        await PumpAsync(successor);
        check(await BoundDefinition() == "target" && Checkpoint(actor) == source &&
            Answers(apply) is [{ Actor: var migrated, Result.Outcome: MigrationPlanOutcome.Migrated }] && migrated == actor,
            "a cold successor commits the new definition, same waiting state and one migrated outcome");

        await MigrateAsync("apply-after-retention", "source", "target", apply);
        await PumpAsync(successor);
        check(Answers(apply).Length == 1, "replaying the last applied plan with a fresh input ID adds no outcome");
        await MigrateAsync("wrong-from", "source", "target", wrong);
        await PumpAsync(successor);
        check(Answers(wrong) is [{ Actor: var atDifferentDefinition, Result.Outcome: MigrationPlanOutcome.NotAtSource }] &&
            atDifferentDefinition == actor && await BoundDefinition() == "target", "a different stale plan answers not at source");

        await edge.SendAsync(scope, new Contract.Target.Event("event:ready"), "ready", "published", "yes");
        await PumpAsync(successor);
        check(Checkpoint(actor) == source && (await reads.GetAsync(scope, actor))?.Header.Status == Contract.ActorStatus.Running,
            "the migrated workflow remains suspended and does not consume its original event yet");
        await edge.SendAsync(scope, target, "resume", Contract.Kinds.Resume);
        await PumpAsync(successor);
        var finished = Json.Read<NativeState>(Checkpoint(actor));
        check((await reads.GetAsync(scope, actor))?.Header.Status == Contract.ActorStatus.Finished &&
            finished.Variables["output"]?.GetValue<string>() == "target tail" &&
            finished.Results["wait"]?["Payload"]?.GetValue<string>() == "yes",
            "public resume consumes the existing wait once and executes the replacement tail");

        await MigrateAsync("ended", "target", "source", ended);
        await PumpAsync(successor);
        check(Answers(ended) is [{ Actor: var final, Result: { Outcome: MigrationPlanOutcome.Refused, Finding: "Ended" } }] &&
            final == actor && await BoundDefinition() == "target", "an ended workflow refuses to change its definition");

        var journal = (await reads.TailAsync(scope, actor)).Items.Select(row => Json.Read<JsonElement>(row.Entry)).ToArray();
        var migratedEntries = journal.Count(item => item.ValueKind == JsonValueKind.String && item.GetString() == "migrate");
        var activityEntries = journal.Count(item => item.ValueKind == JsonValueKind.String &&
            item.GetString()?.EndsWith(":wait", StringComparison.Ordinal) == true);
        var definitionEntries = journal.Count(item => item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("Target", out var definition) && definition.ValueKind == JsonValueKind.String &&
            definition.GetString() == "target");
        var expectedDetail = history == Contract.History.Full ? 1 : 0;
        check(migratedEntries == 1 && activityEntries == expectedDetail && definitionEntries == expectedDetail,
            $"the {history} journal records one successful lifecycle boundary and the applicable activity detail");

        async Task<Contract.ActorRef> StartAsync(string id, Models.WorkflowStart start) =>
            (await runtime.StartAsync(scope, id, start)).Actor ?? throw new InvalidOperationException("The witness could not start.");
        Task<Contract.ActorRef> StartPlanAsync(string id) => StartAsync(id, new(PlanSink.Key, id));
        Task<Contract.Admission> MigrateAsync(string id, string from, string to, Contract.ActorRef plan) =>
            edge.SendAsync(scope, target, id, Contract.Kinds.Migrate, Json.Write(new MigrationRequest(from, to, plan))).AsTask();
        string Checkpoint(Contract.ActorRef reference) =>
            store.ReadFoundationActor(reference == foreign ? otherScope.Tenant : scope.Tenant, reference.Id).Actor?.Row.State
                ?? throw new InvalidOperationException("The witness checkpoint is missing.");
        PlanAnswer[] Answers(Contract.ActorRef plan) => Json.Read<PlanAnswer[]>(Checkpoint(plan));
        async Task<string?> BoundDefinition() => (await reads.GetAsync(scope, actor))?.Header.Labels.GetValueOrDefault("definition");
        async Task<IScopeActivation> ActivateAsync(string node)
        {
            var worker = new Worker(node, 0, wire, new ActorAdapter(store, type), new WorkerOptions { Monotonic = () => now - initial });
            return await new DurableHost([worker]).ActivateAsync(0)
                ?? throw new InvalidOperationException("The migration witness could not acquire its owner.");
        }
    }

    private static async Task VerifyHeaderLimitAsync(Action<bool, string> check)
    {
        const int headerLimit = 16 * 1_024;
        var store = new InMemoryStore(1, () => 1_701_800_000_000);
        var scope = new Contract.BoundScope("acme");
        Contract.IIngress edge = new InMemoryIngress(store, new Requests("header-limit"), "header-limit",
            (_, kind) => kind == Contract.Kinds.Migrate);
        Contract.IActorReads reads = new ActorList(store, 32);
        var runtime = new PublicRuntime(edge, reads);
        var type = new PublicWorkflow([new PlanSink(), new CappedHeaderModel()]);
        var actor = (await runtime.StartAsync(scope, "capped-start", new(CappedHeaderModel.Key, "source"))).Actor!.Value;
        var plan = (await runtime.StartAsync(scope, "capped-plan", new(PlanSink.Key, "plan"))).Actor!.Value;
        var worker = new Worker("header-limit", 0, store, new ActorAdapter(store, type), new WorkerOptions { Monotonic = () => 0 });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        await PumpAsync(owner);
        var original = (await reads.GetAsync(scope, actor))!.Header.TypeData;
        // SubStatus changes from the four characters null to a two-quote JSON string.
        var fullness = new string('x', headerLimit - Encoding.UTF8.GetByteCount(original) + 2);
        var target = new Contract.Target.Ref(actor);
        await edge.SendAsync(scope, target, "fill", "set-substatus", fullness);
        await PumpAsync(owner);
        var full = (await reads.GetAsync(scope, actor))!.Header.TypeData;
        var command = Json.Write(new MigrationRequest("source", "longer-target", plan));
        await edge.SendAsync(scope, target, "capped", Contract.Kinds.Migrate, command);
        await PumpAsync(owner);
        await edge.SendAsync(scope, target, "capped-replay", Contract.Kinds.Migrate, command);
        await PumpAsync(owner);
        var after = (await reads.GetAsync(scope, actor))!.Header;
        var answers = Json.Read<PlanAnswer[]>(store.ReadFoundationActor(scope.Tenant, plan.Id).Actor!.Row.State);
        check(Encoding.UTF8.GetByteCount(full) == headerLimit && after.TypeData == full &&
            after.Labels.GetValueOrDefault("definition") == "source" &&
            answers is [{ Actor: var sender, Result: { Outcome: MigrationPlanOutcome.Refused, Finding: "HeaderLimit" } }] &&
            sender == actor, "a full existing header refuses an expanding target and remembers the answer on replay");
    }

    private static Workflow Definition(string id, string eventName, string tail) => Wf.Workflow(id,
        Wf.Sequence("sequence", Wf.Event("wait", eventName), Wf.Set("tail", "output", tail)), ("output", null));

    private static async Task PumpAsync(IScopeActivation owner, Func<bool>? until = null)
    {
        for (var pass = 0; pass < 30 && until?.Invoke() != true; pass++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
            await Task.Yield();
        }
    }

    private sealed record PlanAnswer(Contract.ActorRef Actor, MigrationPlanResult Result);

    private sealed class CappedHeaderModel : Models.IExecutionModel, Models.ISimpleModelMigration
    {
        internal const string Key = "capped-header";
        string Models.IExecutionModel.Key => Key;
        public Type StateType => typeof(int);
        public int StateVersion => 1;
        public object CreateState(Models.WorkflowStart start) => 0;
        public object UpgradeState(string state, int fromVersion) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Models.InstanceInfo instance, object state) => new([], [], []);
        public long TerminalGrowth(Models.InstanceInfo instance, object state) => 0;
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public string? ValidateMigration(Models.InstanceInfo source, object state, string target) => null;
        public ValueTask TurnAsync(Models.ITurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox.Where(message => message.Kind == "set-substatus"))
                turn.SetStatus(Contract.ActorStatus.Running, message.Payload);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OldNative(int version) : Models.IExecutionModel
    {
        public string Key => NativeExecutionModel.ModelKey;
        public Type StateType => typeof(NativeState);
        public int StateVersion => version;
        public object CreateState(Models.WorkflowStart start) => throw new NotSupportedException();
        public object UpgradeState(string state, int fromVersion) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Models.InstanceInfo instance, object state) => throw new NotSupportedException();
        public long TerminalGrowth(Models.InstanceInfo instance, object state) => throw new NotSupportedException();
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask TurnAsync(Models.ITurn turn, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    // A real model turn receives the durable message and checks the foundation-issued sender.
    private sealed class PlanSink : Models.IExecutionModel
    {
        internal const string Key = "plan-sink";
        string Models.IExecutionModel.Key => Key;
        public Type StateType => typeof(List<PlanAnswer>);
        public int StateVersion => 1;
        public object CreateState(Models.WorkflowStart start) => new List<PlanAnswer>();
        public object UpgradeState(string state, int fromVersion) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Models.InstanceInfo instance, object state) => new([], [], []);
        public long TerminalGrowth(Models.InstanceInfo instance, object state) => 0;
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask TurnAsync(Models.ITurn turn, CancellationToken cancellationToken)
        {
            var results = (List<PlanAnswer>)turn.State;
            foreach (var message in turn.Inbox)
                if (message is { Kind: MigrationPlanResult.Kind, Sender: { } sender, Payload: { } payload })
                    results.Add(new(sender, Json.Read<MigrationPlanResult>(payload)));
            return ValueTask.CompletedTask;
        }
    }
}
