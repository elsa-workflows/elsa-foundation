using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class ListWorkflowDispatchesRequestHandler(
    IWorkflowDispatchQueryStore queryStore,
    IPersistenceAccessContextAccessor? accessContextAccessor = null)
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
                take,
                request.AfterCreatedAt,
                EmptyToNull(request.AfterDispatchId)),
            cancellationToken);

        if (accessContextAccessor is not null)
        {
            foreach (var record in records)
                accessContextAccessor.Current.EnsureTenantScope(record.TenantId);
        }

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

public sealed class RedriveWorkflowDispatchRequestHandler : IRequestHandler<RedriveWorkflowDispatch, WorkflowDispatchRedriveView>
{
    private readonly IWorkflowDispatchRedriveStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RedriveWorkflowDispatchRequestHandler> _logger;

    public RedriveWorkflowDispatchRequestHandler(
        IWorkflowDispatchRedriveStore store,
        TimeProvider? timeProvider = null,
        ILogger<RedriveWorkflowDispatchRequestHandler>? logger = null)
    {
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<RedriveWorkflowDispatchRequestHandler>.Instance;
    }

    public async Task<WorkflowDispatchRedriveView> Handle(
        RedriveWorkflowDispatch request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DispatchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestId);

        var result = await _store.RedriveAsync(
            new WorkflowDispatchRedriveRequest(request.DispatchId, request.RequestId, _timeProvider.GetUtcNow()),
            cancellationToken);

        _logger.LogInformation(
            new EventId(68109, "WorkflowDispatchRedriveEvaluated"),
            "Workflow dispatch redrive {Disposition} for {DispatchId} at generation {DeliveryGeneration}",
            result.Disposition,
            result.DispatchId,
            result.Generation);
        return WorkflowDispatchRedriveView.From(result);
    }
}

public sealed class GetWorkflowDispatchRequestHandler(
    IWorkflowDispatchStore store,
    IPersistenceAccessContextAccessor? accessContextAccessor = null)
    : IRequestHandler<GetWorkflowDispatch, GetWorkflowDispatchResponse>
{
    public async Task<GetWorkflowDispatchResponse> Handle(
        GetWorkflowDispatch request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DispatchId);
        var record = await store.FindAsync(request.DispatchId, cancellationToken);
        if (record is not null)
            accessContextAccessor?.Current.EnsureTenantScope(record.TenantId);
        return new(record is null ? null : WorkflowDispatchView.From(record));
    }
}
