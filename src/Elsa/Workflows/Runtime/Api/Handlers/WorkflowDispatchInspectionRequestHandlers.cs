using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class ListWorkflowDispatchesRequestHandler(IWorkflowDispatchQueryStore queryStore)
    : IRequestHandler<ListWorkflowDispatches, IReadOnlyCollection<WorkflowDispatchView>>
{
    private const int DefaultTake = WorkflowDispatchQuery.MaximumTake;
    private const int MaxTake = WorkflowDispatchQuery.MaximumTake;

    public async Task<IReadOnlyCollection<WorkflowDispatchView>> Handle(
        ListWorkflowDispatches request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var parentId = EmptyToNull(request.ParentWorkflowExecutionId);
        var childId = EmptyToNull(request.ChildWorkflowExecutionId);
        var status = ParseStatus(request.Status);
        if (parentId is null && childId is null && status is null)
            throw new ArgumentException("At least one parent execution, child execution, or lifecycle status filter is required.", nameof(request));
        var take = request.Take ?? DefaultTake;
        if (take is <= 0 or > MaxTake)
            throw new ArgumentOutOfRangeException(nameof(request), $"Take must be between 1 and {MaxTake}.");

        var records = await queryStore.QueryAsync(
            new WorkflowDispatchQuery(
                parentId,
                childId,
                status,
                take),
            cancellationToken);

        return records.Select(WorkflowDispatchView.From).ToArray();
    }

    private static WorkflowDispatchStatus? ParseStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!Enum.TryParse<WorkflowDispatchStatus>(value, ignoreCase: true, out var status) || !Enum.IsDefined(status))
            throw new ArgumentException($"The workflow dispatch status '{value}' is invalid.", nameof(value));
        return status;
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

public sealed class GetWorkflowDispatchRequestHandler(IWorkflowDispatchStore store)
    : IRequestHandler<GetWorkflowDispatch, GetWorkflowDispatchResponse>
{
    public async Task<GetWorkflowDispatchResponse> Handle(
        GetWorkflowDispatch request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DispatchId);
        var record = await store.FindAsync(request.DispatchId, cancellationToken);
        return new(record is null ? null : WorkflowDispatchView.From(record));
    }
}
