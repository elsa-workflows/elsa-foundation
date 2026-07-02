using System.Text.Json;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Options;
using Elsa.Expressions.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Sequence.Tests;

/// <summary>
/// End-to-end proof that container-scoped (and workflow) variables resolve through the live
/// publish→run pipeline (ADR 0027, #206/#207/#210). A Sequence declares a container variable; its
/// descendant activities read it through a structured <c>Variable</c> input binding, assign it, and a
/// later sibling observes the assignment. A resumed run rebuilds the same container execution's
/// values from its persisted snapshot.
/// </summary>
public sealed class SequenceContainerVariableRuntimeTests
{
    private const string CounterReferenceKey = "var-counter";
    private const string SequenceNodeId = "node-sequence";
    private readonly DateTimeOffset _now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Descendant_reads_container_variable_default_through_variable_input_binding()
    {
        await using var provider = NewProvider(["actexec-sequence", "actexec-read"]);
        var executable = NewExecutable(
            children: [CaptureNode("node-read", VariableBinding(CounterReferenceKey, SequenceNodeId))],
            variableDefault: "starting");

        await ExecuteAsync(provider, executable);

        Assert.Equal("starting", CapturedValue(provider, "actexec-read"));
    }

    [Fact]
    public async Task Descendant_assignment_is_observed_by_a_later_sibling_in_the_same_container()
    {
        await using var provider = NewProvider(["actexec-sequence", "actexec-assign", "actexec-read"]);
        var executable = NewExecutable(
            children:
            [
                AssignNode("node-assign", "Counter", "assigned-value"),
                CaptureNode("node-read", VariableBinding(CounterReferenceKey, SequenceNodeId))
            ],
            variableDefault: "starting");

        await ExecuteAsync(provider, executable);

        // The later sibling sees the assignment the earlier sibling made through the shared container scope.
        Assert.Equal("assigned-value", CapturedValue(provider, "actexec-read"));

        // The assignment was persisted to the owning container execution's snapshot (single source of truth).
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        var sequenceState = Assert.Single(states, state => state.Execution.ExecutableNodeId == SequenceNodeId);
        Assert.Contains("assigned-value", sequenceState.Metadata[RuntimeMetadataKeys.ScopedVariableValues]);
    }

    [Fact]
    public async Task Resume_rebuilds_container_values_from_the_persisted_snapshot()
    {
        await using var provider = NewProvider(["actexec-sequence", "actexec-assign", "actexec-read"]);
        var executable = NewExecutable(
            children:
            [
                AssignNode("node-assign", "Counter", "assigned-value"),
                CaptureNode("node-read", VariableBinding(CounterReferenceKey, SequenceNodeId))
            ],
            variableDefault: "starting");
        await ExecuteAsync(provider, executable);

        // Simulate a resume: rebuild the descendant's visible scope from the persisted container
        // state, exactly as the invoke handler does, and assert the assigned value is restored.
        // A real resume targets a container that is still running (a descendant suspended on a
        // bookmark), so clear the completion marker this finished run left behind — a completed
        // scope is deliberately non-live (#210).
        var store = provider.GetRequiredService<IActivityExecutionStateStore>();
        var readState = Assert.Single(await store.ListAsync("wfexec-1"), state => state.Execution.ExecutableNodeId == "node-read");
        var sequenceState = Assert.Single(await store.ListAsync("wfexec-1"), state => state.Execution.ExecutableNodeId == SequenceNodeId);
        await store.SaveAsync(sequenceState with
        {
            Metadata = sequenceState.Metadata
                .Where(entry => entry.Key != RuntimeMetadataKeys.ScopedVariableScopeCompleted)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)
        });
        var scope = await new RuntimeContainerScopeService(store).BuildScopeAsync(executable, "wfexec-1", readState);

        Assert.NotNull(scope);
        Assert.True(scope!.TryGetValue(new VariableReference(CounterReferenceKey, SequenceNodeId), out var restored));
        Assert.Equal("assigned-value", restored?.ToString());
    }

    [Fact]
    public async Task Container_completion_marks_scope_completed_and_redacts_evidence_by_default()
    {
        await using var provider = NewProvider(["actexec-sequence", "actexec-assign", "actexec-read"]);
        var executable = NewExecutable(
            children:
            [
                AssignNode("node-assign", "Counter", "assigned-value"),
                CaptureNode("node-read", VariableBinding(CounterReferenceKey, SequenceNodeId))
            ],
            variableDefault: "starting");

        await ExecuteAsync(provider, executable);

        // The completed container's scope is marked completed (no longer live for later expressions).
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        var sequenceState = Assert.Single(states, state => state.Execution.ExecutableNodeId == SequenceNodeId);
        Assert.Equal(bool.TrueString, sequenceState.Metadata[RuntimeMetadataKeys.ScopedVariableScopeCompleted]);

        // The completed-scope variable is recorded as inspection evidence, but the default capture
        // policy omits its payload — completed-scope values are retained only when policy allows.
        var snapshot = await ContainerVariableSnapshot(provider, "actexec-sequence", "Counter");
        Assert.Equal(RuntimePayloadCaptureMode.None, snapshot.CaptureMode);
        Assert.Null(snapshot.Payload);
    }

    [Fact]
    public async Task Capture_policy_retains_completed_scope_variable_payload()
    {
        await using var provider = NewProvider(["actexec-sequence", "actexec-assign", "actexec-read"], new CapturingPolicy());
        var executable = NewExecutable(
            children:
            [
                AssignNode("node-assign", "Counter", "assigned-value"),
                CaptureNode("node-read", VariableBinding(CounterReferenceKey, SequenceNodeId))
            ],
            variableDefault: "starting");

        await ExecuteAsync(provider, executable);

        var snapshot = await ContainerVariableSnapshot(provider, "actexec-sequence", "Counter");
        Assert.Equal(RuntimePayloadCaptureMode.Payload, snapshot.CaptureMode);
        Assert.Equal("assigned-value", snapshot.Payload?.GetString());
    }

    [Fact]
    public async Task Container_without_scoped_variables_is_not_marked_completed()
    {
        await using var provider = NewProvider(["actexec-sequence", "actexec-read"]);
        var executable = NewExecutable(
            children: [CaptureNode("node-read", LiteralBinding("plain"))],
            variableDefault: null);

        await ExecuteAsync(provider, executable);

        // No scoped variables declared → the completion path leaves the container state untouched.
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        var sequenceState = Assert.Single(states, state => state.Execution.ExecutableNodeId == SequenceNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, sequenceState.Status);
        Assert.DoesNotContain(RuntimeMetadataKeys.ScopedVariableScopeCompleted, sequenceState.Metadata.Keys);

        var projection = await provider.GetRequiredService<IActivityExecutionInspectionStore>().FindAsync("wfexec-1", "actexec-sequence");
        Assert.DoesNotContain(projection?.ValueSnapshots ?? [], s => s.Subject == ActivityExecutionInspectionValueSubject.ContainerVariable);
    }

    private static RuntimeInputBinding LiteralBinding(string value) =>
        new(
            inputName: "Value",
            source: RuntimeInputBindingSource.Literal,
            literalValue: JsonSerializer.SerializeToElement(value),
            metadata: new Dictionary<string, string>
            {
                [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = "System.String",
                ["referenceKey"] = "Value"
            });

    private static async Task<ActivityExecutionInspectionValueSnapshot> ContainerVariableSnapshot(ServiceProvider provider, string activityExecutionId, string variableName)
    {
        var projection = await provider.GetRequiredService<IActivityExecutionInspectionStore>().FindAsync("wfexec-1", activityExecutionId);
        Assert.NotNull(projection);
        return Assert.Single(projection!.ValueSnapshots, s =>
            s.Subject == ActivityExecutionInspectionValueSubject.ContainerVariable &&
            s.Name == variableName);
    }

    // Captures the payload for completed container-scoped variables (the opposite of the default policy).
    private sealed class CapturingPolicy : IRuntimePayloadCapturePolicy
    {
        public RuntimePayloadCaptureDecision Decide(RuntimePayloadCaptureRequest request) =>
            request.Subject == RuntimePayloadCaptureSubject.ContainerVariable
                ? new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.Payload, "Container variables captured for the test.")
                : new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.None, "Other subjects omitted for the test.");
    }

    private static string? CapturedValue(ServiceProvider provider, string activityExecutionId)
    {
        var outputs = provider.GetRequiredService<IRuntimeActivityOutputRegister>()
            .GetActivityOutputs("wfexec-1", activityExecutionId);
        var captured = Assert.Single(outputs);
        return captured.Value.ValueKind == JsonValueKind.String ? captured.Value.GetString() : captured.Value.ToString();
    }

    private ServiceProvider NewProvider(IEnumerable<string> activityExecutionIds, IRuntimePayloadCapturePolicy? capturePolicy = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActivityConstructor, SequenceActivityConstructor>();
        services.AddSingleton<IActivityConstructor, CaptureActivityConstructor>();
        services.AddSingleton<IActivityConstructor, AssignActivityConstructor>();
        services.AddSingleton<IRuntimeExecutionIdGenerator>(new DeterministicRuntimeExecutionIdGenerator(activityExecutionIds));
        if (capturePolicy is not null)
            services.AddSingleton(capturePolicy);

        // Minimal expression evaluation surface so the runtime materializer resolves the Variable
        // expression language through the descriptor registry (the same wiring published apps get).
        services.AddSingleton<IExpressionDescriptorProvider, DefaultExpressionDescriptorProvider>();
        services.AddSingleton<IExpressionDescriptorRegistry>(sp =>
            new ExpressionDescriptorRegistry(sp.GetServices<IExpressionDescriptorProvider>()));
        services.AddSingleton(Options.Create(ExpressionEvaluatorOptions.Empty));
        services.AddScoped<IExpressionEvaluator, ExpressionEvaluator>();

        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        new ActivitiesSequenceFeature().ConfigureServices(services);

        return services.BuildServiceProvider();
    }

    private async Task ExecuteAsync(ServiceProvider provider, WorkflowExecutable executable)
    {
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var agent = await provider.GetRequiredService<IWorkflowExecutionAgentProvider>()
            .GetAgentAsync(NewActivationRequest("wfexec-1"));

        var result = await agent.EnqueueAsync(NewStartEnvelope(executable.Identity));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.Status);
        Assert.Empty(await provider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    private WorkflowExecutable NewExecutable(IReadOnlyCollection<ExecutableNode> children, string? variableDefault)
    {
        var activities = children.Select(child => child.ExecutableNodeId).ToArray();
        object structurePayload = variableDefault is null
            ? new { activities }
            : new
            {
                activities,
                variables = new[] { new VariableDefinition(CounterReferenceKey, "Counter", new TypeReference("String"), null, new ArgumentValue(variableDefault, "Literal")) }
            };
        var root = new ExecutableNode(
            executableNodeId: SequenceNodeId,
            authoredActivityId: "authored-sequence",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: SequenceActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new SequenceDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structurePayload, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        return new WorkflowExecutable(
            identity: NewIdentity(),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: _now,
            publishedAt: _now,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static ExecutableNode CaptureNode(string nodeId, RuntimeInputBinding valueBinding) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/capture",
            activityTypeVersion: "1.0.0",
            descriptorType: CaptureActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new CaptureDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { ["Value"] = valueBinding },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static ExecutableNode AssignNode(string nodeId, string variableName, string value) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/assign",
            activityTypeVersion: "1.0.0",
            descriptorType: AssignActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new AssignDescriptor(variableName, value)),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static RuntimeInputBinding VariableBinding(string referenceKey, string declaringScopeId) =>
        new(
            inputName: "Value",
            source: RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(
                "Variable",
                JsonSerializer.Serialize(new { referenceKey, declaringScopeId }),
                new RuntimeValueTypeDescriptor("clr", "System.String", null)),
            metadata: new Dictionary<string, string>
            {
                [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = "System.String",
                ["referenceKey"] = "Value"
            });

    private WorkflowExecutionAgentActivationRequest NewActivationRequest(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionAgentActivationReason.Start,
            requestedAt: _now,
            requestedBy: "sequence-scope-test",
            requiredCapabilities: WorkflowExecutionAgentCapabilities.InProcessMailbox);

    private WorkflowExecutionCommandEnvelope NewStartEnvelope(WorkflowExecutableIdentity pinnedExecutable)
    {
        var payload = new WorkflowExecutionStartCommandPayload(pinnedExecutable, pinnedExecutable.ArtifactId);
        var command = new WorkflowExecutionCommand(
            CommandId: "command-start",
            WorkflowExecutionId: "wfexec-1",
            Kind: WorkflowExecutionCommandKind.Start,
            EnqueuedAt: _now,
            Payload: JsonSerializer.SerializeToElement(payload),
            Metadata: new Dictionary<string, string>());

        return new WorkflowExecutionCommandEnvelope(
            envelopeId: "envelope-start",
            workflowExecutionId: "wfexec-1",
            command: command,
            idempotencyKey: "wfexec-1:start:artifact-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: _now,
            sequence: 1,
            metadata: new Dictionary<string, string>());
    }

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private sealed class SequenceActivityConstructor : IActivityConstructor<SequenceDescriptor>
    {
        public static string DescriptorTypeKey => typeof(SequenceDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new SequenceActivity());

        public ValueTask<IActivity> Construct(SequenceDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new SequenceActivity());
    }

    private sealed record SequenceDescriptor;

    private sealed class CaptureActivityConstructor : IActivityConstructor<CaptureDescriptor>
    {
        public static string DescriptorTypeKey => typeof(CaptureDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new CaptureActivity(ResolveValueInput(inputs)));

        public ValueTask<IActivity> Construct(CaptureDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new CaptureActivity(ResolveValueInput(inputs)));

        private static InputArgument<string>? ResolveValueInput(IDictionary<string, InputArgument>? inputs) =>
            inputs is not null && inputs.TryGetValue("Value", out var input) ? (InputArgument<string>)input : null;
    }

    private sealed record CaptureDescriptor;

    // Reads its container-scoped "Value" input (resolved through the scope chain) and records it as output.
    private sealed class CaptureActivity(InputArgument<string>? value) : CodeActivity("test/capture")
    {
        protected override void Execute(IActivityExecutionContext context)
        {
            var resolved = context.Get(value);
            context.Set(new OutputArgument<string>(new CapturedMemoryBlockReference()), resolved, "Captured");
            context.SetOutcomes([Workflows.Runtime.Core.Constants.ActivityOutcomes.Done]);
        }
    }

    private sealed class AssignActivityConstructor : IActivityConstructor<AssignDescriptor>
    {
        public static string DescriptorTypeKey => typeof(AssignDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken)
        {
            var descriptor = payload.Deserialize<AssignDescriptor>() ?? throw new InvalidOperationException("Assign descriptor resolved to null.");
            return Construct(descriptor, inputs, outputs, cancellationToken);
        }

        public ValueTask<IActivity> Construct(AssignDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new AssignActivity(descriptor.VariableName, descriptor.Value));
    }

    private sealed record AssignDescriptor(string VariableName, string Value);

    // Assigns a container-scoped variable by name through the visible scope chain.
    private sealed class AssignActivity(string variableName, string value) : CodeActivity("test/assign")
    {
        protected override void Execute(IActivityExecutionContext context)
        {
            context.ExpressionExecutionContext.SetVariable(variableName, value);
            context.SetOutcomes([Workflows.Runtime.Core.Constants.ActivityOutcomes.Done]);
        }
    }

    private sealed class CapturedMemoryBlockReference : IMemoryBlockReference
    {
        public string Id { get; set; } = "Captured";
        public IMemoryBlock Declare() => new CapturedMemoryBlock();
        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context) => context.Get<T>(this);
        public T? Get<T>(IExpressionExecutionContext context) => context.Get<T>(this);
    }

    private sealed class CapturedMemoryBlock : IMemoryBlock
    {
        public object? Value { get; set; }
        public object? Metadata { get; set; }
    }

    private sealed class DeterministicRuntimeExecutionIdGenerator(IEnumerable<string> activityExecutionIds) : IRuntimeExecutionIdGenerator
    {
        private readonly Queue<string> _activityExecutionIds = new(activityExecutionIds);

        public string NewWorkflowExecutionId() => "wfexec-1";
        public string NewWorkflowExecutionCommandId() => "command-generated";
        public string NewWorkflowExecutionCommandEnvelopeId() => "envelope-generated";

        public string NewActivityExecutionId() =>
            _activityExecutionIds.TryDequeue(out var activityExecutionId)
                ? activityExecutionId
                : throw new InvalidOperationException("No deterministic activity execution ID is available.");
    }
}
