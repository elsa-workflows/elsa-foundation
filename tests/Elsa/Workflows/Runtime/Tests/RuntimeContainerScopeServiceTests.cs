using System.Text.Json;
using Elsa.Primitives.Models;
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
