using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class WorkflowInstanceListService(
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IActivityExecutionStateStore activityExecutionStateStore,
    IIncidentStateStore incidentStateStore,
    IActivityInspectionContextAsync authorization) : IWorkflowInstanceListService
{
    private const int PagedDefaultTake = 25;
    private const int PagedMaxTake = 100;
    private const int LegacyDefaultTake = 100;
    private const int LegacyMaxTake = 500;

    public async Task<WorkflowInstanceListView> ListAsync(ListWorkflowInstances request, CancellationToken cancellationToken)
    {
        var (defaultTake, maxTake) = request.PagingContract == WorkflowInstanceListPagingContract.LegacyArray
            ? (LegacyDefaultTake, LegacyMaxTake)
            : (PagedDefaultTake, PagedMaxTake);
        var take = Math.Clamp(request.Take ?? defaultTake, 1, maxTake);

        var incidentHealth = EmptyToNull(request.IncidentHealth)?.Trim().ToLowerInvariant();
        if (incidentHealth is not (null or "active" or "blocking" or "none"))
            throw new ArgumentException($"The incident health filter '{request.IncidentHealth}' is invalid.", nameof(request.IncidentHealth));

        var hasValidStatus = TryParseStatus(request.Status, out var status);

        WorkflowRunKind? runKind = null;
        if (!string.IsNullOrWhiteSpace(request.RunKind))
        {
            if (!Enum.TryParse<WorkflowRunKind>(request.RunKind, ignoreCase: true, out var parsedRunKind) || !Enum.IsDefined(parsedRunKind))
                throw new ArgumentException($"The workflow run kind '{request.RunKind}' is invalid.", nameof(request.RunKind));
            runKind = parsedRunKind;
        }

        if (!hasValidStatus)
            return Empty();

        var query = new WorkflowExecutionStatePageQuery(
            take,
            DefinitionId: EmptyToNull(request.DefinitionId),
            Status: status,
            RunKind: runKind,
            From: request.From,
            To: request.To,
            CorrelationId: EmptyToNull(request.CorrelationId),
            WorkflowExecutionId: EmptyToNull(request.WorkflowExecutionId),
            ArtifactId: EmptyToNull(request.ArtifactId));
        var cursorPrefix = incidentHealth is null ? null : $"health.{incidentHealth}.";
        if (!string.IsNullOrWhiteSpace(request.Cursor))
        {
            var cursor = request.Cursor;
            if (cursorPrefix is not null)
            {
                if (!cursor.StartsWith(cursorPrefix, StringComparison.Ordinal))
                    throw new ArgumentException("The workflow history cursor does not belong to this incident health query.", "cursor");
                cursor = cursor[cursorPrefix.Length..];
            }
            query = query with { Cursor = cursor };
        }

        query.Validate();
        var healthQuery = incidentHealth is not null &&
                          workflowExecutionStateStore is IWorkflowHealthQuery candidateHealthQuery &&
                          candidateHealthQuery.SupportsIncidentStore(incidentStateStore)
            ? candidateHealthQuery
            : null;
        WorkflowExecutionStatePage page;
        if (authorization is AllowAllActivityExecutionInspectionAuthorizationContext && incidentHealth is null)
            page = await workflowExecutionStateStore.QueryPageAsync(query, cancellationToken);
        else if (authorization is AllowAllActivityExecutionInspectionAuthorizationContext && query.CorrelationId is null &&
                 incidentHealth is not null && healthQuery is not null)
            page = await healthQuery.QueryHealthPageAsync(query, Enum.Parse<IncidentHealth>(incidentHealth, ignoreCase: true), cancellationToken);
        else
            page = await QueryAuthorizedPageAsync(query, incidentHealth, healthQuery, cancellationToken);
        // Every store in a request resolves the same scoped persistence context, so the per-row reads are issued one
        // at a time. Overlapping them - across rows or within a row - starts a second operation on that one context,
        // which relational providers reject, so the page is composed sequentially rather than fanned out.
        var items = new WorkflowInstanceSummaryView[page.Items.Count];
        for (var index = 0; index < items.Length; index++)
        {
            var state = page.Items[index];
            var activityCount = await activityExecutionStateStore.CountAsync(state.WorkflowExecutionId, cancellationToken);
            var health = await incidentStateStore.CountHealthAsync(state.WorkflowExecutionId, cancellationToken);
            var canInspectSensitiveValues = await authorization.CanInspectSensitiveValuesAsync(state, cancellationToken);
            items[index] = WorkflowInstanceSummaryView.From(state, activityCount, health.Total, canInspectSensitiveValues, health);
        }

        return new(
            items,
            page.NextCursor is null ? null : cursorPrefix + page.NextCursor,
            page.HasNext,
            items.Length,
            page.TotalCount >= int.MaxValue ? int.MaxValue : (int)page.TotalCount);
    }

    private static WorkflowInstanceListView Empty() => new([], null, false, 0, 0);

    private async ValueTask<WorkflowExecutionStatePage> QueryAuthorizedPageAsync(
        WorkflowExecutionStatePageQuery query,
        string? incidentHealth,
        IWorkflowHealthQuery? healthQuery,
        CancellationToken cancellationToken)
    {
        // Health and inspection authorization are not predicates in the provider-neutral page query. Apply both
        // before canonical keyset paging so unauthorized/nonmatching runs never enter items, counts or cursors.
        var authorizedStore = new InMemoryWorkflowExecutionStateStore();
        var requiresSensitiveValues = query.CorrelationId is not null;
        string? candidateCursor = null;
        do
        {
            var candidateQuery = query with { PageSize = PagedMaxTake, Cursor = candidateCursor };
            var candidates = incidentHealth is not null && healthQuery is not null
                ? await healthQuery.QueryHealthPageAsync(candidateQuery, Enum.Parse<IncidentHealth>(incidentHealth, ignoreCase: true), cancellationToken)
                : await workflowExecutionStateStore.QueryPageAsync(candidateQuery, cancellationToken);
            foreach (var state in candidates.Items)
            {
                if (!await authorization.CanInspectStructureAsync(state, cancellationToken) ||
                    (requiresSensitiveValues && !await authorization.CanInspectSensitiveValuesAsync(state, cancellationToken)))
                    continue;

                if (incidentHealth is not null)
                {
                    var health = await incidentStateStore.CountHealthAsync(state.WorkflowExecutionId, cancellationToken);
                    var matches = incidentHealth switch
                    {
                        "active" => health.Active > 0,
                        "blocking" => health.Blocking > 0,
                        "none" => health.Active == 0,
                        _ => false
                    };
                    if (!matches)
                        continue;
                }
                await authorizedStore.SaveAsync(state, cancellationToken);
            }
            candidateCursor = candidates.NextCursor;
        } while (candidateCursor is not null);

        return await authorizedStore.QueryPageAsync(query, cancellationToken);
    }

    private static bool TryParseStatus(string? value, out WorkflowExecutionStatus? status)
    {
        status = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;
        if (!Enum.TryParse<WorkflowExecutionStatus>(value, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            return false;
        status = parsed;
        return true;
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
/// The workflow-instance list operation the runtime endpoints dispatch to. The request's paging contract selects
/// the historical array route's bounds or the additive page route's bounded defaults.
/// </summary>
public interface IWorkflowInstanceListService
{
    Task<WorkflowInstanceListView> ListAsync(ListWorkflowInstances request, CancellationToken cancellationToken);
}
