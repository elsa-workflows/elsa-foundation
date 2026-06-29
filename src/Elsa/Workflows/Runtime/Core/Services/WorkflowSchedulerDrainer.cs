using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowSchedulerDrainer : IWorkflowSchedulerDrainer
{
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly IReadOnlyCollection<IWorkflowSchedulerWorkHandler> _customHandlers;
    private readonly IReadOnlyCollection<IWorkflowSchedulerWorkHandler> _fallbackHandlers;
    private readonly TimeProvider _timeProvider;
    private readonly IWorkflowSchedulerPauseGate? _pauseGate;
    private readonly IWorkflowExecutionAmbientServicesAccessor _ambientServicesAccessor;
    private readonly IWorkflowExecutionStateStore? _workflowExecutionStateStore;

    public WorkflowSchedulerDrainer(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IEnumerable<IWorkflowSchedulerWorkHandler> handlers)
        : this(schedulerWorkQueue, handlers, TimeProvider.System, pauseGate: null)
    {
    }

    public WorkflowSchedulerDrainer(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IEnumerable<IWorkflowSchedulerWorkHandler> handlers,
        IWorkflowSchedulerPauseGate pauseGate)
        : this(schedulerWorkQueue, handlers, TimeProvider.System, pauseGate)
    {
    }

    public WorkflowSchedulerDrainer(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IEnumerable<IWorkflowSchedulerWorkHandler> handlers,
        TimeProvider timeProvider)
        : this(schedulerWorkQueue, handlers, timeProvider, pauseGate: null)
    {
    }

    public WorkflowSchedulerDrainer(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IEnumerable<IWorkflowSchedulerWorkHandler> handlers,
        TimeProvider timeProvider,
        IWorkflowSchedulerPauseGate? pauseGate)
        : this(schedulerWorkQueue, handlers, timeProvider, pauseGate, NoopWorkflowExecutionAmbientServicesAccessor.Instance)
    {
    }

    public WorkflowSchedulerDrainer(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IEnumerable<IWorkflowSchedulerWorkHandler> handlers,
        TimeProvider timeProvider,
        IWorkflowSchedulerPauseGate? pauseGate,
        IWorkflowExecutionAmbientServicesAccessor ambientServicesAccessor)
        : this(schedulerWorkQueue, handlers, timeProvider, pauseGate, ambientServicesAccessor, workflowExecutionStateStore: null)
    {
    }

    public WorkflowSchedulerDrainer(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IEnumerable<IWorkflowSchedulerWorkHandler> handlers,
        TimeProvider timeProvider,
        IWorkflowSchedulerPauseGate? pauseGate,
        IWorkflowExecutionAmbientServicesAccessor ambientServicesAccessor,
        IWorkflowExecutionStateStore? workflowExecutionStateStore)
    {
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(ambientServicesAccessor);

        _schedulerWorkQueue = schedulerWorkQueue;
        var handlerSnapshot = handlers.ToArray();
        _customHandlers = handlerSnapshot.Where(handler => handler is not IFallbackWorkflowSchedulerWorkHandler).ToArray();
        _fallbackHandlers = handlerSnapshot.Where(handler => handler is IFallbackWorkflowSchedulerWorkHandler).ToArray();
        _timeProvider = timeProvider;
        _pauseGate = pauseGate;
        _ambientServicesAccessor = ambientServicesAccessor;
        _workflowExecutionStateStore = workflowExecutionStateStore;
    }

    public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var ambientServices = _ambientServicesAccessor.Push(request.AmbientServices);
        var startedAt = _timeProvider.GetUtcNow();
        var results = new List<RuntimeSchedulerWorkItemResult>();
        var remaining = request.MaxWorkItems ?? int.MaxValue;

        // Once the workflow execution reaches a terminal status (Completed/Faulted/Cancelled), any sibling work
        // a parallel fork already enqueued must not run: dispatching it would write post-completion state. The
        // status is read once on entry (covers "already terminal") and re-checked only after a dispatched item
        // completes — the workflow can only become terminal as a result of work dispatched inside this loop
        // (Finish/checkpoint/cancel handlers all run as dispatched scheduler work here), so a per-iteration read
        // would re-load and deserialize the state document needlessly on durable providers. (#293)
        var stoppedOnTerminalStatus = await IsWorkflowTerminatedAsync(request.WorkflowExecutionId, cancellationToken);

        while (remaining > 0 && !stoppedOnTerminalStatus)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nextWorkItem = await PeekAsync(request.WorkflowExecutionId, cancellationToken);
            if (nextWorkItem is null)
                break;

            var pauseDecision = await EvaluatePauseAsync(nextWorkItem, cancellationToken);
            if (pauseDecision is { CanAdvance: false })
            {
                results.Add(CreatePausedResult(nextWorkItem, pauseDecision));
                break;
            }

            var workItem = await _schedulerWorkQueue.DequeueAsync(request.WorkflowExecutionId, cancellationToken);
            if (workItem is null)
                break;

            var result = await DispatchAsync(workItem, cancellationToken);
            results.Add(result);
            remaining--;

            if (result.Status == RuntimeSchedulerWorkItemResultStatus.Faulted)
                break;

            // The dispatched item may have committed a terminal status (e.g. the Finish path). Re-check so the
            // remaining queued siblings are not dequeued on the next iteration.
            stoppedOnTerminalStatus = await IsWorkflowTerminatedAsync(request.WorkflowExecutionId, cancellationToken);
        }

        // The loop only continues past a Completed result, so a terminal stop always coincides with an
        // all-completed drain; flag it as the stop reason.
        var stopReason = stoppedOnTerminalStatus
            ? RuntimeSchedulerDrainStopReason.WorkflowTerminated
            : (RuntimeSchedulerDrainStopReason?)null;

        return new RuntimeSchedulerDrainResult(
            workflowExecutionId: request.WorkflowExecutionId,
            startedAt: startedAt,
            completedAt: _timeProvider.GetUtcNow(),
            items: results,
            stopReason: stopReason);
    }

    private async ValueTask<bool> IsWorkflowTerminatedAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        var store = ResolveWorkflowExecutionStateStore();
        if (store is null)
            return false;

        var state = await store.FindAsync(workflowExecutionId, cancellationToken);
        return state is not null && state.Status.IsTerminal();
    }

    private IWorkflowExecutionStateStore? ResolveWorkflowExecutionStateStore() =>
        _ambientServicesAccessor.Current?.GetService<IWorkflowExecutionStateStore>() ?? _workflowExecutionStateStore;

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
                error: exception.ToString());
        }
    }

    private async ValueTask<RuntimeSchedulerWorkItem?> PeekAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        var items = await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery(workflowExecutionId, limit: 1), cancellationToken);
        return items.FirstOrDefault();
    }

    private async ValueTask<SchedulerPauseDecision?> EvaluatePauseAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken)
    {
        if (_pauseGate is null)
            return null;

        return await _pauseGate.EvaluateAsync(workItem, cancellationToken);
    }

    private RuntimeSchedulerWorkItemResult CreatePausedResult(RuntimeSchedulerWorkItem workItem, SchedulerPauseDecision decision)
    {
        var now = _timeProvider.GetUtcNow();
        return new RuntimeSchedulerWorkItemResult(
            workItemId: workItem.WorkItemId,
            workflowExecutionId: workItem.WorkflowExecutionId,
            commandKind: workItem.CommandKind,
            status: RuntimeSchedulerWorkItemResultStatus.Paused,
            handlerName: nameof(WorkflowSchedulerPauseGate),
            startedAt: now,
            completedAt: now,
            error: $"Scheduler work is paused at boundary '{decision.Boundary}' by hold '{decision.HoldId}': {decision.Reason}");
    }

    private IWorkflowSchedulerWorkHandler FindHandler(RuntimeSchedulerWorkItem workItem)
    {
        foreach (var handler in _customHandlers)
        {
            if (handler.CanHandle(workItem))
                return handler;
        }

        foreach (var handler in _fallbackHandlers)
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
