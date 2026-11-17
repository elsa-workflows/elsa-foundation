using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services.StepVariables;

/// <summary>
/// Creates the variable session of a step (spec 1231 T007): one per evaluation of a leaf activity's body, on invoke and
/// on every resume, and a fresh one on every new attempt, so session reads are never carried across (ADR 0092
/// decision 11). Structural activities get none (Q4).
/// </summary>
internal static class StepVariableSessions
{
    public static async ValueTask<StepVariableSession?> CreateAsync(
        IServiceProvider services,
        RuntimeContainerScopeService scopeService,
        IActivity activity,
        WorkflowExecutable executable,
        ExecutableNode node,
        ActivityExecutionState invocation,
        IReadOnlyCollection<DurableValueState>? durableValues,
        CancellationToken cancellationToken,
        string? attemptId = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(scopeService);
        ArgumentNullException.ThrowIfNull(activity);
        if (activity is IRuntimeStructuralActivity || node.ActivityContract is not { } contract)
            return null;

        // Only an activity that can name a variable loads its frame chain: a variable-reference input, the free-name marker,
        // or an engine script activity. Any other gets a session whose every call faults the step, at no cost.
        var needsFrames = contract.HasVariableReferenceInputs || contract.AccessesUndeclaredVariables || activity is IStepVariableSessionConsumer;
        // A step inside a closed activity execution scope reads that scope's variables from its rows.
        if (needsFrames && durableValues is null && services.GetService<IDurableValueStateStore>() is { } durableValueStore)
            durableValues = await durableValueStore.ListAllDurableValueStatesAsync(invocation.Execution.WorkflowExecutionId, cancellationToken);
        var frames = needsFrames
            ? await scopeService.BuildVisibleFramesAsync(
                invocation.Execution.WorkflowExecutionId,
                invocation,
                includeCurrentIteration: true,
                includeOwnContainer: false,
                cancellationToken,
                durableValues)
            : null;
        return new StepVariableSession(frames, executable, invocation, node, services.GetService<IWorkflowExecutableInputValidator>(), attemptId);
    }
}
