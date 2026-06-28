using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Builds and persists the live container-scoped variable state for one activity execution (ADR 0027,
/// #210). It walks the activity's ancestor container executions, projects each container's declared
/// variables from its compiled executable structure, and restores its current values from that
/// container execution's persisted snapshot — the container's <see cref="ActivityExecutionState"/>
/// metadata is the single source of truth for its values, so reads and writes never diverge across a
/// second store. The resulting <see cref="VariableScope"/> chain (outermost container first) is
/// threaded into input-expression evaluation and activity execution; mutations are written back to
/// the owning container execution's snapshot.
/// </summary>
/// <remarks>
/// The chain carries only container scopes (reference-key addressed). The workflow scope is kept out
/// of the chain deliberately: the runtime already owns one workflow-variable store, so adding a
/// second copy here would be the divergence the consistency requirement forbids. Workflow-scope
/// references fall through to the existing context variable accessors (the variable handler resolves
/// them when the scope chain returns no match).
/// </remarks>
public sealed class RuntimeContainerScopeService(IActivityExecutionStateStore activityExecutionStateStore)
{
    private readonly RuntimeVariableScopeFactory _scopeFactory = new();

    /// <summary>
    /// Builds the innermost visible <see cref="VariableScope"/> for <paramref name="activityState"/>
    /// by walking its ancestor container executions (nearest first), or <c>null</c> when no enclosing
    /// container declares variables.
    /// </summary>
    public async ValueTask<VariableScope?> BuildScopeAsync(
        WorkflowExecutable executable,
        string workflowExecutionId,
        ActivityExecutionState activityState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(activityState);

        var containerLayers = new List<RuntimeContainerScopeLayer>();
        var ancestorId = activityState.ParentActivityExecutionId;
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (!string.IsNullOrEmpty(ancestorId) && visited.Add(ancestorId))
        {
            var ancestorState = await activityExecutionStateStore.FindAsync(workflowExecutionId, ancestorId, cancellationToken);
            if (ancestorState is null)
                break;

            if (executable.NodesById.TryGetValue(ancestorState.Execution.ExecutableNodeId, out var ancestorNode))
            {
                var declared = _scopeFactory.ProjectDeclaredVariables(ancestorNode);
                if (declared.Count > 0)
                {
                    containerLayers.Add(new RuntimeContainerScopeLayer(
                        ScopeId: ancestorState.Execution.ExecutableNodeId,
                        ExecutionId: ancestorState.Execution.ActivityExecutionId,
                        Variables: declared,
                        Values: ReadSnapshot(ancestorState)));
                }
            }

            ancestorId = ancestorState.ParentActivityExecutionId;
        }

        if (containerLayers.Count == 0)
            return null;

        // Ancestors were collected nearest-first; the chain must be assembled outermost-first.
        containerLayers.Reverse();
        return _scopeFactory.BuildChain(containerLayers);
    }

    /// <summary>
    /// Writes back every container scope in <paramref name="scope"/>'s visible chain whose live
    /// values now differ from the persisted snapshot of its owning container execution, so sibling
    /// branches and later activities observe assignments and resume restores them. No-op when the
    /// chain is null. Returns the number of container executions whose snapshot was updated.
    /// </summary>
    public async ValueTask<int> PersistScopeMutationsAsync(
        VariableScope? scope,
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        var updated = 0;

        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.ExecutionId is not { } executionId)
                continue;

            var containerState = await activityExecutionStateStore.FindAsync(workflowExecutionId, executionId, cancellationToken);
            if (containerState is null)
                continue;

            var mutated = CaptureScopeMutation(containerState, current);
            if (mutated is null)
                continue;

            await activityExecutionStateStore.SaveAsync(mutated, cancellationToken);
            updated++;
        }

        return updated;
    }

    /// <summary>
    /// Returns <paramref name="containerState"/> with its persisted scope-value snapshot refreshed
    /// from <paramref name="scope"/> when the live values differ, or <c>null</c> when nothing changed.
    /// </summary>
    public static ActivityExecutionState? CaptureScopeMutation(ActivityExecutionState containerState, VariableScope scope)
    {
        ArgumentNullException.ThrowIfNull(containerState);
        ArgumentNullException.ThrowIfNull(scope);

        var snapshot = scope.SnapshotValues();
        var serialized = JsonSerializer.Serialize(snapshot);

        if (containerState.Metadata.TryGetValue(RuntimeMetadataKeys.ScopedVariableValues, out var existing) &&
            StringComparer.Ordinal.Equals(existing, serialized))
            return null;

        var metadata = containerState.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.ScopedVariableValues] = serialized;
        return containerState with { Metadata = metadata };
    }

    private static IReadOnlyDictionary<string, object?> ReadSnapshot(ActivityExecutionState containerState)
    {
        if (!containerState.Metadata.TryGetValue(RuntimeMetadataKeys.ScopedVariableValues, out var serialized) ||
            string.IsNullOrWhiteSpace(serialized))
            return EmptyValues;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(serialized) ?? EmptyValues;
        }
        catch (JsonException)
        {
            return EmptyValues;
        }
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyValues = new Dictionary<string, object?>(StringComparer.Ordinal);
}
