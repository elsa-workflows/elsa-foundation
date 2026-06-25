using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowExecutionDrainCoordinator : IWorkflowExecutionDrainCoordinator
{
    private const int MaxDrainCycles = 64;
    private const int OutboxDeliveryBatchSize = 64;

    private readonly IWorkflowSchedulerDrainer _schedulerDrainer;
    private readonly IRuntimePostCommitOutboxProcessor _postCommitOutboxProcessor;
    private readonly IReadOnlyCollection<IWorkflowSchedulerDrainObserver> _schedulerDrainObservers;

    public WorkflowExecutionDrainCoordinator(
        IWorkflowSchedulerDrainer schedulerDrainer,
        IRuntimePostCommitOutboxProcessor postCommitOutboxProcessor,
        IEnumerable<IWorkflowSchedulerDrainObserver> schedulerDrainObservers)
    {
        ArgumentNullException.ThrowIfNull(schedulerDrainer);
        ArgumentNullException.ThrowIfNull(postCommitOutboxProcessor);
        ArgumentNullException.ThrowIfNull(schedulerDrainObservers);

        _schedulerDrainer = schedulerDrainer;
        _postCommitOutboxProcessor = postCommitOutboxProcessor;
        _schedulerDrainObservers = schedulerDrainObservers.ToArray();
    }

    public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(
        WorkflowExecutionCommandEnvelope envelope,
        RuntimeSchedulerDrainRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.WorkflowExecutionId, envelope.WorkflowExecutionId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Scheduler drain request workflow execution ID '{request.WorkflowExecutionId}' does not match command envelope workflow execution ID '{envelope.WorkflowExecutionId}'.");

        var drainResult = await DrainSchedulerAndPostCommitWorkAsync(request, cancellationToken);
        await NotifyObserversAsync(envelope, drainResult, cancellationToken);
        return drainResult;
    }

    private async ValueTask<RuntimeSchedulerDrainResult> DrainSchedulerAndPostCommitWorkAsync(
        RuntimeSchedulerDrainRequest request,
        CancellationToken cancellationToken)
    {
        RuntimeSchedulerDrainResult? firstDrainResult = null;
        RuntimeSchedulerDrainResult? lastDrainResult = null;
        var itemResults = new List<RuntimeSchedulerWorkItemResult>();
        var completed = false;

        for (var cycle = 0; cycle < MaxDrainCycles; cycle++)
        {
            var drainResult = await _schedulerDrainer.DrainAsync(request, cancellationToken);
            firstDrainResult ??= drainResult;
            lastDrainResult = drainResult;
            itemResults.AddRange(drainResult.Items);

            if (drainResult.StoppedOnFault || drainResult.StoppedOnPause)
            {
                completed = true;
                break;
            }

            var outboxResult = await _postCommitOutboxProcessor.ProcessAsync(
                new RuntimePostCommitOutboxProcessRequest(
                    limit: OutboxDeliveryBatchSize,
                    workflowExecutionId: request.WorkflowExecutionId,
                    intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork),
                cancellationToken);

            if (outboxResult.DeliveredCount == 0)
            {
                completed = true;
                break;
            }
        }

        if (lastDrainResult is null || firstDrainResult is null)
            throw new InvalidOperationException("Workflow execution draining did not produce a scheduler drain result.");

        if (!completed)
            throw new InvalidOperationException($"Workflow execution drain exceeded the maximum cycle count of {MaxDrainCycles} for workflow execution '{request.WorkflowExecutionId}'.");

        return new RuntimeSchedulerDrainResult(
            workflowExecutionId: request.WorkflowExecutionId,
            startedAt: firstDrainResult.StartedAt,
            completedAt: lastDrainResult.CompletedAt,
            items: itemResults);
    }

    private async ValueTask NotifyObserversAsync(
        WorkflowExecutionCommandEnvelope envelope,
        RuntimeSchedulerDrainResult drainResult,
        CancellationToken cancellationToken)
    {
        List<Exception>? observerExceptions = null;

        foreach (var observer in _schedulerDrainObservers)
        {
            try
            {
                await observer.OnDrainedAsync(envelope, drainResult, cancellationToken);
            }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                if (observerExceptions is not null)
                {
                    observerExceptions.Add(exception);
                    throw new AggregateException("One or more scheduler drain observers failed before cancellation.", observerExceptions);
                }

                throw;
            }
            catch (Exception exception)
            {
                observerExceptions ??= [];
                observerExceptions.Add(exception);
            }
        }

        if (observerExceptions is not null)
            throw new AggregateException("One or more scheduler drain observers failed.", observerExceptions);
    }
}
