using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class ListWorkflowInstancesRequestHandler(
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IActivityExecutionStateStore activityExecutionStateStore,
    IIncidentStateStore incidentStateStore)
    : IRequestHandler<ListWorkflowInstances, IReadOnlyCollection<WorkflowInstanceSummaryView>>
{
    private const int DefaultTake = 100;
    private const int MaxTake = 500;

    public async Task<IReadOnlyCollection<WorkflowInstanceSummaryView>> Handle(ListWorkflowInstances request, CancellationToken cancellationToken)
    {
        var states = await workflowExecutionStateStore.ListAsync(cancellationToken);
        var query = states.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(request.Status))
            query = query.Where(state => string.Equals(state.Status.ToString(), request.Status, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(request.DefinitionId))
            query = query.Where(state => string.Equals(state.PinnedExecutable.DefinitionId, request.DefinitionId, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
            query = query.Where(state => string.Equals(state.CorrelationId, request.CorrelationId, StringComparison.Ordinal));

        var take = Math.Clamp(request.Take ?? DefaultTake, 1, MaxTake);
        var orderedStates = query
            .OrderByDescending(GetSortTimestamp)
            .ThenBy(state => state.WorkflowExecutionId, StringComparer.Ordinal)
            .Take(take)
            .ToArray();

        var summaryTasks = orderedStates.Select(async state =>
        {
            var activityCount = (await activityExecutionStateStore.ListAsync(state.WorkflowExecutionId, cancellationToken)).Count;
            var incidentCount = (await incidentStateStore.ListAsync(state.WorkflowExecutionId, cancellationToken)).Count;
            return WorkflowInstanceSummaryView.From(state, activityCount, incidentCount);
        });
        return await Task.WhenAll(summaryTasks);
    }

    private static DateTimeOffset GetSortTimestamp(WorkflowExecutionState state) =>
        state.UpdatedAt ?? state.CompletedAt ?? state.StartedAt ?? state.CreatedAt;
}
