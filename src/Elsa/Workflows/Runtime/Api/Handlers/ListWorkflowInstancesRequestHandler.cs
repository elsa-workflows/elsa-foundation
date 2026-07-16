using System.Globalization;
using System.Text;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class ListWorkflowInstancesRequestHandler(
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IActivityExecutionStateStore activityExecutionStateStore,
    IIncidentStateStore incidentStateStore,
    IActivityExecutionInspectionAuthorizationContext authorization)
    : IRequestHandler<ListWorkflowInstances, WorkflowInstanceListView>
{
    private const int PagedDefaultTake = 25;
    private const int PagedMaxTake = 100;
    private const int LegacyDefaultTake = 100;
    private const int LegacyMaxTake = 500;

    public async Task<WorkflowInstanceListView> Handle(ListWorkflowInstances request, CancellationToken cancellationToken)
    {
        var (defaultTake, maxTake) = request.PagingContract == WorkflowInstanceListPagingContract.LegacyArray
            ? (LegacyDefaultTake, LegacyMaxTake)
            : (PagedDefaultTake, PagedMaxTake);
        var take = Math.Clamp(request.Take ?? defaultTake, 1, maxTake);

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
        if (!string.IsNullOrWhiteSpace(request.Cursor))
            query = query with { Cursor = DecodeCursor(request.Cursor, maxTake, query.Scope()) };

        var page = authorization.TenantScope == "all-tenants"
            ? await workflowExecutionStateStore.QueryPageAsync(query, cancellationToken)
            : await QueryAuthorizedPageAsync(query, cancellationToken);
        var summaryTasks = page.Items.Select(async state =>
        {
            var activityCount = (await activityExecutionStateStore.ListAsync(state.WorkflowExecutionId, cancellationToken)).Count;
            var incidentCount = (await incidentStateStore.ListAsync(state.WorkflowExecutionId, cancellationToken)).Count;
            return WorkflowInstanceSummaryView.From(
                state,
                activityCount,
                incidentCount,
                authorization.CanInspectSensitiveValues(state));
        });
        var items = await Task.WhenAll(summaryTasks);

        return new(
            items,
            page.PreviousCursor is { } previous ? EncodeCursor(previous) : null,
            page.NextCursor is { } next ? EncodeCursor(next) : null,
            page.HasPrevious,
            page.HasNext,
            items.Length,
            page.TotalCount >= int.MaxValue ? int.MaxValue : (int)page.TotalCount);
    }

    private static WorkflowInstanceListView Empty() => new([], null, null, false, false, 0, 0);

    private async ValueTask<WorkflowExecutionStatePage> QueryAuthorizedPageAsync(
        WorkflowExecutionStatePageQuery query,
        CancellationToken cancellationToken)
    {
        // The provider-neutral page query can express one concrete tenant but not the authorization rule used by
        // inspection (a caller's tenant plus tenant-less executions). Materialize only for that constrained case,
        // filter before counting/cursoring, and then reuse the canonical in-memory keyset implementation. This keeps
        // unauthorized rows out of items, cursors, and TotalCount instead of trading disclosure safety for paging.
        var authorizedStore = new InMemoryWorkflowExecutionStateStore();
        var requiresSensitiveValues = query.CorrelationId is not null;
        foreach (var state in await workflowExecutionStateStore.ListAsync(cancellationToken))
        {
            if (!authorization.CanInspectStructure(state) ||
                (requiresSensitiveValues && !authorization.CanInspectSensitiveValues(state)))
                continue;

            await authorizedStore.SaveAsync(state, cancellationToken);
        }

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

    private static string EncodeCursor(WorkflowExecutionStatePageCursor cursor)
    {
        var value = string.Join('|',
            "v1",
            cursor.Direction == WorkflowExecutionStatePageDirection.Next ? "n" : "p",
            cursor.SortTimestamp.UtcTicks.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(cursor.WorkflowExecutionId)),
            cursor.PageSize.ToString(CultureInfo.InvariantCulture),
            cursor.Scope);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static WorkflowExecutionStatePageCursor DecodeCursor(string cursor, int maxTake, string queryScope)
    {
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split('|');
            if (parts.Length is not (5 or 6) || parts[0] != "v1" || parts[1] is not ("n" or "p") ||
                !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var pageSize))
                throw new FormatException();

            var scope = parts.Length == 6 ? parts[5] : queryScope;
            return new(
                new DateTimeOffset(ticks, TimeSpan.Zero),
                Encoding.UTF8.GetString(Convert.FromBase64String(parts[3])),
                parts[1] == "n" ? WorkflowExecutionStatePageDirection.Next : WorkflowExecutionStatePageDirection.Previous,
                Math.Clamp(pageSize, 1, maxTake),
                scope);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ArgumentException("The workflow instance cursor is invalid.", nameof(cursor), exception);
        }
    }
}
