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
        var suspends = activity is IStatefulActivity || activity is IRuntimeActivityCheckpointParticipant;
        return new StepVariableSession(frames, executable, invocation, node, services.GetService<IWorkflowExecutableInputValidator>(), attemptId, suspends,
            BoundReferences(node, contract), activity is IStepVariableSessionConsumer);
    }

    // The variables this node's reference inputs are bound to, from their pinned bindings, and whether each is writable.
    private static IReadOnlyDictionary<(string ScopeId, string Key), bool> BoundReferences(ExecutableNode node, Elsa.Activities.Runtime.Core.Models.ActivityContract contract)
    {
        var bound = new Dictionary<(string ScopeId, string Key), bool>();
        foreach (var input in contract.Inputs.Values)
        {
            if (input.VariableReference is not { } reference ||
                !node.InputBindings.TryGetValue(input.Key, out var binding) ||
                binding.LiteralValue is not { ValueKind: System.Text.Json.JsonValueKind.Object } literal ||
                !literal.TryGetProperty("scopeId", out var scope) || !literal.TryGetProperty("variableKey", out var key))
                continue;

            var address = (scope.GetString()!, key.GetString()!);
            var writable = reference.Access == Elsa.Activities.Runtime.Core.Models.ActivityVariableAccess.ReadWrite;
            bound[address] = bound.TryGetValue(address, out var existing) ? existing || writable : writable;
        }

        return bound;
    }
}
