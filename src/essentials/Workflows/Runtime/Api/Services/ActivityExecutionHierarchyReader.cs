using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Services;

public sealed class ActivityExecutionHierarchyReader(
    IWorkflowExecutionStateStore workflowExecutions,
    IActivityExecutionHierarchyStore hierarchy,
    IActivityInspectionContextAsync authorization) : IActivityExecutionDescendantsReader
{
    Task<ActivityExecutionHierarchyPageView?> IActivityExecutionDescendantsReader.ReadAsync(GetActivityExecutionDescendants request, CancellationToken cancellationToken) =>
        ReadAsync(request.WorkflowExecutionId, request.ActivityExecutionId, request.Cursor, request.Limit, request.Include, cancellationToken).AsTask();

    public async ValueTask<ActivityExecutionHierarchyPageView?> ReadAsync(
        string workflowExecutionId,
        string activityExecutionId,
        string? cursor,
        int? limit,
        string? include,
        CancellationToken cancellationToken)
    {
        var workflow = await FindAuthorizedAsync(workflowExecutionId, cancellationToken);
        if (workflow is null)
            return null;
        var query = new ActivityExecutionHierarchyQuery(
            workflowExecutionId,
            activityExecutionId,
            cursor,
            limit,
            ParseInclude(include),
            await authorization.GetAuthorizationProfileAsync(cancellationToken),
            authorization.TenantScope);
        var page = await hierarchy.ReadPageAsync(query, cancellationToken);
        return page is null ? null : ActivityExecutionHierarchyPageView.From(page);
    }

    private async ValueTask<WorkflowExecutionState?> FindAuthorizedAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        var workflow = await workflowExecutions.FindAsync(workflowExecutionId, cancellationToken);
        return workflow is not null && await authorization.CanInspectStructureAsync(workflow, cancellationToken) ? workflow : null;
    }

    private static IReadOnlySet<ActivityExecutionHierarchyInclude> ParseInclude(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new HashSet<ActivityExecutionHierarchyInclude>();
        var result = new HashSet<ActivityExecutionHierarchyInclude>();
        foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var include = token.ToLowerInvariant() switch
            {
                "outcomes" => ActivityExecutionHierarchyInclude.Outcomes,
                "bookmarks" => ActivityExecutionHierarchyInclude.Bookmarks,
                "incidents" => ActivityExecutionHierarchyInclude.Incidents,
                _ => throw new ArgumentException($"Unknown hierarchy include value '{token}'.", nameof(value))
            };
            result.Add(include);
        }
        return result;
    }
}

/// <summary>
/// The descendant-hierarchy read operation the runtime endpoints dispatch to. A null page means the workflow
/// execution is missing or the caller may not inspect it.
/// </summary>
public interface IActivityExecutionDescendantsReader
{
    Task<ActivityExecutionHierarchyPageView?> ReadAsync(GetActivityExecutionDescendants request, CancellationToken cancellationToken);
}
