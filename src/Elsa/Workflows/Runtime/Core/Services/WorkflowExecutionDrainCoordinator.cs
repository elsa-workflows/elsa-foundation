using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowExecutionDrainCoordinator : IWorkflowExecutionDrainCoordinator
{
    private readonly IWorkflowSchedulerDrainer _schedulerDrainer;
    private readonly IRuntimePostCommitOutboxProcessor _postCommitOutboxProcessor;
    private readonly IReadOnlyCollection<IWorkflowSchedulerDrainObserver> _schedulerDrainObservers;
    private readonly WorkflowExecutionDrainCoordinatorOptions _options;
    private readonly IRuntimeExecutionOwnershipService? _ownershipService;
    private readonly IRuntimeExecutionOwnershipContextAccessor? _ownershipContextAccessor;
    private readonly IRuntimeCoalescingDrainScopeFactory? _coalescingScopeFactory;

    public WorkflowExecutionDrainCoordinator(
        IWorkflowSchedulerDrainer schedulerDrainer,
        IRuntimePostCommitOutboxProcessor postCommitOutboxProcessor,
        IEnumerable<IWorkflowSchedulerDrainObserver> schedulerDrainObservers,
        WorkflowExecutionDrainCoordinatorOptions? options = null)
        : this(schedulerDrainer, postCommitOutboxProcessor, schedulerDrainObservers, options, ownershipService: null, ownershipContextAccessor: null)
    {
    }

    public WorkflowExecutionDrainCoordinator(
        IWorkflowSchedulerDrainer schedulerDrainer,
        IRuntimePostCommitOutboxProcessor postCommitOutboxProcessor,
        IEnumerable<IWorkflowSchedulerDrainObserver> schedulerDrainObservers,
        WorkflowExecutionDrainCoordinatorOptions? options,
        IRuntimeExecutionOwnershipService? ownershipService,
        IRuntimeExecutionOwnershipContextAccessor? ownershipContextAccessor)
        : this(schedulerDrainer, postCommitOutboxProcessor, schedulerDrainObservers, options, ownershipService, ownershipContextAccessor, coalescingScopeFactory: null)
    {
    }

    // Greediest constructor: MS DI selects it only when the coalescing drain scope factory has been registered (the
    // opt-in coalescing wiring). On the default path the factory is absent, this constructor is not selected, and the
    // coordinator runs its existing ownership-only path byte-for-byte unchanged.
    public WorkflowExecutionDrainCoordinator(
        IWorkflowSchedulerDrainer schedulerDrainer,
        IRuntimePostCommitOutboxProcessor postCommitOutboxProcessor,
        IEnumerable<IWorkflowSchedulerDrainObserver> schedulerDrainObservers,
        WorkflowExecutionDrainCoordinatorOptions? options,
        IRuntimeExecutionOwnershipService? ownershipService,
        IRuntimeExecutionOwnershipContextAccessor? ownershipContextAccessor,
        IRuntimeCoalescingDrainScopeFactory? coalescingScopeFactory)
    {
        ArgumentNullException.ThrowIfNull(schedulerDrainer);
        ArgumentNullException.ThrowIfNull(postCommitOutboxProcessor);
        ArgumentNullException.ThrowIfNull(schedulerDrainObservers);

        _schedulerDrainer = schedulerDrainer;
        _postCommitOutboxProcessor = postCommitOutboxProcessor;
        _schedulerDrainObservers = schedulerDrainObservers.ToArray();
        _options = options ?? new WorkflowExecutionDrainCoordinatorOptions();
        _ownershipService = ownershipService;
        _ownershipContextAccessor = ownershipContextAccessor;
        _coalescingScopeFactory = coalescingScopeFactory;
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

        // Single-writer ownership (RT-2): claim a fencing lease for this drain and expose it as the active ownership
        // scope so every checkpoint commit made during the drain is fenced against it. Acquiring writes a lease +
        // heartbeat to operational state, giving the recovery scanner real data; a crash mid-drain leaves that lease in
        // place (the finally never runs) so the interrupted execution stays detectable, while a clean or handled return
        // releases it to avoid false-positive recovery.
        if (_ownershipService is null || _ownershipContextAccessor is null)
            return await DrainCoreAsync(envelope, request, cancellationToken);

        var lease = await _ownershipService.AcquireAsync(request.WorkflowExecutionId, cancellationToken);
        using (_ownershipContextAccessor.Push(lease))
        {
            try
            {
                return await DrainCoreAsync(envelope, request, cancellationToken);
            }
            finally
            {
                await _ownershipService.ReleaseAsync(lease, cancellationToken);
            }
        }
    }

    private async ValueTask<RuntimeSchedulerDrainResult> DrainCoreAsync(
        WorkflowExecutionCommandEnvelope envelope,
        RuntimeSchedulerDrainRequest request,
        CancellationToken cancellationToken)
    {
        // Default path: no coalescing scope factory registered, so the drain runs with Immediate persistence unchanged.
        if (_coalescingScopeFactory is null)
        {
            var plainResult = await DrainSchedulerAndPostCommitWorkAsync(request, cancellationToken);
            await NotifyObserversAsync(envelope, plainResult, cancellationToken);
            return plainResult;
        }

        // Coalescing path: establish the ambient session for the drain, then fold-and-flush the buffered segment at
        // quiescence. The flush runs inside the active ownership scope so W5 fencing gates the single durable write. If
        // the drain throws, the flush is skipped and the scope is disposed with its buffer discarded, so a crash
        // mid-segment replays from the last flushed state plus durable scheduler-queue redelivery.
        await using var scope = _coalescingScopeFactory.Begin(request.WorkflowExecutionId);
        var drainResult = await DrainSchedulerAndPostCommitWorkAsync(request, cancellationToken);
        await scope.FlushAtQuiescenceAsync(cancellationToken);
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
        var outboxDeliveryResults = new List<RuntimePostCommitOutboxProcessResult>();
        var stopReason = RuntimeSchedulerDrainStopReason.Quiesced;
        var outboxDeliveryFailed = false;
        var completed = false;

        for (var cycle = 0; cycle < _options.MaxDrainCycles; cycle++)
        {
            var drainResult = await _schedulerDrainer.DrainAsync(request, cancellationToken);
            firstDrainResult ??= drainResult;
            lastDrainResult = drainResult;
            itemResults.AddRange(drainResult.Items);

            if (drainResult.StoppedOnFault || drainResult.StoppedOnPause)
            {
                stopReason = drainResult.StoppedOnFault
                    ? RuntimeSchedulerDrainStopReason.Faulted
                    : RuntimeSchedulerDrainStopReason.Paused;
                completed = true;
                break;
            }

            var outboxResult = await _postCommitOutboxProcessor.ProcessAsync(
                new RuntimePostCommitOutboxProcessRequest(
                    limit: _options.OutboxDeliveryBatchSize,
                    workflowExecutionId: request.WorkflowExecutionId,
                    intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork),
                cancellationToken);
            outboxDeliveryResults.Add(outboxResult);
            outboxDeliveryFailed = outboxDeliveryFailed || outboxResult.FailedCount > 0;

            if (outboxResult.DeliveredCount == 0)
            {
                stopReason = outboxDeliveryFailed
                    ? RuntimeSchedulerDrainStopReason.OutboxDeliveryFailed
                    : RuntimeSchedulerDrainStopReason.Quiesced;
                completed = true;
                break;
            }
        }

        if (lastDrainResult is null || firstDrainResult is null)
            throw new InvalidOperationException("Workflow execution draining did not produce a scheduler drain result.");

        if (!completed)
            throw new WorkflowExecutionDrainCycleLimitExceededException(request.WorkflowExecutionId, _options.MaxDrainCycles);

        return new RuntimeSchedulerDrainResult(
            workflowExecutionId: request.WorkflowExecutionId,
            startedAt: firstDrainResult.StartedAt,
            completedAt: lastDrainResult.CompletedAt,
            items: itemResults,
            outboxDeliveryResults: outboxDeliveryResults,
            stopReason: stopReason);
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
