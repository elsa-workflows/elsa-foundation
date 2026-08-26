using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class WorkflowDispatchInspectionService(
    IWorkflowDispatchQueryStore queryStore,
    IWorkflowDispatchStore store,
    IWorkflowDispatchRedriveStore redriveStore,
    IPersistenceAccessContextAccessor? accessContextAccessor = null,
    TimeProvider? timeProvider = null,
    ILogger<WorkflowDispatchInspectionService>? logger = null) : IWorkflowDispatchInspectionService
{
    private const int DefaultTake = WorkflowDispatchQuery.MaximumTake;
    private const int MaxTake = WorkflowDispatchQuery.MaximumTake;

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ILogger<WorkflowDispatchInspectionService> _logger = logger ?? NullLogger<WorkflowDispatchInspectionService>.Instance;

    public async Task<IReadOnlyCollection<WorkflowDispatchView>> ListAsync(
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

    public async Task<WorkflowDispatchRedriveView> RedriveAsync(
        RedriveWorkflowDispatch request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DispatchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestId);

        var result = await redriveStore.RedriveAsync(
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

    public async Task<WorkflowDispatchView?> GetAsync(
        GetWorkflowDispatch request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DispatchId);
        var record = await store.FindAsync(request.DispatchId, cancellationToken);
        if (record is not null)
            accessContextAccessor?.Current.EnsureTenantScope(record.TenantId);
        return record is null ? null : WorkflowDispatchView.From(record);
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

/// <summary>
/// The dispatch inspection operations the runtime endpoints dispatch to. A null view from
/// <see cref="GetAsync"/> means the dispatch record is missing.
/// </summary>
public interface IWorkflowDispatchInspectionService
{
    Task<IReadOnlyCollection<WorkflowDispatchView>> ListAsync(ListWorkflowDispatches request, CancellationToken cancellationToken);
    Task<WorkflowDispatchRedriveView> RedriveAsync(RedriveWorkflowDispatch request, CancellationToken cancellationToken);
    Task<WorkflowDispatchView?> GetAsync(GetWorkflowDispatch request, CancellationToken cancellationToken);
}
