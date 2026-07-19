using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Selects committed activity results from one immutable runtime view using structural-frame and
/// causal-lineage identity. It deliberately has no newest-by-node fallback.
/// </summary>
public sealed class CausalActivityResultResolver
{
    /// <summary>
    /// Resolves the unique completed producer invocation visible to <paramref name="consumer"/>.
    /// The caller supplies one captured runtime view so a complete input snapshot can resolve all of
    /// its bindings against the same state. An unavailable optional reference returns <see langword="null"/>;
    /// ambiguity always fails because choosing either candidate would be nondeterministic.
    /// </summary>
    public CausalActivityResultResolution? Resolve(
        RuntimeActivityResultReference reference,
        ActivityExecutionState consumer,
        IReadOnlyCollection<ActivityExecutionState> runtimeView)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return ResolveCore(
            reference.ProducerExecutableNodeId,
            reference.ProjectionKey,
            reference.IsOptional,
            reference.ProducerScopeId,
            consumer,
            runtimeView);
    }

    /// <summary>
    /// Resolves a portable expression parameter. Publication owns lexical-scope validation for this
    /// closed binding, so Runtime selects the unique causal producer without an ambient scope lookup.
    /// </summary>
    public CausalActivityResultResolution? Resolve(
        ActivityResultExpressionParameterBinding reference,
        ActivityExecutionState consumer,
        IReadOnlyCollection<ActivityExecutionState> runtimeView)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return ResolveCore(
            reference.ProducerNodeId,
            reference.ProjectionKey,
            reference.IsOptional,
            producerScopeId: null,
            consumer,
            runtimeView);
    }

    private static CausalActivityResultResolution? ResolveCore(
        string producerNodeId,
        string projectionKey,
        bool isOptional,
        string? producerScopeId,
        ActivityExecutionState consumer,
        IReadOnlyCollection<ActivityExecutionState> runtimeView)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(runtimeView);

        var workflowExecutionId = consumer.Execution.WorkflowExecutionId;
        var statesByInvocationId = runtimeView
            .Where(state => StringComparer.Ordinal.Equals(state.Execution.WorkflowExecutionId, workflowExecutionId))
            .ToDictionary(state => state.InvocationId, StringComparer.Ordinal);
        var causalLineage = BuildCausalLineage(consumer, statesByInvocationId);
        var candidates = statesByInvocationId.Values
            .Where(state => causalLineage.Contains(state.InvocationId))
            .Where(state => StringComparer.Ordinal.Equals(state.Execution.ExecutableNodeId, producerNodeId))
            .Where(state => IsStructurallyVisible(producerScopeId, state, consumer))
            .Where(IsCommittedCompletion)
            .OrderBy(state => state.ExecutionSequence)
            .ThenBy(state => state.InvocationId, StringComparer.Ordinal)
            .ToArray();

        return candidates.Length switch
        {
            0 when isOptional => null,
            0 => throw NewUnavailable(producerNodeId, projectionKey, producerScopeId, consumer),
            1 => new CausalActivityResultResolution(candidates[0], candidates[0].Completion!, projectionKey),
            _ => throw NewAmbiguous(producerNodeId, projectionKey, producerScopeId, consumer, candidates)
        };
    }

    private static HashSet<string> BuildCausalLineage(
        ActivityExecutionState consumer,
        IReadOnlyDictionary<string, ActivityExecutionState> statesByInvocationId)
    {
        var lineage = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(GetPredecessorIds(consumer));

        while (pending.TryPop(out var invocationId))
        {
            if (!lineage.Add(invocationId) || !statesByInvocationId.TryGetValue(invocationId, out var state))
                continue;

            foreach (var predecessorId in GetPredecessorIds(state))
                pending.Push(predecessorId);
        }

        return lineage;
    }

    private static IEnumerable<string> GetPredecessorIds(ActivityExecutionState state)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        Add(state.SchedulingActivityExecutionId);
        Add(state.ParentActivityExecutionId);
        Add(state.Provenance.SchedulingActivityExecutionId);
        Add(state.Provenance.ParentActivityExecutionId);
        return ids;

        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !StringComparer.Ordinal.Equals(value, state.InvocationId))
                ids.Add(value);
        }
    }

    private static bool IsCommittedCompletion(ActivityExecutionState state) =>
        state.Status == ActivityExecutionStatus.Completed &&
        state.Completion is { } completion &&
        StringComparer.Ordinal.Equals(completion.InvocationId, state.InvocationId);

    private static bool IsStructurallyVisible(
        string? declaredProducerScopeId,
        ActivityExecutionState producer,
        ActivityExecutionState consumer)
    {
        var actualProducerScopeId = GetExecutionScopeId(producer);
        if (declaredProducerScopeId is not null && actualProducerScopeId is not null &&
            !StringComparer.Ordinal.Equals(actualProducerScopeId, declaredProducerScopeId))
            return false;

        return IsFrameDimensionVisible(GetBranchId(producer), GetBranchId(consumer)) &&
               IsFrameDimensionVisible(GetIterationId(producer), GetIterationId(consumer));
    }

    private static bool IsFrameDimensionVisible(string? producerValue, string? consumerValue) =>
        producerValue is null || StringComparer.Ordinal.Equals(producerValue, consumerValue);

    private static string? GetBranchId(ActivityExecutionState state) =>
        NormalizeFrameId(state.Provenance.BranchId) ?? NormalizeFrameId(state.BranchId);

    private static string? GetIterationId(ActivityExecutionState state) =>
        NormalizeFrameId(state.Provenance.IterationId) ?? NormalizeFrameId(state.IterationId);

    private static string? GetExecutionScopeId(ActivityExecutionState state)
    {
        if (!string.IsNullOrWhiteSpace(state.Provenance.ExecutionScopeId))
            return state.Provenance.ExecutionScopeId;

        return state.Metadata.TryGetValue(RuntimeMetadataKeys.FlowchartExecutionScopeId, out var scopeId) &&
               !string.IsNullOrWhiteSpace(scopeId)
            ? scopeId
            : null;
    }

    private static string? NormalizeFrameId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static CausalActivityResultResolutionException NewUnavailable(
        string producerNodeId,
        string projectionKey,
        string? producerScopeId,
        ActivityExecutionState consumer) =>
        new(
            $"Activity result projection '{projectionKey}' from executable node '{producerNodeId}' " +
            $"in structural scope '{producerScopeId ?? "<publication-validated>"}' is not available in the causal lineage of consumer invocation '{consumer.InvocationId}'.",
            CausalActivityResultResolutionFailureReason.Unavailable,
            consumer.InvocationId,
            producerNodeId,
            projectionKey,
            producerScopeId ?? string.Empty);

    private static CausalActivityResultResolutionException NewAmbiguous(
        string producerNodeId,
        string projectionKey,
        string? producerScopeId,
        ActivityExecutionState consumer,
        IReadOnlyCollection<ActivityExecutionState> candidates)
    {
        var candidateIds = candidates.Select(state => state.InvocationId).ToArray();
        return new CausalActivityResultResolutionException(
            $"Activity result projection '{projectionKey}' from executable node '{producerNodeId}' " +
            $"in structural scope '{producerScopeId ?? "<publication-validated>"}' is ambiguous for consumer invocation '{consumer.InvocationId}'. " +
            $"Causal candidates: {string.Join(", ", candidateIds)}.",
            CausalActivityResultResolutionFailureReason.Ambiguous,
            consumer.InvocationId,
            producerNodeId,
            projectionKey,
            producerScopeId ?? string.Empty,
            candidateIds);
    }
}

public sealed record CausalActivityResultResolution(
    ActivityExecutionState ProducerInvocation,
    ActivityCompletion Completion,
    string ProjectionKey);
