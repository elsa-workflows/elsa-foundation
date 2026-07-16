using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Resolves and activates the durable lexical frame chain for structured runtime activities.
/// The historical class name is retained temporarily for handler compatibility; its model is now
/// exclusively <see cref="VariableFrameState"/>/<see cref="ValueEnvelope"/>.
/// </summary>
public sealed class RuntimeContainerScopeService(
    IActivityExecutionStateStore activityExecutionStateStore,
    IWorkflowExecutionStateStore workflowExecutionStateStore)
{
    private readonly RuntimeVariableDeclarationProjector _declarations = new();
    private readonly RuntimeLoopIterationFrameFactory _iterations = new();
    private readonly VariableFrameFactory _frames = new();

    public async ValueTask<RuntimeVisibleVariableFrames> BuildVisibleFramesAsync(
        string workflowExecutionId,
        ActivityExecutionState activityState,
        bool includeCurrentIteration = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(activityState);

        var workflowState = await workflowExecutionStateStore.FindAsync(workflowExecutionId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow execution '{workflowExecutionId}' has no durable variable-frame owner state.");
        var root = workflowState.RootVariableFrame
            ?? throw new InvalidOperationException($"Workflow execution '{workflowExecutionId}' has no canonical root variable frame.");
        if (root.Status != VariableFrameStatus.Active ||
            root.Kind != VariableFrameKind.Root ||
            root.ParentFrameId is not null ||
            !StringComparer.Ordinal.Equals(root.ScopeId, Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId) ||
            !StringComparer.Ordinal.Equals(root.ActivationId, workflowExecutionId))
        {
            throw new InvalidOperationException($"Workflow execution '{workflowExecutionId}' has an invalid canonical root variable frame while activity '{activityState.InvocationId}' is still executing.");
        }
        var ordered = new List<VariableFrameState> { root };

        var ancestors = new Stack<ActivityExecutionState>();
        var ancestorId = activityState.ParentActivityExecutionId;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (!string.IsNullOrWhiteSpace(ancestorId))
        {
            if (!visited.Add(ancestorId))
                throw new InvalidOperationException($"Activity '{activityState.InvocationId}' has a cyclic parent execution chain at '{ancestorId}'.");
            var ancestor = await activityExecutionStateStore.FindAsync(workflowExecutionId, ancestorId, cancellationToken);
            if (ancestor is null)
                throw new InvalidOperationException($"Activity '{activityState.InvocationId}' references missing ancestor execution '{ancestorId}'.");
            ancestors.Push(ancestor);
            ancestorId = ancestor.ParentActivityExecutionId;
        }

        foreach (var ancestor in ancestors)
            AddOwnedFrames(ancestor, ordered);

        if (includeCurrentIteration && activityState.IterationVariableFrame is { } iteration)
            AddFrame(iteration, VariableFrameKind.Iteration, IterationActivationId(activityState), ordered);

        return new RuntimeVisibleVariableFrames(ordered);
    }

    public async ValueTask<ActivityExecutionState> ActivateOwnedFramesAsync(
        WorkflowExecutable executable,
        ExecutableNode node,
        ActivityExecutionState activityState,
        LoopIterationScopeRequest? iterationRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(activityState);

        var declarations = _declarations.ProjectInitialValues(node);
        var isWorkflowRoot = StringComparer.Ordinal.Equals(node.ExecutableNodeId, executable.RootActivity.ExecutableNodeId);
        var ownsContainer = !isWorkflowRoot && declarations.Count > 0;
        var pendingIteration = ResolveIterationRequest(activityState, iterationRequest);
        if (activityState.VariableFrame is not null || activityState.IterationVariableFrame is not null)
        {
            if (pendingIteration is not null)
                throw new InvalidOperationException($"Activity '{activityState.InvocationId}' has partially activated iteration-frame state.");
            if (ownsContainer != (activityState.VariableFrame is not null))
                throw new InvalidOperationException($"Activity '{activityState.InvocationId}' has partially activated container-frame state.");

            var existingVisible = await BuildVisibleFramesAsync(
                activityState.Execution.WorkflowExecutionId,
                activityState,
                includeCurrentIteration: false,
                cancellationToken);
            var existingParent = existingVisible.Frames[^1];
            if (activityState.IterationVariableFrame is { } existingIteration)
            {
                var iterationId = activityState.IterationId
                    ?? throw new InvalidOperationException($"Activity '{activityState.InvocationId}' owns an iteration frame without an iteration identity.");
                ValidateFrame(
                    existingIteration,
                    VariableFrameKind.Iteration,
                    existingIteration.ScopeId,
                    $"{activityState.InvocationId}:iteration:{iterationId}",
                    existingParent.FrameId);
                await ValidateIterationOwnershipAsync(executable, activityState, existingIteration.ScopeId, iterationId, cancellationToken);
                existingParent = existingIteration;
            }
            if (activityState.VariableFrame is { } existingContainer)
                ValidateFrame(existingContainer, VariableFrameKind.Container, node.ExecutableNodeId, activityState.InvocationId, existingParent.FrameId);
            return activityState;
        }

        var visible = await BuildVisibleFramesAsync(
            activityState.Execution.WorkflowExecutionId,
            activityState,
            includeCurrentIteration: false,
            cancellationToken);
        var parent = visible.Frames.LastOrDefault()
            ?? throw new InvalidOperationException($"Activity '{activityState.InvocationId}' cannot activate a lexical frame without an active workflow root frame.");

        VariableFrameState? iteration = null;
        if (pendingIteration is not null)
        {
            await ValidateIterationOwnershipAsync(executable, activityState, pendingIteration.OwnerNodeId, pendingIteration.IterationId, cancellationToken);
            if (pendingIteration.Values.Values.Any(value => value.Policy.Lifecycle == DurableValueLifecycle.None))
                throw new InvalidOperationException($"Iteration frame '{pendingIteration.IterationId}' contains a transient value that cannot cross the durable scheduling boundary.");
            iteration = _iterations.Create(pendingIteration, activityState.InvocationId, parent);
            parent = iteration;
        }

        VariableFrameState? container = null;
        if (ownsContainer)
            container = _frames.CreateContainer(node.ExecutableNodeId, activityState.InvocationId, parent, declarations);

        return activityState with
        {
            IterationVariableFrame = iteration,
            VariableFrame = container,
            IterationFrameRequest = null
        };
    }

    private async ValueTask ValidateIterationOwnershipAsync(
        WorkflowExecutable executable,
        ActivityExecutionState activityState,
        string ownerNodeId,
        string iterationId,
        CancellationToken cancellationToken)
    {
        if (!StringComparer.Ordinal.Equals(iterationId, activityState.IterationId))
            throw new InvalidOperationException($"Iteration frame '{iterationId}' does not match activity iteration '{activityState.IterationId}'.");
        if (!StringComparer.Ordinal.Equals(iterationId, activityState.Provenance.IterationId))
            throw new InvalidOperationException($"Iteration frame '{iterationId}' does not match activity provenance iteration '{activityState.Provenance.IterationId}'.");
        var parentExecutionId = activityState.ParentActivityExecutionId
            ?? throw new InvalidOperationException($"Iteration activity '{activityState.InvocationId}' has no scheduling parent.");
        var parentState = await activityExecutionStateStore.FindAsync(activityState.Execution.WorkflowExecutionId, parentExecutionId, cancellationToken)
            ?? throw new InvalidOperationException($"Iteration activity '{activityState.InvocationId}' references missing scheduling parent '{parentExecutionId}'.");
        if (!StringComparer.Ordinal.Equals(activityState.SchedulingActivityExecutionId, parentExecutionId) ||
            !StringComparer.Ordinal.Equals(parentState.Execution.ExecutableNodeId, ownerNodeId) ||
            !executable.NodesById.ContainsKey(ownerNodeId))
        {
            throw new InvalidOperationException($"Iteration frame '{iterationId}' is not owned by the scheduling lexical parent of activity '{activityState.InvocationId}'.");
        }
    }

    private static LoopIterationScopeRequest? ResolveIterationRequest(
        ActivityExecutionState state,
        LoopIterationScopeRequest? supplied)
    {
        var persisted = state.IterationFrameRequest;
        if (persisted is null || supplied is null)
            return persisted ?? supplied;
        if (!StringComparer.Ordinal.Equals(persisted.OwnerNodeId, supplied.OwnerNodeId) ||
            !StringComparer.Ordinal.Equals(persisted.IterationId, supplied.IterationId) ||
            persisted.Values.Count != supplied.Values.Count ||
            persisted.Values.Any(item => !supplied.Values.TryGetValue(item.Key, out var value) || value != item.Value))
        {
            throw new InvalidOperationException($"Activity '{state.InvocationId}' carries conflicting iteration-frame requests.");
        }
        return persisted;
    }

    private static void ValidateFrame(
        VariableFrameState frame,
        VariableFrameKind kind,
        string scopeId,
        string activationId,
        string parentFrameId)
    {
        if (frame.Status != VariableFrameStatus.Active ||
            frame.Kind != kind ||
            !StringComparer.Ordinal.Equals(frame.ScopeId, scopeId) ||
            !StringComparer.Ordinal.Equals(frame.ActivationId, activationId) ||
            !StringComparer.Ordinal.Equals(frame.ParentFrameId, parentFrameId))
        {
            throw new InvalidOperationException($"Variable frame '{frame.FrameId}' does not match its persisted lexical owner or parent.");
        }
    }

    public static ActivityExecutionState CloseOwnedFrames(ActivityExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state with
        {
            VariableFrame = Close(state.VariableFrame),
            IterationVariableFrame = Close(state.IterationVariableFrame)
        };
    }

    public static WorkflowExecutionState CloseRootFrame(WorkflowExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state with { RootVariableFrame = Close(state.RootVariableFrame) };
    }

    public IReadOnlyList<RuntimeScopedVariableValue> ReadScopeVariableValues(
        ExecutableNode containerNode,
        ActivityExecutionState containerState)
    {
        ArgumentNullException.ThrowIfNull(containerNode);
        ArgumentNullException.ThrowIfNull(containerState);
        if (containerState.VariableFrame is not { } frame)
            return [];

        var declarations = _declarations.ProjectDeclarations(containerNode);
        return declarations.Select(item => new RuntimeScopedVariableValue(
                item.Value.Name,
                item.Key,
                frame.Values.TryGetValue(item.Key, out var value) ? Materialize(value) : null))
            .ToArray();
    }

    /// <summary>
    /// Projects the visible lexical frame chain into the name-keyed read view used by transitional
    /// execution-context accessors. Values always come from canonical frames; durable-value rows are
    /// never consulted as a second variable store.
    /// </summary>
    public IReadOnlyDictionary<string, object?> ProjectVisibleVariables(
        WorkflowExecutable executable,
        RuntimeVisibleVariableFrames visibleFrames)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(visibleFrames);

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var frame in visibleFrames.Frames)
        {
            if (frame.Kind == VariableFrameKind.Iteration)
            {
                foreach (var (key, value) in frame.Values)
                    result[key] = Materialize(value);
                continue;
            }

            var owner = frame.Kind == VariableFrameKind.Root
                ? executable.RootActivity
                : executable.NodesById.TryGetValue(frame.ScopeId, out var node)
                    ? node
                    : throw new InvalidOperationException($"Variable frame '{frame.FrameId}' references missing executable scope '{frame.ScopeId}'.");
            var declarations = _declarations.ProjectDeclarations(owner);
            foreach (var (key, value) in frame.Values)
            {
                if (!declarations.TryGetValue(key, out var declaration))
                    throw new InvalidOperationException($"Variable frame '{frame.FrameId}' contains undeclared variable key '{key}'.");
                result[declaration.Name] = Materialize(value);
            }
        }

        return result;
    }

    private static object? Materialize(ValueEnvelope envelope) => envelope.Presence switch
    {
        ValuePresence.Absent or ValuePresence.ExplicitNull => null,
        ValuePresence.Present when envelope.InlineValue is { } inline => inline.Clone(),
        _ => envelope.ExternalReference
    };

    private static VariableFrameState? Close(VariableFrameState? frame) =>
        frame is { Status: VariableFrameStatus.Active }
            ? frame.Close(frame.Revision)
            : frame;

    private static void AddOwnedFrames(ActivityExecutionState state, List<VariableFrameState> frames)
    {
        if (state.IterationVariableFrame is { } iteration)
            AddFrame(iteration, VariableFrameKind.Iteration, IterationActivationId(state), frames);
        if (state.VariableFrame is { } container)
            AddFrame(container, VariableFrameKind.Container, state.InvocationId, frames);
    }

    private static string IterationActivationId(ActivityExecutionState state)
    {
        if (string.IsNullOrWhiteSpace(state.IterationId))
            throw new InvalidOperationException($"Activity '{state.InvocationId}' owns an iteration frame without an iteration identity.");
        return $"{state.InvocationId}:iteration:{state.IterationId}";
    }

    private static void AddFrame(
        VariableFrameState frame,
        VariableFrameKind expectedKind,
        string expectedActivationId,
        List<VariableFrameState> frames)
    {
        if (frame.Status != VariableFrameStatus.Active)
            throw new InvalidOperationException($"Closed variable frame '{frame.FrameId}' is present in the visible execution chain.");
        if (frame.Kind != expectedKind || !StringComparer.Ordinal.Equals(frame.ActivationId, expectedActivationId))
            throw new InvalidOperationException($"Variable frame '{frame.FrameId}' does not match its owning activity activation '{expectedActivationId}'.");
        var expectedParentId = frames.Last().FrameId;
        if (!StringComparer.Ordinal.Equals(frame.ParentFrameId, expectedParentId))
            throw new InvalidOperationException($"Variable frame '{frame.FrameId}' expects parent '{frame.ParentFrameId}', but the visible lexical parent is '{expectedParentId}'.");
        frames.Add(frame);
    }
}

public sealed class RuntimeVisibleVariableFrames
{
    public RuntimeVisibleVariableFrames(IReadOnlyCollection<VariableFrameState> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        Frames = frames.ToArray();
        if (Frames.Count == 0 || Frames[0].Kind != VariableFrameKind.Root || Frames[0].ParentFrameId is not null)
            throw new InvalidOperationException("A visible variable-frame chain must start with a root frame.");
        for (var index = 1; index < Frames.Count; index++)
        {
            if (!StringComparer.Ordinal.Equals(Frames[index].ParentFrameId, Frames[index - 1].FrameId))
                throw new InvalidOperationException($"Variable frame '{Frames[index].FrameId}' is detached from visible parent '{Frames[index - 1].FrameId}'.");
        }
        var values = new Dictionary<RuntimeVariableValueAddress, ValueEnvelope>();
        foreach (var frame in Frames)
        foreach (var (key, value) in frame.Values)
            values[new RuntimeVariableValueAddress(frame.ScopeId, key)] = value;
        Values = values;
    }

    public IReadOnlyList<VariableFrameState> Frames { get; }
    public IReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope> Values { get; }
}
