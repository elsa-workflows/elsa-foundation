using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class RuntimePauseDecisionProvider : IRuntimePauseDecisionProvider
{
    private const string PolicyMetadataKey = "runtime.pause.policy";
    private const string ScopeMetadataKey = "runtime.pause.holdScope";

    private readonly IWorkflowHoldStateStore _controlPlaneStateStore;

    public RuntimePauseDecisionProvider(IWorkflowHoldStateStore controlPlaneStateStore)
    {
        ArgumentNullException.ThrowIfNull(controlPlaneStateStore);

        _controlPlaneStateStore = controlPlaneStateStore;
    }

    public async ValueTask<SchedulerPauseDecision> DecideAsync(RuntimePauseDecisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var states = ShouldUseWorkflowScopedQuery(request)
            ? await _controlPlaneStateStore.ListForWorkflowExecutionAsync(request.WorkflowExecutionId!, cancellationToken)
            : await _controlPlaneStateStore.ListAllAsync(cancellationToken);
        var matchingHold = states
            .SelectMany(state => state.ActiveHolds)
            .Where(hold => hold.IsEffective)
            .Where(hold => Matches(hold, request))
            .OrderBy(hold => hold.RequestedAt)
            .ThenBy(hold => hold.HoldId, StringComparer.Ordinal)
            .FirstOrDefault();

        if (matchingHold is null)
            return new SchedulerPauseDecision(
                canAdvance: true,
                boundary: request.Boundary,
                continuationPolicy: RuntimePauseContinuationPolicy.NotPaused,
                holdId: null,
                reason: null,
                metadata: new Dictionary<string, string>
                {
                    [PolicyMetadataKey] = nameof(RuntimePauseDecisionProvider)
                });

        return new SchedulerPauseDecision(
            canAdvance: false,
            boundary: request.Boundary,
            continuationPolicy: matchingHold.ContinuationPolicy,
            holdId: matchingHold.HoldId,
            reason: matchingHold.Reason,
            metadata: new Dictionary<string, string>
            {
                [PolicyMetadataKey] = nameof(RuntimePauseDecisionProvider),
                [ScopeMetadataKey] = matchingHold.Scope.ToString()
            });
    }

    private static bool Matches(WorkflowHold hold, RuntimePauseDecisionRequest request) =>
        hold.Scope switch
        {
            WorkflowHoldScope.WorkflowExecution =>
                StringComparer.Ordinal.Equals(hold.WorkflowExecutionId, request.WorkflowExecutionId),
            WorkflowHoldScope.ActivityExecution =>
                StringComparer.Ordinal.Equals(hold.WorkflowExecutionId, request.WorkflowExecutionId) &&
                StringComparer.Ordinal.Equals(hold.ActivityExecutionId, request.ActivityExecutionId),
            WorkflowHoldScope.Generator =>
                StringComparer.Ordinal.Equals(hold.WorkflowExecutionId, request.WorkflowExecutionId) &&
                StringComparer.Ordinal.Equals(hold.GeneratorId, request.GeneratorId),
            WorkflowHoldScope.Ingress =>
                StringComparer.Ordinal.Equals(hold.IngressSourceId, request.IngressSourceId),
            WorkflowHoldScope.WorkerDispatcher =>
                StringComparer.Ordinal.Equals(hold.WorkerId, request.WorkerId),
            WorkflowHoldScope.HostDrain =>
                StringComparer.Ordinal.Equals(hold.HostId, request.HostId),
            _ => false
        };

    private static bool ShouldUseWorkflowScopedQuery(RuntimePauseDecisionRequest request) =>
        request.WorkflowExecutionId is not null &&
        request.IngressSourceId is null &&
        request.WorkerId is null &&
        request.HostId is null;
}
