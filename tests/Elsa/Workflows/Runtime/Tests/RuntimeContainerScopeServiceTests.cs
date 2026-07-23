using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeContainerScopeServiceTests
{
    private const string WorkflowExecutionId = "wfexec";
    private static readonly ValueTypeDescriptor StringType = new("String");
    private readonly InMemoryActivityExecutionStateStore _activities = new();
    private readonly InMemoryWorkflowExecutionStateStore _workflows = new();

    [Fact]
    public async Task Visible_values_follow_root_iteration_container_lexical_order()
    {
        var factory = new VariableFrameFactory();
        var root = factory.CreateRoot(WorkflowExecutionId, "workflow", Values(("root", "root")));
        await _workflows.SaveAsync(WorkflowState(root));
        var parent = State("parent", "container") with
        {
            IterationId = "0",
            IterationVariableFrame = factory.CreateIteration("loop", "parent", "0", root, Values(("item", "apple")))
        };
        parent = parent with
        {
            VariableFrame = factory.CreateContainer("container", "parent", parent.IterationVariableFrame!, Values(("local", "container")))
        };
        await _activities.SaveAsync(parent);
        var child = State("child", "leaf", parent.InvocationId);

        var visible = await Service().BuildVisibleFramesAsync(WorkflowExecutionId, child);

        Assert.Equal([root.FrameId, parent.IterationVariableFrame!.FrameId, parent.VariableFrame!.FrameId], visible.Frames.Select(frame => frame.FrameId));
        Assert.Equal("apple", visible.Values[new RuntimeVariableValueAddress("loop", "item")].InlineValue!.Value.GetString());
        Assert.Equal("container", visible.Values[new RuntimeVariableValueAddress("container", "local")].InlineValue!.Value.GetString());
    }

    [Fact]
    public async Task Activation_places_body_container_beneath_typed_iteration_frame()
    {
        var root = new VariableFrameFactory().CreateRoot(WorkflowExecutionId, "workflow", Values(("root", "root")));
        await _workflows.SaveAsync(WorkflowState(root));
        var node = RuntimeVariableScopeFactoryTests.Node(("local", "Local", 7));
        var executableRoot = new ExecutableNode(
            "loop",
            "authored-loop",
            "test/root",
            "1.0.0",
            "test/descriptor",
            JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot("children", [node])]);
        var executable = new WorkflowExecutable(
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1.0.0", "hash"),
            executableRoot,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>());
        await _activities.SaveAsync(State("parent", executableRoot.ExecutableNodeId));
        var request = new LoopIterationScopeRequest("loop", "iteration-0", Values(("item", "apple")));
        var state = State("body", node.ExecutableNodeId, "parent") with
        {
            IterationId = "iteration-0",
            Provenance = ActivitySchedulingProvenance.From(
                WorkflowExecutionId,
                "parent",
                "parent",
                branchId: null,
                iterationId: "iteration-0",
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: "test"),
            IterationFrameRequest = request
        };

        var activated = await Service().ActivateOwnedFramesAsync(executable, node, state, state.IterationFrameRequest);

        Assert.Null(state.IterationVariableFrame);
        Assert.NotNull(state.IterationFrameRequest);
        Assert.Equal(root.FrameId, activated.IterationVariableFrame!.ParentFrameId);
        Assert.Equal(activated.IterationVariableFrame.FrameId, activated.VariableFrame!.ParentFrameId);
        Assert.Equal(7, activated.VariableFrame.Values["local"].InlineValue!.Value.GetInt32());
        Assert.Null(activated.IterationFrameRequest);
    }

    [Fact]
    public async Task Root_node_with_declarations_activates_its_own_container_frame()
    {
        // #972: the root node is a normal declaring container — its structure variables live in its OWN
        // container frame (scope id = its node id), parented on the workflow root frame; they are no longer
        // folded into the "workflow" scope.
        var root = new VariableFrameFactory().CreateRoot(WorkflowExecutionId, "workflow", Values(("wf", "workflow-value")));
        await _workflows.SaveAsync(WorkflowState(root));
        var rootNode = DeclNode("root", [], ("local", "Local"));
        var executable = Executable(rootNode, Declaration("wf", "WorkflowVar"));
        var state = State("root-exec", "root");

        var activated = await Service().ActivateOwnedFramesAsync(executable, rootNode, state, iterationRequest: null);

        Assert.NotNull(activated.VariableFrame);
        Assert.Equal("root", activated.VariableFrame!.ScopeId);
        Assert.Equal(VariableFrameKind.Container, activated.VariableFrame.Kind);
        Assert.Equal(root.FrameId, activated.VariableFrame.ParentFrameId);
        Assert.Equal("seed", activated.VariableFrame.Values["local"].InlineValue!.Value.GetString());
    }

    [Fact]
    public async Task Visible_frame_recovery_rejects_missing_ancestor()
    {
        await _workflows.SaveAsync(WorkflowState(new VariableFrameFactory().CreateRoot(WorkflowExecutionId, "workflow", Values(("root", "root")))));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().BuildVisibleFramesAsync(WorkflowExecutionId, State("child", "leaf", "missing")).AsTask());

        Assert.Contains("missing ancestor", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Visible_frame_recovery_rejects_cyclic_ancestor_chain()
    {
        await _workflows.SaveAsync(WorkflowState(new VariableFrameFactory().CreateRoot(WorkflowExecutionId, "workflow", Values(("root", "root")))));
        await _activities.SaveAsync(State("a", "a", "b"));
        await _activities.SaveAsync(State("b", "b", "a"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().BuildVisibleFramesAsync(WorkflowExecutionId, State("child", "leaf", "a")).AsTask());

        Assert.Contains("cyclic parent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Visible_frame_recovery_rejects_detached_container_frame()
    {
        var factory = new VariableFrameFactory();
        var root = factory.CreateRoot(WorkflowExecutionId, "workflow", Values(("root", "root")));
        await _workflows.SaveAsync(WorkflowState(root));
        var detachedParent = factory.CreateRoot("other-workflow", "workflow", Values(("other", "other")));
        var parent = State("parent", "container") with
        {
            VariableFrame = factory.CreateContainer("container", "parent", detachedParent, Values(("local", "value")))
        };
        await _activities.SaveAsync(parent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().BuildVisibleFramesAsync(WorkflowExecutionId, State("child", "leaf", "parent")).AsTask());

        Assert.Contains("visible lexical parent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Activation_rejects_iteration_frame_from_non_owner_parent()
    {
        var (executable, body) = await PrepareIterationActivationAsync("actual-loop");
        var request = new LoopIterationScopeRequest("other-loop", "iteration-0", Values(("item", "apple")));
        var state = IterationState(body.ExecutableNodeId, request);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().ActivateOwnedFramesAsync(executable, body, state, request).AsTask());

        Assert.Contains("not owned", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Activation_rejects_transient_iteration_value()
    {
        var (executable, body) = await PrepareIterationActivationAsync("loop");
        var transient = ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement("apple"), ValueProtectionPolicy.Transient);
        var request = new LoopIterationScopeRequest("loop", "iteration-0", new Dictionary<string, ValueEnvelope> { ["item"] = transient });
        var state = IterationState(body.ExecutableNodeId, request);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().ActivateOwnedFramesAsync(executable, body, state, request).AsTask());

        Assert.Contains("transient value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Activation_rejects_partially_persisted_iteration_frame()
    {
        var (executable, body) = await PrepareIterationActivationAsync("loop");
        var request = new LoopIterationScopeRequest("loop", "iteration-0", Values(("item", "apple")));
        var state = IterationState(body.ExecutableNodeId, request);
        var root = (await _workflows.FindAsync(WorkflowExecutionId))!.RootVariableFrame!;
        state = state with { IterationVariableFrame = new RuntimeLoopIterationFrameFactory().Create(request, state.InvocationId, root) };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().ActivateOwnedFramesAsync(executable, body, state, request).AsTask());

        Assert.Contains("partially activated iteration-frame", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Closing_owner_frames_is_atomic_and_idempotent()
    {
        var factory = new VariableFrameFactory();
        var root = factory.CreateRoot(WorkflowExecutionId, "workflow", Values(("root", "root")));
        var state = State("container", "container") with
        {
            VariableFrame = factory.CreateContainer("container", "container", root, Values(("local", "value")))
        };

        var closed = RuntimeContainerScopeService.CloseOwnedFrames(state);
        var closedAgain = RuntimeContainerScopeService.CloseOwnedFrames(closed);

        Assert.Equal(VariableFrameStatus.Closed, closed.VariableFrame!.Status);
        Assert.Equal(closed.VariableFrame, closedAgain.VariableFrame);
    }

    // ---- spec 123: runtime scoped-variable read seam ----

    [Fact]
    public void Scoped_variable_envelopes_project_by_name_innermost_scope_wins()
    {
        var factory = new VariableFrameFactory();
        var root = factory.CreateRoot(WorkflowExecutionId, "workflow", Values(("g", "root-value")));
        var iteration = factory.CreateIteration("loop", "parent", "0", root, Values(("item", "apple")));
        var container = factory.CreateContainer("container", "parent", iteration, Values(("g2", "container-value")));
        var containerNode = DeclNode("container", [], ("g2", "Shadowed"));
        var rootNode = DeclNode("root", [new ExecutableChildSlot("children", [containerNode])]);

        var envelopes = Service().ProjectVisibleVariableEnvelopes(
            Executable(rootNode, Declaration("g", "Shadowed")),
            new RuntimeVisibleVariableFrames([root, iteration, container]));

        // Root, iteration, and container are all visible; the shadowed name resolves to the innermost (container) value.
        Assert.Equal("apple", envelopes["item"].InlineValue!.Value.GetString());
        Assert.Equal("container-value", envelopes["Shadowed"].InlineValue!.Value.GetString());
    }

    [Fact]
    public async Task Scoped_variable_reader_projection_is_marker_gated()
    {
        var root = new VariableFrameFactory().CreateRoot(WorkflowExecutionId, "workflow", Values(("g", "hello")));
        await _workflows.SaveAsync(WorkflowState(root));
        var rootNode = DeclNode("root", []);
        var executable = Executable(rootNode, Declaration("g", "Greeting"));
        var state = State("marker", "root");

        var populated = await Service().ProjectScopedVariablesForReaderAsync(new MarkerReaderActivity(), executable, state);
        var unpopulated = await Service().ProjectScopedVariablesForReaderAsync(new PlainActivity(), executable, state);

        Assert.Null(unpopulated);
        Assert.NotNull(populated);
        Assert.Equal("hello", populated!["Greeting"].InlineValue!.Value.GetString());
    }

    [Fact]
    public async Task Scoped_variable_reader_sees_committed_container_write_on_a_later_evaluation()
    {
        // A value committed into the reader's OWN container frame (as a mid-run intrinsic write would leave it) is
        // visible on the next evaluation's projection through the own-container inclusion.
        var factory = new VariableFrameFactory();
        var root = factory.CreateRoot(WorkflowExecutionId, "workflow", Values(("r", "root-value")));
        await _workflows.SaveAsync(WorkflowState(root));
        var readerNode = DeclNode("reader", [], ("c", "Counter"));
        var rootNode = DeclNode("root", [new ExecutableChildSlot("children", [readerNode])]);
        var executable = Executable(rootNode, Declaration("r", "Root"));
        await _activities.SaveAsync(State("root-exec", "root"));
        var reader = State("reader-exec", "reader", "root-exec") with
        {
            VariableFrame = factory.CreateContainer("reader", "reader-exec", root, Values(("c", "committed-value")))
        };
        await _activities.SaveAsync(reader);

        var populated = await Service().ProjectScopedVariablesForReaderAsync(new MarkerReaderActivity(), executable, reader);

        Assert.NotNull(populated);
        Assert.Equal("committed-value", populated!["Counter"].InlineValue!.Value.GetString());
        Assert.Equal("root-value", populated["Root"]!.InlineValue!.Value.GetString());
    }

    private static WorkflowExecutable Executable(ExecutableNode rootNode, params RuntimeVariableDeclaration[] workflowVariables) =>
        new(
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1.0.0", "hash"),
            rootNode,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            runtimeRequirements: null,
            storageDriverRequirements: null,
            checkpointCadence: null,
            workflowVariables: workflowVariables);

    private static ExecutableNode DeclNode(string id, IReadOnlyCollection<ExecutableChildSlot> childSlots, params (string Key, string Name)[] variables) =>
        new(
            executableNodeId: id,
            authoredActivityId: $"authored-{id}",
            activityType: "test/container",
            activityTypeVersion: "1.0.0",
            descriptorType: "test/descriptor",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: new ExecutableActivityStructure(
                "test.structure",
                "1.0.0",
                JsonSerializer.SerializeToElement(
                    new { variables = variables.Select(item => Declaration(item.Key, item.Name)) },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    private static RuntimeVariableDeclaration Declaration(string key, string name) =>
        new(key, name, StringType, ValueProtectionPolicy.InstanceInline,
            new RuntimeInputBinding(key, StringType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement("seed"), ValueProtectionPolicy.InstanceInline)));

    private sealed class MarkerReaderActivity : IActivity, IRuntimeScopedVariableReader
    {
        public ValueTask<ActivityTransition> ExecuteAsync(ActivityExecutionContext context) => throw new NotSupportedException();
    }

    private sealed class PlainActivity : IActivity
    {
        public ValueTask<ActivityTransition> ExecuteAsync(ActivityExecutionContext context) => throw new NotSupportedException();
    }

    private RuntimeContainerScopeService Service() => new(_activities, _workflows);

    private async Task<(WorkflowExecutable Executable, ExecutableNode Body)> PrepareIterationActivationAsync(string parentNodeId)
    {
        var rootFrame = new VariableFrameFactory().CreateRoot(WorkflowExecutionId, "workflow", Values(("root", "root")));
        await _workflows.SaveAsync(WorkflowState(rootFrame));
        var body = RuntimeVariableScopeFactoryTests.Node();
        var parent = new ExecutableNode(
            parentNodeId,
            $"authored-{parentNodeId}",
            "test/loop",
            "1.0.0",
            "test/descriptor",
            JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot("body", [body])]);
        var executable = new WorkflowExecutable(
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1.0.0", "hash"),
            parent,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>());
        await _activities.SaveAsync(State("parent", parentNodeId));
        return (executable, body);
    }

    private static ActivityExecutionState IterationState(string nodeId, LoopIterationScopeRequest request) =>
        State("body", nodeId, "parent") with
        {
            IterationId = request.IterationId,
            Provenance = ActivitySchedulingProvenance.From(
                WorkflowExecutionId,
                "parent",
                "parent",
                branchId: null,
                iterationId: request.IterationId,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: "test"),
            IterationFrameRequest = request
        };

    private static WorkflowExecutionState WorkflowState(VariableFrameState root) =>
        new(WorkflowExecutionId, new WorkflowExecutableIdentity("artifact", "definition", "version", "1.0.0", "hash"), WorkflowExecutionStatus.Running, null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null, null, null, new Dictionary<string, string>())
        { RootVariableFrame = root };

    private static ActivityExecutionState State(string id, string nodeId, string? parentId = null) =>
        new(
            new ActivityExecution(id, WorkflowExecutionId, nodeId, $"authored-{nodeId}", "test/activity", "1.0.0"),
            ActivityExecutionStatus.Running,
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            parentId,
            parentId,
            null,
            null,
            null,
            [],
            [],
            0,
            0,
            new Dictionary<string, string>());

    private static IReadOnlyDictionary<string, ValueEnvelope> Values(params (string Key, string Value)[] values) =>
        values.ToDictionary(item => item.Key, item =>
            ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(item.Value), ValueProtectionPolicy.InstanceInline), StringComparer.Ordinal);
}
