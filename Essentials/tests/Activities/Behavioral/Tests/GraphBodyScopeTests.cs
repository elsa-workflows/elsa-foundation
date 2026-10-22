using System.Text.Json;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Graph.Runtime;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Behavioral;

/// <summary>
/// A node inside a closed-scope graph body reads the activity's own inputs, its variables and other body
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
        // A body reference to the started workflow's root scope does not reach the caller.
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
            Echo("node-compute", Literal("Value", 5), PlacedOccurrence),
            Echo("node-format", Result("node-compute", "node-body"), PlacedOccurrence));
        var boundary = Boundary(body);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(boundary));

        run.AssertWorkflowCompleted();
        Assert.Equal(5, ReadInput(run, "node-format"));
    }

    [Fact]
    public async Task A_nested_graph_reads_the_value_the_outer_graph_passed_it()
    {
        // US4 AS2-3: the inner boundary's own binding resolves in the outer graph; its body resolves in the inner one.
        await using var harness = GraphBoundaryCompleteTests.NewHarness();
        var inner = Boundary(
            Echo("node-read-y", Request("Y")),
            inputs: [Input("Y")],
            boundaryInputs: [Request("X", inputName: "Y")],
            nodeId: "node-inner");
        var outer = Boundary(inner, inputs: [Input("X")], boundaryInputs: [Literal("X", 5)]);

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(outer),
            new Dictionary<string, JsonElement> { ["X"] = JsonSerializer.SerializeToElement(99), ["Y"] = JsonSerializer.SerializeToElement(98) });

        run.AssertWorkflowCompleted();
        Assert.Equal(5, ReadInput(run, "node-read-y"));
    }

    [Fact]
    public async Task A_graph_compiled_before_closed_scopes_nested_in_a_closed_one_reads_the_enclosing_graph()
    {
        // The legacy body's caller is the closed graph around it, so its root reads land there, not on the workflow.
        await using var harness = GraphBoundaryCompleteTests.NewHarness();
        var legacy = Boundary(
            GraphBoundaryCompleteTests.Sequence(
                "node-legacy-body",
                Echo("node-read-a", Request("A")),
                Echo("node-read-total", Variable("total", VariableReference.WorkflowScopeId))),
            closed: false,
            nodeId: "node-legacy");
        var outer = Boundary(
            legacy,
            inputs: [Input("A")],
            variables: [new GraphActivityVariableDescriptor("total", "RunningTotal", ObjectType, WellKnownRuntimeDurableValueStorageDrivers.Json, new GraphActivityExpression("Literal", JsonSerializer.SerializeToElement(10)))],
            boundaryInputs: [Literal("A", 2)]);

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(outer, CallerTotal()),
            new Dictionary<string, JsonElement> { ["A"] = JsonSerializer.SerializeToElement(99) });

        run.AssertWorkflowCompleted();
        Assert.Equal(2, ReadInput(run, "node-read-a"));
        Assert.Equal(10, ReadInput(run, "node-read-total"));
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

    [Fact]
    public async Task A_body_node_captures_an_output_that_a_later_node_reads()
    {
        // The capture writes the graph's own variable row, and the caller's variable of the same name is untouched.
        await using var harness = GraphBoundaryCompleteTests.NewHarness();
        var body = GraphBoundaryCompleteTests.Sequence(
            "node-body",
            CaptureInto(Echo("node-lookup", Literal("Value", 5)), "total"),
            Echo("node-read-total", Variable("total", BoundaryNodeId)));
        var boundary = Boundary(
            body,
            variables: [new GraphActivityVariableDescriptor("total", "RunningTotal", ObjectType, WellKnownRuntimeDurableValueStorageDrivers.Json, new GraphActivityExpression("Literal", JsonSerializer.SerializeToElement(10)))]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(boundary, CallerTotal()));

        run.AssertWorkflowCompleted();
        Assert.Equal(5, ReadInput(run, "node-read-total"));
        Assert.Equal(0, run.WorkflowState!.RootVariableFrame!.Values["total"].InlineValue!.Value.GetInt32());
    }

    [Fact]
    public async Task A_captured_value_reaches_the_boundary_output_mapping()
    {
        await using var harness = ScriptingHarness();
        var body = CaptureInto(Echo("node-lookup", Literal("Value", 5)), "total");
        var boundary = Boundary(
            body,
            variables: [new GraphActivityVariableDescriptor("total", "RunningTotal", ObjectType, WellKnownRuntimeDurableValueStorageDrivers.Json, new GraphActivityExpression("Literal", JsonSerializer.SerializeToElement(10)))],
            outputs: [new GraphActivityOutputDescriptor("sum", ObjectType, WellKnownRuntimeDurableValueStorageDrivers.Json, "Sum")],
            outputMappings: [new GraphActivityOutputMappingDescriptor("sum", new GraphActivityExpression("JavaScript", JsonSerializer.SerializeToElement("getVariable('RunningTotal')")))]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(boundary));

        run.AssertWorkflowCompleted();
        var durableValues = await harness.Services.GetRequiredService<IDurableValueStateStore>()
            .ListAllDurableValueStatesAsync(run.WorkflowState!.WorkflowExecutionId);
        var output = Assert.Single(durableValues, value => value.DurableValueId.EndsWith(":output:3:sum", StringComparison.Ordinal));
        Assert.Equal(5, output.InlineValue!.Value.GetInt32());
    }

    [Theory]
    [InlineData("variables.RunningTotal + 1")]
    [InlineData("getVariable('RunningTotal') + 1")]
    [InlineData("getRunningTotal() + 1")]
    public async Task Body_javascript_reads_graph_variables_by_name_not_the_callers(string script)
    {
        // The ambient variables surface below a closed scope is the graph's variables, named by their rows.
        await using var harness = ScriptingHarness();
        var body = Echo("node-read-total", new RuntimeInputBinding(
            nameof(EchoActivity.Value), new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", script)));
        var boundary = Boundary(
            body,
            variables: [new GraphActivityVariableDescriptor("total", "RunningTotal", ObjectType, WellKnownRuntimeDurableValueStorageDrivers.Json, new GraphActivityExpression("Literal", JsonSerializer.SerializeToElement(10)))]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(boundary, CallerTotal()));

        run.AssertWorkflowCompleted();
        Assert.Equal(11, ReadInput(run, "node-read-total"));
    }

    [Fact]
    public async Task After_a_resume_the_body_reads_the_same_input_and_variable_values()
    {
        // SC-006: the body suspends on an Event before reading; the reads after the resume see the boundary's values.
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new Primitives.ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new GraphActivitiesRuntimeFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .Build(Enumerable.Range(1, 64).Select(index => $"actexec-{index}"));
        var body = GraphBoundaryCompleteTests.Sequence(
            "node-body",
            CaptureInto(Echo("node-lookup", Literal("Value", 5)), "total"),
            Nodes.Leaf("node-wait", typeof(Primitives.Activities.Event), new Dictionary<string, object?> { ["EventName"] = "go", ["CanStartWorkflow"] = false }),
            Echo("node-read-a", Request("A")),
            Echo("node-read-total", Variable("total", BoundaryNodeId)));
        var boundary = Boundary(
            body,
            inputs: [Input("A")],
            variables: [new GraphActivityVariableDescriptor("total", "RunningTotal", ObjectType, WellKnownRuntimeDurableValueStorageDrivers.Json, new GraphActivityExpression("Literal", JsonSerializer.SerializeToElement(10)))],
            boundaryInputs: [Literal("A", 2)]);
        var waitTarget = WorkflowExecutableResumeTarget.ComposeScopedId("node-wait", Primitives.Activities.Event.ResumeTargetId);
        var executable = new WorkflowExecutable(
            WorkflowExecutionHarness.Identity,
            boundary,
            new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                [waitTarget] = new(waitTarget, "node-wait", "ResumeAsync", new Dictionary<string, string>(), Primitives.Activities.Event.ResumeTargetId)
            },
            new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero),
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);

        var suspended = await harness.RunAsync(executable, allowPendingWorkOnTerminalCompletion: true, inputs: new Dictionary<string, JsonElement> { ["A"] = JsonSerializer.SerializeToElement(99) });
        var wait = suspended.State("node-wait");
        var resumed = await harness.ResumeAsync(
            WorkflowExecutionHarness.Identity,
            bookmarkId: Assert.Single(wait.BookmarkIds),
            activityExecutionId: wait.InvocationId,
            executableNodeId: "node-wait",
            resumeTargetId: waitTarget,
            stimulusType: Primitives.Activities.EventStimulus.StimulusType,
            stimulusHash: Primitives.Activities.EventStimulus.Hash("go"),
            input: JsonSerializer.SerializeToElement(new { eventName = "go" }));

        resumed.AssertWorkflowCompleted();
        Assert.Equal(2, ReadInput(resumed, "node-read-a"));
        // The value captured before the suspension, not the variable's initial 10.
        Assert.Equal(5, ReadInput(resumed, "node-read-total"));
    }

    [Fact]
    public async Task Add_two_numbers_computes_its_sum_in_a_body_node_from_its_own_inputs()
    {
        // SC-001 through the API: Sum comes from a body node reading A and B as request parameters, and the caller's
        // own A and B are not what it reads.
        await using var harness = ScriptingHarness();
        var parameters = new Dictionary<string, ExpressionParameterBinding>
        {
            ["a"] = new WorkflowRequestExpressionParameterBinding("A"),
            ["b"] = new WorkflowRequestExpressionParameterBinding("B")
        };
        var body = Echo("node-sum", new RuntimeInputBinding(
            nameof(EchoActivity.Value), new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", "args.a + args.b", parameters: parameters)));
        var boundary = Boundary(body, inputs: [Input("A"), Input("B")], boundaryInputs: [Literal("A", 2), Literal("B", 3)]);

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(boundary),
            new Dictionary<string, JsonElement> { ["A"] = JsonSerializer.SerializeToElement(40), ["B"] = JsonSerializer.SerializeToElement(50) });

        run.AssertWorkflowCompleted();
        Assert.Equal(5, ReadInput(run, "node-sum"));
    }

    private static WorkflowExecutionHarness ScriptingHarness() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new GraphActivitiesRuntimeFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .WithFeature(services => new Expressions.ExpressionsFeature().ConfigureServices(services))
            .WithFeature(services => new Expressions.JavaScript.JavaScriptFeature().ConfigureServices(services))
            .WithFeature(services => new Expressions.JavaScript.Jint.JintFeature().ConfigureServices(services))
            .WithFeature(services => services.AddMemoryCache())
            .Build(Enumerable.Range(1, 64).Select(index => $"actexec-{index}"));

    private static ExecutableNode CaptureInto(ExecutableNode node, string variableKey)
    {
        var capture = new RuntimeOutputCapture(
            nameof(EchoResult.Value),
            $"graph-variable:{variableKey}",
            new RuntimeValueTypeDescriptor("object", WellKnownRuntimeDurableValueStorageDrivers.Json, ObjectType),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Custom,
            captureOnSuccessfulCompletion: true,
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.TargetVariableReferenceKey] = variableKey,
                [RuntimeMetadataKeys.VariableName] = "RunningTotal",
                [RuntimeMetadataKeys.StorageDriverKey] = WellKnownRuntimeDurableValueStorageDrivers.Json,
                [RuntimeMetadataKeys.CaptureTargetScopeId] = BoundaryNodeId
            },
            WellKnownRuntimeDurableValueStorageDrivers.Json);
        return new ExecutableNode(
            node.ExecutableNodeId, node.AuthoredActivityId, node.ActivityType, node.ActivityTypeVersion,
            node.DescriptorType, node.DescriptorPayload, node.InputBindings, node.Metadata,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture> { [capture.OutputName] = capture });
    }

    private static RuntimeVariableDeclaration CallerTotal() => new(
        "total", "RunningTotal", new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline, Literal("total", 0));

    // A placed body occurrence carries its template's identity, which makes it open its own execution scope (plan A-2).
    private static readonly IReadOnlyDictionary<string, string> PlacedOccurrence =
        new Dictionary<string, string> { ["activity.definitionVersionId"] = "version-echo" };

    private static int ReadInput(WorkflowExecutionRun run, string nodeId) =>
        run.State(nodeId).InputSnapshot!.Values[nameof(EchoActivity.Value)].InlineValue!.Value.GetInt32();

    private static ExecutableNode Echo(string nodeId, RuntimeInputBinding value, IReadOnlyDictionary<string, string>? metadata = null) => new(
        executableNodeId: nodeId,
        authoredActivityId: $"authored-{nodeId}",
        activityType: typeof(EchoActivity).FullName!,
        activityTypeVersion: "1.0.0",
        descriptorType: "test",
        descriptorPayload: JsonSerializer.SerializeToElement(new { }),
        inputBindings: new Dictionary<string, RuntimeInputBinding> { [value.InputName] = value },
        metadata: metadata ?? new Dictionary<string, string>());

    private static RuntimeInputBinding Request(string memberKey, string inputName = nameof(EchoActivity.Value)) => new(
        inputName, new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
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
        bool closed = true,
        IReadOnlyList<GraphActivityOutputDescriptor>? outputs = null,
        IReadOnlyList<GraphActivityOutputMappingDescriptor>? outputMappings = null,
        string nodeId = BoundaryNodeId)
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
            outputs: outputs ?? [],
            outputMappings: outputMappings ?? [],
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
            nodeId,
            $"authored-{nodeId}",
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
