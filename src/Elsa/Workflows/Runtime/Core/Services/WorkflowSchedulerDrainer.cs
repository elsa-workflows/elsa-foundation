using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowSchedulerDrainer : IWorkflowSchedulerDrainer
{
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly IReadOnlyCollection<IWorkflowSchedulerWorkHandler> _handlers;
    private readonly TimeProvider _timeProvider;

    public WorkflowSchedulerDrainer(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IEnumerable<IWorkflowSchedulerWorkHandler> handlers)
        : this(schedulerWorkQueue, handlers, TimeProvider.System)
    {
    }

    public WorkflowSchedulerDrainer(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IEnumerable<IWorkflowSchedulerWorkHandler> handlers,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _schedulerWorkQueue = schedulerWorkQueue;
        _handlers = handlers.ToArray();
        _timeProvider = timeProvider;
    }

    public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startedAt = _timeProvider.GetUtcNow();
        var results = new List<RuntimeSchedulerWorkItemResult>();
        var remaining = request.MaxWorkItems ?? int.MaxValue;

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var workItem = await _schedulerWorkQueue.DequeueAsync(request.WorkflowExecutionId, cancellationToken);
            if (workItem is null)
                break;

            var result = await DispatchAsync(workItem, cancellationToken);
            results.Add(result);
            remaining--;

            if (result.Status == RuntimeSchedulerWorkItemResultStatus.Faulted)
                break;
        }

        return new RuntimeSchedulerDrainResult(
            workflowExecutionId: request.WorkflowExecutionId,
            startedAt: startedAt,
            completedAt: _timeProvider.GetUtcNow(),
            items: results);
    }

    private async ValueTask<RuntimeSchedulerWorkItemResult> DispatchAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken)
    {
        IWorkflowSchedulerWorkHandler? handler = null;
        var startedAt = _timeProvider.GetUtcNow();

        try
        {
            handler = FindHandler(workItem);
            await handler.HandleAsync(workItem, cancellationToken);

            return new RuntimeSchedulerWorkItemResult(
                workItemId: workItem.WorkItemId,
                workflowExecutionId: workItem.WorkflowExecutionId,
                commandKind: workItem.CommandKind,
                status: RuntimeSchedulerWorkItemResultStatus.Completed,
                handlerName: handler.Name,
                startedAt: startedAt,
                completedAt: _timeProvider.GetUtcNow());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new RuntimeSchedulerWorkItemResult(
                workItemId: workItem.WorkItemId,
                workflowExecutionId: workItem.WorkflowExecutionId,
                commandKind: workItem.CommandKind,
                status: RuntimeSchedulerWorkItemResultStatus.Faulted,
                handlerName: handler?.Name ?? nameof(WorkflowSchedulerDrainer),
                startedAt: startedAt,
                completedAt: _timeProvider.GetUtcNow(),
                error: exception.Message);
        }
    }

    private IWorkflowSchedulerWorkHandler FindHandler(RuntimeSchedulerWorkItem workItem)
    {
        foreach (var handler in _handlers.Where(handler => handler is not NoopWorkflowSchedulerWorkHandler))
        {
            if (handler.CanHandle(workItem))
                return handler;
        }

        foreach (var handler in _handlers)
        {
            if (handler.CanHandle(workItem))
                return handler;
        }

        return new FaultingMissingSchedulerWorkHandler();
    }

    private sealed class FaultingMissingSchedulerWorkHandler : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(FaultingMissingSchedulerWorkHandler);

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"No workflow scheduler work handler accepted command kind '{workItem.CommandKind}'.");
    }
}
