using Kernel.Foundation.InMemory;
using Kernel.Native;
using System.Text.Json.Nodes;
using Contract = Kernel.Foundation.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;

namespace Kernel.Sim;

internal static class StructuredMigrationChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
        {
            void Check(bool ok, string message) => check(ok, $"{mode}: {message}");
            await CheckSequence(mode, Check);
            await CheckContainers(mode, Check);
            await CheckLoopAndGraph(mode, Check);
            await CheckStoredReferences(mode, Check);
            await CheckFinished(mode, Check);
        }
        await CheckQueuedWork(check);
    }

    private static async Task CheckLoopAndGraph(string mode, Action<bool, string> check)
    {
        var loop = Wf.Workflow("loop-source", Wf.ForEach("loop", Binding.Literal(new[] { 1, 2 }), Wait()));
        await using (var actor = await WaitingActor.StartAsync(loop, mode))
        {
            var state = Json.Read<NativeState>(actor.Checkpoint());
            check(state.Waits.Values.Single().Key == "event:ready" &&
                StructuredMigration.Validate(loop, Wf.Workflow("loop-same",
                    Wf.ForEach("loop", Binding.Literal(new[] { 1, 2 }), Wait())), state) is null,
                "ForEach can keep its current iteration and unchanged body slot");
            check(StructuredMigration.Validate(loop, Wf.Workflow("loop-change-items",
                Wf.ForEach("loop", Binding.Literal(new[] { 2, 1 }), Wait())), state) == StructuredMigrationFinding.AuthoredNode,
                "ForEach rejects a changed ordered items binding");
            check(StructuredMigration.Validate(loop, Wf.Workflow("loop-change-body",
                Wf.ForEach("loop", Binding.Literal(new[] { 1, 2 }), Wf.Event("replacement", "ready"))), state)
                == StructuredMigrationFinding.LiveNode, "ForEach refuses a replacement for the live body ID");
        }

        var graph = Wf.Workflow("graph", Wf.Flowchart("flowchart", [Wait()], []));
        await using (var actor = await WaitingActor.StartAsync(graph, mode))
        {
            var state = Json.Read<NativeState>(actor.Checkpoint());
            check(state.Waits.Values.Single().Key == "event:ready" &&
                StructuredMigration.Validate(graph, graph, state) == StructuredMigrationFinding.UnsupportedCheckpoint,
                "a live Flowchart requires its own token-state validator");
        }

        var whileLoop = Wf.Workflow("while-source", Wf.While("while", true, Wait()));
        await using (var actor = await WaitingActor.StartAsync(whileLoop, mode))
        {
            var state = Json.Read<NativeState>(actor.Checkpoint());
            check(state.Waits.Values.Single().Key == "event:ready" &&
                StructuredMigration.Validate(whileLoop, Wf.Workflow("while-same", Wf.While("while", true, Wait())), state) is null,
                "While can keep its body and current iteration");
            check(StructuredMigration.Validate(whileLoop, Wf.Workflow("while-different",
                Wf.While("while", true, Wf.Event("other", "ready"))), state) == StructuredMigrationFinding.LiveNode,
                "While cannot replace a live body");
        }
    }

    private static async Task CheckSequence(string mode, Action<bool, string> check)
    {
        var source = Definition("source", Prefix(), Wait(), Wf.Set("tail", "output", "source"));
        await using var actor = await WaitingActor.StartAsync(source, mode);
        var before = actor.Checkpoint();
        var state = Json.Read<NativeState>(before);
        check(state.Frames.Values.Any(frame => frame.Node == "wait") && state.Waits.Values.Single().Key == "event:ready" &&
            state.Variables["output"]?.GetValue<string>() == "prefix", "the public Native turn commits the prefix and active wait");

        Check(Definition("target", Prefix(), Wait(), Wf.Set("new-tail", "output", "target")), null,
            "a Sequence can replace its unstarted tail");
        Check(Definition("insert-prefix", Prefix(), Wf.Set("extra", "output", "extra"), Wait()),
            StructuredMigrationFinding.SequencePrefix, "a new child inserted ahead of the waiting child is refused");
        Check(Definition("move-prefix", Wait(), Prefix()), StructuredMigrationFinding.SequencePrefix,
            "a completed child cannot move behind the waiting child");
        Check(Definition("change-wait", Prefix(), Wf.Event("wait", "other")), StructuredMigrationFinding.AuthoredNode,
            "a changed literal on the live wait is refused");
        Check(Definition("different-binding", Prefix(), Wf.Event("wait", Binding.Var("output"))),
            StructuredMigrationFinding.AuthoredNode, "a different binding kind on the live wait is refused");
        Check(Definition("move-wait", Prefix(), Wf.Sequence("inner", Wait())),
            StructuredMigrationFinding.LiveParent, "the live wait must keep its parent");
        Check(Wf.Workflow("add-declaration", Wf.Sequence("sequence", Prefix(), Wait()), ("output", null), ("new", 1)),
            StructuredMigrationFinding.VariableDeclarations, "declared defaults are not mapped by this validation slice");
        check(actor.Checkpoint() == before && Json.Write(state) == Json.Write(Json.Read<NativeState>(before)),
            "validating multiple candidates leaves both the loaded and persisted checkpoint intact");

        await actor.PublishAsync();
        check(actor.Status() == InstanceStatus.Finished && Json.Read<NativeState>(actor.Checkpoint()).Variables["output"]?.GetValue<string>() == "source",
            "the unchanged live instance consumes its original wait and executes the source tail");
        void Check(Workflow target, StructuredMigrationFinding? expected, string label) =>
            check(StructuredMigration.Validate(source, target, state) == expected, label);
    }

    private static async Task CheckContainers(string mode, Action<bool, string> check)
    {
        var condition = Wf.Workflow("if-source", Wf.If("if", true, Wait(), Wf.Set("else", "output", 1)));
        await using (var actor = await WaitingActor.StartAsync(condition, mode))
        {
            var state = Json.Read<NativeState>(actor.Checkpoint());
            check(state.Frames.Values.Any(frame => frame.Node == "wait" && frame.Tag == "True"),
                "the If checkpoint identifies the selected branch");
            check(StructuredMigration.Validate(condition, Wf.Workflow("if-safe",
                Wf.If("if", true, Wait(), Wf.Set("changed-else", "output", 2))), state) is null,
                "the untaken If branch can change");
            check(StructuredMigration.Validate(condition, Wf.Workflow("if-unsafe",
                Wf.If("if", true, Wf.Set("added-then", "output", 2), Wait())), state) == StructuredMigrationFinding.AuthoredNode,
                "an active branch cannot switch slots");
        }

        var parallel = Wf.Workflow("parallel-source", Wf.Parallel("parallel", 2, Wait(), Wf.Event("second", "other")));
        await using (var actor = await WaitingActor.StartAsync(parallel, mode))
        {
            var state = Json.Read<NativeState>(actor.Checkpoint());
            check(state.Waits.Count == 2, "both Parallel branches have live waits");
            check(StructuredMigration.Validate(parallel, Wf.Workflow("parallel-safe",
                Wf.Parallel("parallel", 2, Wf.Event("second", "other"), Wait())), state) is null,
                "Parallel accepts the same branch IDs in a different order");
            check(StructuredMigration.Validate(parallel, Wf.Workflow("parallel-add",
                Wf.Parallel("parallel", 2, Wait(), Wf.Event("second", "other"), Wf.Event("third", "another"))), state)
                == StructuredMigrationFinding.ChildLayout, "a live Parallel cannot add a branch");
        }
    }

    private static async Task CheckStoredReferences(string mode, Action<bool, string> check)
    {
        var source = Definition("result-source", Wf.Event("first", "prelude"), Wait());
        await using (var actor = await WaitingActor.StartAsync(source, mode))
        {
            await actor.PublishAsync("prelude");
            var state = Json.Read<NativeState>(actor.Checkpoint());
            check(state.Results.ContainsKey("first") && state.Frames.Values.All(frame => frame.Node != "first"),
                "an event result keeps its source node after that frame is gone");
            check(StructuredMigration.Validate(source, Definition("result-change", Wf.Event("first", "changed"), Wait()), state)
                == StructuredMigrationFinding.AuthoredNode, "a saved result keeps the completed node's bindings live");
        }

        var trigger = Definition("trigger-source", Wait(), Wf.Event("future-trigger", "future"));
        await using (var actor = await WaitingActor.StartAsync(trigger, mode, trigger: "future-trigger"))
        {
            var state = Json.Read<NativeState>(actor.Checkpoint());
            check(state.TriggerNode == "future-trigger" && state.Frames.Values.All(frame => frame.Node != "future-trigger"),
                "the pending trigger points to a node with no frame");
            check(StructuredMigration.Validate(trigger, Definition("remove-trigger", Wait()), state)
                == StructuredMigrationFinding.LiveNode, "the queued trigger's target cannot be removed");
        }
    }

    private static async Task CheckQueuedWork(Action<bool, string> check)
    {
        var source = Definition("agenda-source", Wait(), Wf.Set("tail", "output", 1));
        await using var actor = await WaitingActor.StartAsync(source, Modes.Durable, singleTurn: true);
        var state = Json.Read<NativeState>(actor.Checkpoint());
        check(state.Agenda.Any(work => work.Node == "wait") && state.Frames.Values.All(frame => frame.Node != "wait"),
            "Durable persists a scheduled child before its frame starts");
        check(StructuredMigration.Validate(source, Definition("remove-queued", Wf.Set("tail", "output", 1)), state)
            == StructuredMigrationFinding.LiveNode, "a scheduled child remains live even without a frame");
    }

    private static async Task CheckFinished(string mode, Action<bool, string> check)
    {
        var source = Definition("finished-source", Prefix());
        await using var actor = await WaitingActor.StartAsync(source, mode);
        var state = Json.Read<NativeState>(actor.Checkpoint());
        check(actor.Status() == InstanceStatus.Finished && state.Started && state.Results.Count == 0,
            "the completed Sequence retains its started marker and no live result nodes");
        check(StructuredMigration.Validate(source, Definition("unrelated", Wf.Set("unrelated", "output", 2)), state)
            == StructuredMigrationFinding.UnsupportedCheckpoint, "an empty completed checkpoint is refused");
    }

    private static Node Prefix() => Wf.Set("prefix", "output", "prefix");
    private static Node Wait() => Wf.Event("wait", "ready");
    private static Workflow Definition(string id, params Node[] children) =>
        Wf.Workflow(id, Wf.Sequence("sequence", children), ("output", null));

    private sealed class WaitingActor(InMemoryStore store, Contract.BoundScope scope, Contract.IIngress ingress,
        Contract.ActorRef actor, IScopeActivation owner) : IAsyncDisposable
    {
        internal static async Task<WaitingActor> StartAsync(Workflow source, string mode, string? trigger = null, bool singleTurn = false)
        {
            const long now = 1_701_800_000_000;
            var store = new InMemoryStore(1, () => now);
            var scope = new Contract.BoundScope("acme");
            Contract.IIngress ingress = new InMemoryIngress(store, new Requests("migration-validation"), "migration-validation");
            var runtime = new PublicRuntime(ingress, new ActorList(store, 16));
            var registry = new WorkflowRegistry().Add(source);
            var start = NativeExecutionModel.Start(source.Id, mode, triggerNode: trigger, trigger: trigger is null ? null : JsonValue.Create("receipt"));
            var actor = (await runtime.StartAsync(scope, "validate", start)).Actor!.Value;
            var adapter = new ActorAdapter(store, new PublicWorkflow([new NativeExecutionModel(registry)]));
            var worker = new Worker("validate", 0, store, adapter, new WorkerOptions { Monotonic = () => 0, PassTurnBudget = singleTurn ? 1 : 500 });
            var owner = (await new DurableHost([worker])
                .ActivateAsync(0))!;
            var instance = new WaitingActor(store, scope, ingress, actor, owner);
            if (singleTurn)
            {
                for (var step = 0; step < 20 && store.ReadFoundationActor(scope.Tenant, actor.Id).Actor is null; step++)
                    await instance.PumpAsync(1);
            }
            else await instance.PumpAsync();
            return instance;
        }

        internal string Checkpoint() => store.ReadFoundationActor(scope.Tenant, actor.Id).Actor?.Row.State
            ?? throw new InvalidOperationException("The Native checkpoint was not committed.");
        internal InstanceStatus Status() => store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!.Row.Status;

        internal async Task PublishAsync(string key = "ready")
        {
            await ingress.SendAsync(scope, new Contract.Target.Event("event:" + key), key, "published", "yes");
            await PumpAsync();
        }

        private async Task PumpAsync(int passes = 20)
        {
            for (var pass = 0; pass < passes; pass++)
            {
                await owner.WokenAsync(null);
                await owner.RunAsync();
                await Task.Yield();
            }
        }

        public ValueTask DisposeAsync() => owner.DisposeAsync();
    }
}
