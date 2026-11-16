using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>
/// Materializes an activity invocation's input snapshot from the current committed runtime state: durable
/// values, the runtime view, and the visible variable frames of the activity's lexical ancestor chain. This
/// is the single source of truth for input-snapshot materialization — the start handler uses it to pin the
/// snapshot at the Scheduled→Running transition, and the parent-completion evaluation uses it to transiently
/// re-materialize an <see cref="IRuntimeRematerializeInputsOnChildCompletion"/> composite's inputs against
/// live frames (issue #977) without touching the pinned snapshot.
/// </summary>
public sealed class RuntimeActivityInputSnapshotMaterializer(
    IActivityExecutionStateStore activityExecutionStateStore,
    IDurableValueStateStore? durableValueStateStore = null,
    IWorkflowExecutionStateStore? workflowExecutionStateStore = null)
{
    public async ValueTask<ActivityInputSnapshot> MaterializeAsync(
        WorkflowExecutable executable,
        ExecutableNode executableNode,
        ActivityExecutionState state,
        IServiceProvider serviceProvider,
        DateTimeOffset materializedAt,
        CancellationToken cancellationToken)
    {
        var inputMaterializer = serviceProvider.GetService<IRuntimeActivityInputMaterializer>()
            ?? throw new InvalidOperationException($"Typed activity invocation '{state.InvocationId}' requires an input snapshot materializer.");
        cancellationToken.ThrowIfCancellationRequested();
        // The built-in literal and secret-reference paths need no execution state. Secret values
        // remain withheld here and are resolved only at activation. Custom implementations keep
        // their complete context, including custom binding resolvers inside the default materializer.
        var defaultMaterializer = inputMaterializer as RuntimeActivityInputMaterializer;
        if (defaultMaterializer is not null &&
            ((executableNode.ActivityContract?.Inputs.Count == 0 && executableNode.InputBindings.Count == 0) ||
             defaultMaterializer.UsesExecutionIndependentBindings(executableNode)))
            return await inputMaterializer.MaterializeSnapshotAsync(
                executableNode,
                state.InvocationId,
                new RuntimeInputBindingResolutionContext(state.Execution.WorkflowExecutionId, state.InvocationId, state, executable: executable),
                materializedAt,
                cancellationToken);

        var resolvedDurableValueStateStore = durableValueStateStore
            ?? throw new InvalidOperationException($"Typed activity invocation '{state.InvocationId}' requires a durable value state store.");
        var durableValues = await resolvedDurableValueStateStore.ListAllDurableValueStatesAsync(state.Execution.WorkflowExecutionId, cancellationToken);
        // Request and variable bindings still walk the lexical ancestor chain below, including closed
        // scope boundaries, but do not resolve causal activity results from the complete history.
        IReadOnlyCollection<ActivityExecutionState> runtimeView = defaultMaterializer?.RequiresActivityHistory(executableNode) == false
            ? []
            : await activityExecutionStateStore.ListAllAsync(state.Execution.WorkflowExecutionId, cancellationToken);
        var projections = RuntimeInputBindingStateProjection.ProjectAll(durableValues);
        var resolvedWorkflowExecutionStateStore = workflowExecutionStateStore
            ?? serviceProvider.GetService<IWorkflowExecutionStateStore>()
            ?? throw new InvalidOperationException($"Typed activity invocation '{state.InvocationId}' requires the canonical workflow variable-frame owner store.");
        var visibleFrames = await new RuntimeContainerScopeService(activityExecutionStateStore, resolvedWorkflowExecutionStateStore)
            .BuildVisibleFramesAsync(state.Execution.WorkflowExecutionId, state, cancellationToken: cancellationToken, durableValues: durableValues);
        // Below a closed activity execution scope the request inputs are the boundary's own.
        var workflowInputEnvelopes = visibleFrames.ClosedScopeBoundary is { } boundary
            ? RuntimeClosedScope.InputEnvelopes(boundary, durableValues, executable)
            : projections.WorkflowInputEnvelopes;
        var resolutionContext = new RuntimeInputBindingResolutionContext(
            workflowExecutionId: state.Execution.WorkflowExecutionId,
            activityExecutionId: state.InvocationId,
            consumerInvocation: state,
            runtimeView: runtimeView,
            executable: executable,
            workflowInputEnvelopes: workflowInputEnvelopes,
            variableEnvelopes: visibleFrames.Values,
            // Issue #984: expose the lexically visible variables to JavaScript value expressions
            // (variables.X / getVariable('X') / get<Name>()) through the isolated engine's ambient read surface.
            visibleVariablesByName: RuntimeContainerScopeService.ProjectAmbientVisibleVariableEnvelopes(executable, visibleFrames.Frames, visibleFrames.RootVariableNames),
            correlationId: projections.CorrelationId);

        return await inputMaterializer.MaterializeSnapshotAsync(
            executableNode,
            state.InvocationId,
            resolutionContext,
            materializedAt,
            cancellationToken);
    }
}
