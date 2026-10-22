using System.Text.Json;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Behavioral;

/// <summary>
/// Spec 265: a node inside a closed-scope graph body reads the activity's own inputs, its variables and other body
/// nodes' results, never the started workflow's. The boundaries here are built as placement leaves them: body
/// references already point at placed identities and the boundary carries the closed-scope marker.
/// </summary>
public sealed class GraphBodyScopeTests
{
    private const string BoundaryNodeId = "node-boundary";
    private const string TemplateHash = "sha256:graph-template";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonElement ObjectType = JsonSerializer.SerializeToElement(new { alias = "object" });

    [Fact]
    public async Task A_body_node_reads_the_value_the_caller_passed_not_the_started_workflows_input()
    {
        await using var harness = GraphBoundaryCompleteTests.NewHarness();
        var body = Echo("node-read-a", Request("A"));
        var boundary = Boundary(body, inputs: [Input("A")], boundaryInputs: [Literal("A", 2)]);

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(boundary),
            new Dictionary<string, JsonElement> { ["A"] = JsonSerializer.SerializeToElement(99) });

        run.AssertWorkflowCompleted();
        Assert.Equal(2, ReadInput(run, "node-read-a"));
    }

    [Fact]
    public async Task A_body_node_reads_a_graph_variable_not_a_caller_variable_of_the_same_name()
    {
        await using var harness = GraphBoundaryCompleteTests.NewHarness();
        var body = Echo("node-read-total", Variable("total", BoundaryNodeId));
        var boundary = Boundary(
            body,
            variables: [new GraphActivityVariableDescriptor("total", "RunningTotal", ObjectType, WellKnownRuntimeDurableValueStorageDrivers.Json, new GraphActivityExpression("Literal", JsonSerializer.SerializeToElement(10)))]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            boundary,
            CallerTotal()));

        run.AssertWorkflowCompleted();
        Assert.Equal(10, ReadInput(run, "node-read-total"));
    }

    [Fact]
    public async Task A_body_node_inside_a_container_reads_a_graph_variable()
    {
        await using var harness = GraphBoundaryCompleteTests.NewHarness();
        var body = GraphBoundaryCompleteTests.Sequence("node-body", Echo("node-read-total", Variable("total", BoundaryNodeId)));
        var boundary = Boundary(
            body,
            variables: [new GraphActivityVariableDescriptor("total", "RunningTotal", ObjectType, WellKnownRuntimeDurableValueStorageDrivers.Json, new GraphActivityExpression("Literal", JsonSerializer.SerializeToElement(10)))]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(boundary));

        run.AssertWorkflowCompleted();
        Assert.Equal(10, ReadInput(run, "node-read-total"));
    }

    [Fact]
    public async Task A_caller_variable_is_not_visible_inside_the_body()
    {
        // D-1: a body reference to the started workflow's root scope does not reach the caller.
        await using var harness = GraphBoundaryCompleteTests.NewHarness();
        var body = Echo("node-read-caller", Variable("total", VariableReference.WorkflowScopeId));
        var boundary = Boundary(body);

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(
                boundary,
                CallerTotal()),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        // The read cannot materialize, so the node never runs and the workflow does not complete on a caller value.
        Assert.NotEqual(ActivityExecutionStatus.Completed, run.State("node-read-caller").Status);
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState!.Status);
    }

    [Fact]
    public async Task A_body_node_reads_another_body_nodes_result_from_the_same_invocation()
    {
        await using var harness = GraphBoundaryCompleteTests.NewHarness();
        var body = GraphBoundaryCompleteTests.Sequence(
            "node-body",
            Echo("node-compute", Literal("Value", 5)),
            Echo("node-format", Result("node-compute", "node-body")));
        var boundary = Boundary(body);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(boundary));

        run.AssertWorkflowCompleted();
        Assert.Equal(5, ReadInput(run, "node-format"));
    }

    [Fact]
    public async Task A_boundary_compiled_before_closed_scopes_still_reads_the_caller()
    {
        // Published versions keep their behaviour: without the marker the body resolves against the caller.
        await using var harness = GraphBoundaryCompleteTests.NewHarness();
        var body = Echo("node-read-a", Request("A"));
        var boundary = Boundary(body, inputs: [Input("A")], boundaryInputs: [Literal("A", 2)], closed: false);

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(boundary),
            new Dictionary<string, JsonElement> { ["A"] = JsonSerializer.SerializeToElement(99) });

        run.AssertWorkflowCompleted();
        Assert.Equal(99, ReadInput(run, "node-read-a"));
    }

    private static RuntimeVariableDeclaration CallerTotal() => new(
        "total", "RunningTotal", new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline, Literal("total", 0));

    private static int ReadInput(WorkflowExecutionRun run, string nodeId) =>
        run.State(nodeId).InputSnapshot!.Values[nameof(EchoActivity.Value)].InlineValue!.Value.GetInt32();

    private static ExecutableNode Echo(string nodeId, RuntimeInputBinding value) => new(
        executableNodeId: nodeId,
        authoredActivityId: $"authored-{nodeId}",
        activityType: typeof(EchoActivity).FullName!,
        activityTypeVersion: "1.0.0",
        descriptorType: "test",
        descriptorPayload: JsonSerializer.SerializeToElement(new { }),
        inputBindings: new Dictionary<string, RuntimeInputBinding> { [value.InputName] = value },
        metadata: new Dictionary<string, string>());

    private static RuntimeInputBinding Request(string memberKey) => new(
        nameof(EchoActivity.Value), new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.WorkflowRequest, workflowRequest: new RuntimeWorkflowRequestReference(memberKey));

    private static RuntimeInputBinding Variable(string key, string scopeId) => new(
        nameof(EchoActivity.Value), new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.VariableRead, variable: new RuntimeVariableReference(key, scopeId));

    private static RuntimeInputBinding Result(string producerNodeId, string producerScopeId) => new(
        nameof(EchoActivity.Value), new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.ActivityResult, activityResult: new RuntimeActivityResultReference(producerNodeId, nameof(EchoResult.Value), producerScopeId));

    private static RuntimeInputBinding Literal(string key, int value) => new(
        key, new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
        literal: ValueEnvelope.Inline(new ValueTypeDescriptor("Object"), JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));

    private static GraphActivityInputDescriptor Input(string key, GraphActivityExpression? defaultValue = null) =>
        new(key, ObjectType, WellKnownRuntimeDurableValueStorageDrivers.Json, defaultValue, key);

    // Mirrors what placement leaves for a closed-scope graph boundary (see GraphActivityDrive).
    private static ExecutableNode Boundary(
        ExecutableNode entry,
        IReadOnlyList<GraphActivityInputDescriptor>? inputs = null,
        IReadOnlyList<GraphActivityVariableDescriptor>? variables = null,
        IReadOnlyList<RuntimeInputBinding>? boundaryInputs = null,
        bool closed = true)
    {
        var descriptor = new GraphActivityDescriptor(
            definitionId: "definition-graph",
            definitionVersionId: "definition-graph-version",
            version: "1.0.0",
            templateHash: TemplateHash,
            entryNodeId: entry.ExecutableNodeId,
            entryOccurrenceId: null,
            inputs: inputs ?? [],
            variables: variables ?? [],
            outputs: [],
            outputMappings: [],
            requiredInputReferenceKeys: [],
            requiredOutputReferenceKeys: []);
        var payload = JsonSerializer.SerializeToElement(descriptor, SerializerOptions);
        var contract = new ActivityContract(
            "graph",
            "1.0.0",
            WellKnownRuntimeActivityConsumers.GraphActivity,
            payload,
            (inputs ?? []).Select(input => new ActivityInputContract(
                input.ReferenceKey, input.ReferenceKey, new ValueTypeDescriptor("Object"),
                isRequired: false, isNullable: true, hasDefault: false, defaultValue: null, policy: ActivityValuePolicy.Default)).ToArray(),
            new ActivityResultContract(
                new ValueTypeDescriptor("Object"),
                true,
                ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result },
                []),
            [ActivityOutcomes.Done],
            new ActivityActivationRequirement(
                WellKnownRuntimeActivityConsumers.GraphActivity,
                WellKnownRuntimeActivityConsumers.GraphActivity));
        var metadata = new Dictionary<string, string> { ["graph.templateHash"] = TemplateHash };
        if (closed)
            metadata[RuntimeMetadataKeys.ActivityExecutionScope] = RuntimeMetadataKeys.ActivityExecutionScopeClosed;

        return new ExecutableNode(
            BoundaryNodeId,
            $"authored-{BoundaryNodeId}",
            "graph",
            "1.0.0",
            new RuntimeActivityDescriptor(
                WellKnownRuntimeActivityConsumers.GraphActivity,
                RuntimeActivityDescriptor.InitialSchemaVersion,
                payload),
            (boundaryInputs ?? []).ToDictionary(binding => binding.InputName, StringComparer.Ordinal),
            new Dictionary<string, RuntimeOutputCapture>(),
            metadata,
            [new ExecutableChildSlot("Graph.Entry", [entry])],
            structure: null,
            activityContract: contract);
    }

    public sealed record EchoResult([property: Output(Key = "Value", Path = "value")] object? Value);

    /// <summary>Returns its input as its result, so a test can read what a body node resolved.</summary>
    public sealed class EchoActivity : Activity<EchoResult>
    {
        [ActivityInput] public object? Value { get; set; }

        protected override ValueTask<ActivityTransition<EchoResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new EchoResult(Value)));
    }
}
