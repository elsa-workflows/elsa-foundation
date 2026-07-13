using System.Globalization;
using System.Text;
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
    : IRequestHandler<ListWorkflowInstances, WorkflowInstanceListView>
{
    private const int DefaultTake = 25;
    private const int MaxTake = 100;

    public async Task<WorkflowInstanceListView> Handle(ListWorkflowInstances request, CancellationToken cancellationToken)
    {
        var states = await workflowExecutionStateStore.ListAsync(cancellationToken);
        var query = states.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(request.Status))
            query = query.Where(state => string.Equals(state.Status.ToString(), request.Status, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(request.DefinitionId))
            query = query.Where(state => string.Equals(state.PinnedExecutable.DefinitionId, request.DefinitionId, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
            query = query.Where(state => string.Equals(state.CorrelationId, request.CorrelationId, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(request.WorkflowExecutionId))
            query = query.Where(state => string.Equals(state.WorkflowExecutionId, request.WorkflowExecutionId, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(request.ArtifactId))
            query = query.Where(state => string.Equals(state.PinnedExecutable.ArtifactId, request.ArtifactId, StringComparison.Ordinal));

        if (request.From is { } from)
            query = query.Where(state => GetSortTimestamp(state) >= from);

        if (request.To is { } to)
            query = query.Where(state => GetSortTimestamp(state) <= to);

        var take = Math.Clamp(request.Take ?? DefaultTake, 1, MaxTake);
        var filteredStates = query
            .OrderByDescending(GetSortTimestamp)
            .ThenBy(state => state.WorkflowExecutionId, StringComparer.Ordinal)
            .ToArray();
        var pageCandidates = ApplyCursor(filteredStates, request.Cursor);
        var orderedStates = pageCandidates.Take(take).ToArray();

        var summaryTasks = orderedStates.Select(async state =>
        {
            var activityCount = (await activityExecutionStateStore.ListAsync(state.WorkflowExecutionId, cancellationToken)).Count;
            var incidentCount = (await incidentStateStore.ListAsync(state.WorkflowExecutionId, cancellationToken)).Count;
            return WorkflowInstanceSummaryView.From(state, activityCount, incidentCount);
        });
        var items = await Task.WhenAll(summaryTasks);
        var first = orderedStates.FirstOrDefault();
        var last = orderedStates.LastOrDefault();
        var firstIndex = first is null ? -1 : Array.FindIndex(filteredStates, state => state.WorkflowExecutionId == first.WorkflowExecutionId);
        var lastIndex = last is null ? -1 : Array.FindIndex(filteredStates, state => state.WorkflowExecutionId == last.WorkflowExecutionId);
        var hasPrevious = firstIndex > 0;
        var hasNext = lastIndex >= 0 && lastIndex < filteredStates.Length - 1;

        return new WorkflowInstanceListView(
            items,
            hasPrevious ? EncodeCursor(first!, CursorDirection.Previous, take) : null,
            hasNext ? EncodeCursor(last!, CursorDirection.Next, take) : null,
            hasPrevious,
            hasNext,
            items.Length,
            filteredStates.Length);
    }

    private static IReadOnlyCollection<WorkflowExecutionState> ApplyCursor(
        IReadOnlyCollection<WorkflowExecutionState> orderedStates,
        string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return orderedStates;

        var anchor = DecodeCursor(cursor);
        var relative = orderedStates.Where(state => anchor.Direction == CursorDirection.Next
            ? IsAfter(anchor, state)
            : IsBefore(anchor, state)).ToArray();
        return anchor.Direction == CursorDirection.Next
            ? relative
            : relative.TakeLast(anchor.PageSize).ToArray();
    }

    private static bool IsAfter(CursorAnchor anchor, WorkflowExecutionState candidate) =>
        Compare(candidate, anchor.Timestamp, anchor.WorkflowExecutionId) > 0;

    private static bool IsBefore(CursorAnchor anchor, WorkflowExecutionState candidate) =>
        Compare(candidate, anchor.Timestamp, anchor.WorkflowExecutionId) < 0;

    private static int Compare(WorkflowExecutionState candidate, DateTimeOffset timestamp, string workflowExecutionId)
    {
        var timestampComparison = timestamp.CompareTo(GetSortTimestamp(candidate));
        return timestampComparison != 0
            ? timestampComparison
            : StringComparer.Ordinal.Compare(candidate.WorkflowExecutionId, workflowExecutionId);
    }

    private static string EncodeCursor(WorkflowExecutionState state, CursorDirection direction, int pageSize)
    {
        var value = string.Join('|',
            "v1",
            direction == CursorDirection.Next ? "n" : "p",
            GetSortTimestamp(state).UtcTicks.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(state.WorkflowExecutionId)),
            pageSize.ToString(CultureInfo.InvariantCulture));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static CursorAnchor DecodeCursor(string cursor)
    {
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split('|');
            if (parts is not ["v1", "n" or "p", var ticksText, var executionIdText, var pageSizeText] ||
                !long.TryParse(ticksText, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                !int.TryParse(pageSizeText, NumberStyles.None, CultureInfo.InvariantCulture, out var pageSize))
                throw new FormatException();

            return new CursorAnchor(
                new DateTimeOffset(ticks, TimeSpan.Zero),
                Encoding.UTF8.GetString(Convert.FromBase64String(executionIdText)),
                parts[1] == "n" ? CursorDirection.Next : CursorDirection.Previous,
                Math.Clamp(pageSize, 1, MaxTake));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ArgumentException("The workflow instance cursor is invalid.", nameof(cursor), exception);
        }
    }

    private static DateTimeOffset GetSortTimestamp(WorkflowExecutionState state) =>
        state.UpdatedAt ?? state.CompletedAt ?? state.StartedAt ?? state.CreatedAt;

    private sealed record CursorAnchor(
        DateTimeOffset Timestamp,
        string WorkflowExecutionId,
        CursorDirection Direction,
        int PageSize);

    private enum CursorDirection
    {
        Previous,
        Next
    }
}
