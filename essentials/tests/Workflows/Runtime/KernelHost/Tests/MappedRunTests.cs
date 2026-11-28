using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;
using Contract = Kernel.Foundation.Contracts;
using Native = Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

/// <summary>Mapped workflows run on the Native model, and a Dispatch starts the artifact the host serves for its definition.</summary>
public sealed class MappedRunTests
{
    [Fact]
    public async Task A_mapped_workflow_runs_to_its_end_on_the_native_model()
    {
        var root = Sequence("root",
            Set("set", "v-x", RequestMember("value", "Order", "amount")),
            Intrinsic("correlate", WorkflowIntrinsicKind.SetCorrelationId, null, RequestMember(WorkflowIntrinsicInputKeys.Value, "Order", "id")),
            Node("check", Types.If, [Literal("Condition", true, "Boolean")], Structure("elsa.if.structure", new { then = "yes", @else = "no" }),
                [Slot("If.Then", SetOutput("yes", "total", VariableRead("value", "v-x"))), Slot("If.Else", SetOutput("no", "total", Literal("value", 0)))]),
            StructureTestsGraph(),
            SetOutput("approved", "approved", VariableRead("value", "v-out")),
            Intrinsic("finish", WorkflowIntrinsicKind.Finish, null, Literal(WorkflowIntrinsicInputKeys.Outcome, "Approved", "String")));
        var workflow = NativeExecutableMapper.Map(Executable(root, Variable("v-x", "x"), Variable("v-out", "out")));

        var turn = await NativeTurn.StartAsync(workflow, input: new JsonObject { ["Order"] = new JsonObject { ["id"] = "order-42", ["amount"] = 12 } });

        Assert.Equal(Contract.ActorStatus.Finished, turn.Status);
        Assert.Equal(12, turn.Output!["total"]!.GetValue<int>());
        // The GraphActivity's body set its variable from its input and assigned its output, which the capture read.
        Assert.Equal(7, turn.Output["approved"]!.GetValue<int>());
        Assert.Equal("order-42", turn.Labels[Native.NativeLabels.CorrelationId]);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, 7)]
    [InlineData(true, null)]
    public async Task A_scoped_set_output_enforces_nullability_on_the_evaluated_value(bool isNullable, int? amount)
    {
        var graph = MappingStructureTests.Graph("approve", out _, isNullable: isNullable, amount: Request("amount", "amount"));
        var root = Sequence("root", graph, SetOutput("approved", "approved", VariableRead("value", "v-out")),
            SetOutput("legacy", "legacy", Literal("value", null)));
        var workflow = NativeExecutableMapper.Map(Executable(root, Variable("v-out", "out")));

        var turn = await NativeTurn.StartAsync(workflow, input: new JsonObject { ["amount"] = amount is { } value ? JsonValue.Create(value) : null });

        if (!isNullable && amount is null)
        {
            Assert.Equal(Contract.ActorStatus.Faulted, turn.Status);
            Assert.Equal("activity.graph.output-null", turn.Output?["Code"]?.GetValue<string>());
            return;
        }

        Assert.Equal(Contract.ActorStatus.Finished, turn.Status);
        var output = Assert.IsType<JsonObject>(turn.Output);
        Assert.True(output.TryGetPropertyValue("approved", out var assigned));
        Assert.Equal(amount, assigned?.GetValue<int>());
        Assert.True(output.TryGetPropertyValue("legacy", out var legacy));
        Assert.Null(legacy);
    }

    [Fact]
    public async Task Console_activities_write_to_their_own_model_output()
    {
        var workflow = Native.Wf.Workflow("console", Native.Wf.Sequence("root",
            Native.Wf.WriteLine("line", "first"), Native.Wf.WriteLines("lines", Native.Binding.Literal(new[] { "second", "third" }))));
        using var first = new StringWriter();
        using var second = new StringWriter();

        await Task.WhenAll(NativeTurn.StartAsync(workflow, new Native.NativeExecutionOptions { ConsoleOutput = first }),
            NativeTurn.StartAsync(workflow, new Native.NativeExecutionOptions { ConsoleOutput = second }));

        var expected = string.Join(Environment.NewLine, ["first", "second", "third", ""]);
        Assert.Equal(expected, first.ToString());
        Assert.Equal(expected, second.ToString());
    }

    [Fact]
    public async Task A_loop_body_reads_its_item_and_index_on_the_native_model()
    {
        var root = Sequence("root",
            Node("each", Types.ForEach, [Literal("Collection", new[] { "a", "b", "c" })], Structure("elsa.foreach.structure", new { body = "item-body" }),
                [Slot("ForEach.Body", Sequence("item-body",
                    Set("item", "v-item", VariableRead("value", "currentItem", scope: "each")),
                    Set("position", "v-position", VariableRead("value", "currentIndex", scope: "each"))))]),
            Node("count", Types.For, [Literal("Start", 0, "Int32"), Literal("End", 4, "Int32")], Structure("elsa.for.structure", new { body = "pass" }),
                [Slot("For.Body", Set("pass", "v-index", VariableRead("value", "index", scope: "count")))]),
            SetOutput("item-out", "item", VariableRead("value", "v-item")),
            SetOutput("position-out", "position", VariableRead("value", "v-position")),
            SetOutput("index-out", "index", VariableRead("value", "v-index")));
        var workflow = NativeExecutableMapper.Map(Executable(root, Variable("v-item", "item"), Variable("v-position", "position"), Variable("v-index", "lastIndex")));

        var turn = await NativeTurn.StartAsync(workflow);

        Assert.Equal(Contract.ActorStatus.Finished, turn.Status);
        Assert.Equal("c", turn.Output!["item"]!.GetValue<string>());
        Assert.Equal(2, turn.Output["position"]!.GetValue<int>());
        Assert.Equal(3, turn.Output["index"]!.GetValue<int>());
    }

    private static ExecutableNode StructureTestsGraph() => MappingStructureTests.Graph("approve", out _);

    [Fact]
    public async Task A_selected_turn_reloads_its_exact_plan_and_keeps_it_when_the_registry_changes_again()
    {
        var selected = ExactPlan("selected", "expected");
        var replacement = ExactPlan("replacement", "wrong");
        var child = Native.Wf.Workflow("child", Native.Wf.Sequence("empty"));
        var registry = new Native.WorkflowRegistry().Add(replacement);
        var loads = new List<string>();
        var model = new Native.NativeExecutionModel(registry, new Native.NativeExecutionOptions
        {
            ResolveWorkflow = (id, _) =>
            {
                loads.Add(id);
                return ValueTask.FromResult<Native.Workflow?>(selected);
            },
            ServedWorkflow = _ =>
            {
                registry.Add(replacement);
                return Native.ServedDefinition.FromWorkflow(child);
            }
        });

        var finished = await NativeTurn.StartAsync(model, selected);

        Assert.Equal([selected.Id], loads);
        Assert.Single(finished.Children);
        Assert.Same(replacement, registry.Get(selected.Id));
        Assert.Equal("expected", finished.Output?["value"]?.GetValue<string>());

        static Native.Workflow ExactPlan(string selection, string output) => new("same-id", Native.Wf.Sequence("root",
            Native.Wf.Dispatch("dispatch", "child", wait: false), Native.Wf.SetOutput("output", "value", output)))
            { ExecutableSelection = selection };
    }

    [Fact]
    public async Task A_same_id_resolver_result_cannot_run_a_different_selected_executable()
    {
        var selected = new Native.Workflow("artifact", Native.Wf.WriteLine("out", "selected")) { ExecutableSelection = "selected" };
        var replacement = new Native.Workflow(selected.Id, Native.Wf.WriteLine("out", "different")) { ExecutableSelection = "different" };
        var registry = new Native.WorkflowRegistry();
        var model = new Native.NativeExecutionModel(registry, new Native.NativeExecutionOptions
        {
            ResolveWorkflow = (_, _) => ValueTask.FromResult<Native.Workflow?>(replacement)
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => NativeTurn.StartAsync(model, selected));

        Assert.Equal("The exact executable selected for this workflow is unavailable.", error.Message);
        Assert.False(registry.Contains(selected.Id));
    }

    [Fact]
    public async Task A_background_job_reloads_the_identity_in_its_retained_recipe()
    {
        var selected = new Native.Workflow("artifact", Native.Wf.Call("call", "expected")) { ExecutableSelection = "selected" };
        var replacement = new Native.Workflow(selected.Id, Native.Wf.Call("wrong-node", "wrong")) { ExecutableSelection = "different" };
        var first = await NativeTurn.StartAsync(selected);
        var recipe = Assert.Single(first.Native.Waits).Value.Job!;
        var args = JsonSerializer.Deserialize<Native.BackgroundArgs>(recipe.Arguments!)!;
        Assert.Equal(selected.ExecutableSelection, args.ExecutableSelection);
        var job = new BackgroundJob(first.Instance.Ref, recipe.Key, recipe.Arguments);
        var registry = new Native.WorkflowRegistry().Add(replacement);
        var model = new Native.NativeExecutionModel(registry, new Native.NativeExecutionOptions
        {
            ResolveWorkflow = (_, _) => ValueTask.FromResult<Native.Workflow?>(selected)
        });

        var result = Assert.IsType<Native.BackgroundResult>(await model.ExecuteJobAsync(job, CancellationToken.None));

        Assert.Equal("answer of expected", result.Result?.GetValue<string>());
        Assert.Same(selected, registry.Get(selected.Id));
    }

    private sealed record BackgroundJob(Contract.ActorRef Actor, string Key, string? Arguments) : Contract.IJob
    {
        public string AttemptId => "attempt";
        public int AttemptNumber => 1;
        public long? Deadline => null;
    }

    [Fact]
    public async Task A_migration_uses_the_selected_runs_logical_definition_after_cold_material_resolution()
    {
        var source = new Native.Workflow("before", Native.Wf.Event("wait", "ready"), definition: "logical")
            { ExecutableSelection = "first-material" };
        var canonical = new Native.Workflow(source.Id, source.Root, definition: "original-source")
            { ExecutableSelection = source.ExecutableSelection };
        var target = new Native.Workflow("after", Native.Wf.Event("wait", "ready"), definition: source.Definition)
            { ExecutableSelection = "second-material" };
        var turn = await NativeTurn.StartAsync(source);
        var model = new Native.NativeExecutionModel(new Native.WorkflowRegistry().Add(canonical).Add(target));

        var refusal = model.ValidateMigration(turn.Instance with { Definition = source.Definition }, turn.State, target.Id, out var selection);

        Assert.Null(refusal);
        Assert.Equal(target.ExecutableSelection, selection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_dispatch_starts_the_artifact_the_host_serves_for_its_definition(bool wait)
    {
        var workflow = NativeExecutableMapper.Map(Executable(Node("dispatch", Types.Dispatch,
            [Literal("WorkflowDefinitionId", "invoicing", "String"), Literal("WaitForCompletion", wait, "Boolean")])));
        // A publish lands between any two reads: a second read would see version 5.
        var reads = 0;
        var served = new Native.NativeExecutionOptions
        {
            ServedWorkflow = id => id == "invoicing" ? Native.ServedDefinition.By(++reads == 1 ? "invoicing-artifact-4" : "invoicing-artifact-5") : Native.ServedDefinition.None,
        };

        var resolved = await NativeTurn.StartAsync(workflow, served);
        var unresolved = await NativeTurn.StartAsync(workflow);

        // Read once, and that answer checked and started the child.
        Assert.Equal(1, reads);
        var (start, detached) = Assert.Single(resolved.Children);
        Assert.Equal(("invoicing-artifact-4", !wait), (start.Workflow, detached));
        // A host that serves no publications starts the id itself.
        Assert.Equal("invoicing", Assert.Single(unresolved.Children).Start.Workflow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_dispatch_keeps_the_identity_from_its_exact_served_selection(bool wait)
    {
        var parent = NativeExecutableMapper.Map(Executable(Node("dispatch", Types.Dispatch,
            [Literal("WorkflowDefinitionId", "invoice", "String"), Literal("WaitForCompletion", wait, "Boolean")])));
        var selected = NativeExecutableMapper.Map(ExecutableOf("invoice-id", "invoice", WriteLine("old", Literal("text", "old"))));
        var replacement = new Native.Workflow(selected.Id, new Native.Node("replacement", typeof(Native.WriteLine)), definition: selected.Definition)
            { ExecutableSelection = "different material" };
        var registry = new Native.WorkflowRegistry().Add(parent).Add(selected);
        var model = new Native.NativeExecutionModel(registry, new Native.NativeExecutionOptions
        {
            ServedWorkflow = _ =>
            {
                registry.Add(replacement);
                return Native.ServedDefinition.FromWorkflow(selected);
            }
        });

        var (start, detached) = Assert.Single((await NativeTurn.StartAsync(model, parent)).Children);
        Assert.Equal((selected.Id, selected.Definition, selected.ExecutableSelection, !wait),
            (start.Workflow, start.Definition, start.ExecutableSelection, detached));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_dispatch_of_a_definition_the_host_does_not_publish_faults_before_a_child_starts(bool wait)
    {
        var workflow = NativeExecutableMapper.Map(Executable(Node("dispatch", Types.Dispatch,
            [Literal("WorkflowDefinitionId", "invoicing", "String"), Literal("WaitForCompletion", wait, "Boolean")])));
        var resolves = new List<string>();
        var publishing = new Native.NativeExecutionOptions
        {
            ServedWorkflow = _ => Native.ServedDefinition.None,
            // A stored artifact of that id exists: the Dispatch still does not reach it.
            ResolveWorkflow = (id, _) =>
            {
                resolves.Add(id);
                return ValueTask.FromResult<Native.Workflow?>(NativeExecutableMapper.Map(ExecutableOf(id, id, WriteLine("w", Literal("text", "stored")))));
            },
        };

        var unknown = await NativeTurn.StartAsync(workflow, publishing);

        Assert.Empty(unknown.Children);
        Assert.Empty(resolves);
        Assert.Equal(Contract.ActorStatus.Faulted, unknown.Status);
        Assert.Equal(("dispatch.unpublished", "Dispatch Workflow names definition 'invoicing', which is not published on this host. Publish it, then run the workflow again."),
            (unknown.Output?["Code"]?.GetValue<string>(), unknown.Output?["Message"]?.GetValue<string>()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_dispatch_of_a_definition_several_channels_serve_without_a_default_faults(bool wait)
    {
        var workflow = NativeExecutableMapper.Map(Executable(Node("dispatch", Types.Dispatch,
            [Literal("WorkflowDefinitionId", "invoicing", "String"), Literal("WaitForCompletion", wait, "Boolean")])));

        var turn = await NativeTurn.StartAsync(workflow, new Native.NativeExecutionOptions { ServedWorkflow = _ => Native.ServedDefinition.SeveralServe });

        Assert.Empty(turn.Children);
        Assert.Equal(Contract.ActorStatus.Faulted, turn.Status);
        Assert.Equal(("dispatch.ambiguous", "Definition 'invoicing' is published in several channels and none is 'default'. Publish it to the default channel."),
            (turn.Output?["Code"]?.GetValue<string>(), turn.Output?["Message"]?.GetValue<string>()));
    }

    [Fact]
    public async Task A_dispatch_of_a_workflow_the_registry_holds_starts_it_on_a_publishing_host()
    {
        var workflow = NativeExecutableMapper.Map(Executable(Node("dispatch", Types.Dispatch,
            [Literal("WorkflowDefinitionId", "helper", "String"), Literal("WaitForCompletion", true, "Boolean")])));
        var helper = NativeExecutableMapper.Map(ExecutableOf("helper", "helper", WriteLine("w", Literal("text", "help"))));
        var model = new Native.NativeExecutionModel(new Native.WorkflowRegistry().Add(workflow).Add(helper),
            new Native.NativeExecutionOptions { ServedWorkflow = _ => Native.ServedDefinition.None });

        var turn = await NativeTurn.StartAsync(model, workflow);

        Assert.Equal("helper", Assert.Single(turn.Children).Start.Workflow);
    }
}
