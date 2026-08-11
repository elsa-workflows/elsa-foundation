using System.Runtime.ExceptionServices;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowDrainOrchestrator : IWorkflowDrainOrchestrator
{
    private readonly IWorkflowSchedulerDrainer _schedulerDrainer;
    private readonly IRuntimePostCommitOutboxProcessor _postCommitOutboxProcessor;
    private readonly IReadOnlyCollection<IWorkflowSchedulerDrainObserver> _schedulerDrainObservers;
    private readonly WorkflowDrainOrchestratorOptions _options;
    private readonly IRuntimeExecutionOwnershipService _ownershipService;
    private readonly IRuntimeExecutionOwnershipContextAccessor _ownershipContextAccessor;
    private readonly IRuntimeCoalescingDrainScopeFactory? _coalescingScopeFactory;
    private readonly IRuntimeLiveDrainDeliveryAccessor? _liveDrainDeliveryAccessor;
    private readonly IRuntimeCheckpointCadenceResolver? _cadenceResolver;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the orchestrator. C1 (#1227): the six telescoping constructors collapsed into this single primary
    /// constructor: five required collaborators followed by optional collaborators that default to their
    /// no-op/system implementations. The ownership service and the ownership context accessor are <b>required by
    /// construction</b> so the RT-2 single-writer lease, which fences every checkpoint commit made during the drain
    /// and cancels the drain when the lease is lost, can never be silently disabled by picking a narrower
    /// constructor. The drain observers are required for the same reason: they decide fault outcomes (blocking
    /// incidents, poison projection, incident strategy resolution), so the set must be handed in deliberately.
    /// </summary>
    public WorkflowDrainOrchestrator(
        IWorkflowSchedulerDrainer schedulerDrainer,
        IRuntimePostCommitOutboxProcessor postCommitOutboxProcessor,
        IEnumerable<IWorkflowSchedulerDrainObserver> schedulerDrainObservers,
        IRuntimeExecutionOwnershipService ownershipService,
        IRuntimeExecutionOwnershipContextAccessor ownershipContextAccessor,
        WorkflowDrainOrchestratorOptions? options = null,
        IRuntimeCoalescingDrainScopeFactory? coalescingScopeFactory = null,
        IRuntimeLiveDrainDeliveryAccessor? liveDrainDeliveryAccessor = null,
        IRuntimeCheckpointCadenceResolver? cadenceResolver = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(schedulerDrainer);
        ArgumentNullException.ThrowIfNull(postCommitOutboxProcessor);
        ArgumentNullException.ThrowIfNull(schedulerDrainObservers);
        ArgumentNullException.ThrowIfNull(ownershipService);
        ArgumentNullException.ThrowIfNull(ownershipContextAccessor);

        _schedulerDrainer = schedulerDrainer;
        _postCommitOutboxProcessor = postCommitOutboxProcessor;
        _schedulerDrainObservers = schedulerDrainObservers.ToArray();
        _options = options ?? new WorkflowDrainOrchestratorOptions();
        _ownershipService = ownershipService;
        _ownershipContextAccessor = ownershipContextAccessor;
        _coalescingScopeFactory = coalescingScopeFactory;
        _liveDrainDeliveryAccessor = liveDrainDeliveryAccessor;
        _cadenceResolver = cadenceResolver;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        // heartbeat to operational state, giving the recovery scanner real data. Renewal keeps long-running drains from
        // expiring their own lease; process failure leaves the lease in place so interrupted execution stays detectable,
        // while every in-process completion path stops renewal and releases it to avoid false-positive recovery. Both
        // collaborators are required by construction (C1), so there is no unfenced fallback path.
        var lease = await _ownershipService.AcquireAsync(request.WorkflowExecutionId, cancellationToken);
        using (_ownershipContextAccessor.Push(lease))
        using (var renewalStop = new CancellationTokenSource())
        using (var drainCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            var renewalTask = RenewOwnershipUntilStoppedAsync(lease, renewalStop.Token, drainCancellation);
            RuntimeSchedulerDrainResult? result = null;
            Exception? drainFailure = null;
            Exception? renewalFailure = null;
            Exception? releaseFailure = null;
            try
            {
                result = await DrainCoreAsync(envelope, request, drainCancellation.Token);
            }
            catch (Exception exception)
            {
                drainFailure = exception;
            }
            finally
            {
                await renewalStop.CancelAsync();
                try
                {
                    await renewalTask;
                }
                catch (OperationCanceledException) when (renewalStop.IsCancellationRequested)
                {
                    // Expected when the drain completes before the next heartbeat cadence.
                }
                catch (Exception exception)
                {
                    renewalFailure = exception;
                }

                try
                {
                    var release = await _ownershipService.ReleaseAsync(lease, CancellationToken.None);
                    if (!release.Succeeded && release.Status != RuntimeExecutionOwnershipTransitionStatus.AlreadyApplied)
                        releaseFailure = new RuntimeExecutionOwnershipLostException(lease, "release", release.Status);
                }
                catch (Exception exception)
                {
                    releaseFailure = new RuntimeExecutionOwnershipLostException(lease, "release", transitionStatus: null, exception);
                }
            }

            if (renewalFailure is not null)
                ExceptionDispatchInfo.Capture(renewalFailure).Throw();

            if (drainFailure is not null)
                ExceptionDispatchInfo.Capture(drainFailure).Throw();

            if (releaseFailure is not null)
                ExceptionDispatchInfo.Capture(releaseFailure).Throw();

            return result ?? throw new InvalidOperationException("Workflow execution draining completed without a result.");
        }
    }

    private async Task RenewOwnershipUntilStoppedAsync(
        RuntimeExecutionLease lease,
        CancellationToken stopToken,
        CancellationTokenSource drainCancellation)
    {
        var duration = lease.ExpiresAt - lease.AcquiredAt;
        var cadence = TimeSpan.FromTicks(Math.Max(1, duration.Ticks / 3));
        while (true)
        {
            await Task.Delay(cadence, _timeProvider, stopToken);
            RuntimeExecutionOwnershipTransitionResult heartbeat;
            try
            {
                heartbeat = await _ownershipService.HeartbeatAsync(lease, stopToken);
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await drainCancellation.CancelAsync();
                throw new RuntimeExecutionOwnershipLostException(lease, "heartbeat", transitionStatus: null, exception);
            }

            if (heartbeat.Succeeded || heartbeat.Status == RuntimeExecutionOwnershipTransitionStatus.AlreadyApplied)
                continue;

            await drainCancellation.CancelAsync();
            throw new RuntimeExecutionOwnershipLostException(lease, "heartbeat", heartbeat.Status);
        }
    }

    private async ValueTask<RuntimeSchedulerDrainResult> DrainCoreAsync(
        WorkflowExecutionCommandEnvelope envelope,
        RuntimeSchedulerDrainRequest request,
        CancellationToken cancellationToken)
    {
        // Default path: no coalescing scope factory registered, so the drain runs with Immediate persistence.
        if (_coalescingScopeFactory is null)
            return await DrainImmediateAsync(envelope, request, cancellationToken);

        // Coalescing host, but cadence is resolved per execution (ADR 0032 R5). A workflow that authored Immediate must
        // run Immediate even though the host default is Coalesced: skip establishing the session entirely and take the
        // immediate path, so no relaxable checkpoint is deferred for this run. The mandatory-boundary set is unaffected
        // either way — those flush immediately under both policies. Authored cadence is read off the pinned executable
        // (or the run's own per-run stamp on resume); when no resolver is registered the host default (coalesce) stands.
        var cadence = _cadenceResolver is not null
            ? await _cadenceResolver.ResolveAsync(envelope, cancellationToken)
            : null;

        if (cadence is { Coalesced: false })
            return await DrainImmediateAsync(envelope, request, cancellationToken);

        // Coalescing path: establish the ambient session for the drain, then fold-and-flush the buffered segment at
        // quiescence. The flush runs inside the active ownership scope so W5 fencing gates the single durable write. If
        // the drain throws, the flush is skipped and the scope is disposed with its buffer discarded, so a crash
        // mid-segment replays from the last flushed state plus durable scheduler-queue redelivery. A null cadence (no
        // resolver registered) coalesces with the host-configured cap, byte-identical to pre-R5 behavior.
        await using var scope = _coalescingScopeFactory.Begin(request.WorkflowExecutionId, cadence?.MaxSegmentCheckpoints);
        var drainResult = await DrainSchedulerAndPostCommitWorkAsync(request, cancellationToken);
        await scope.FlushAtQuiescenceAsync(cancellationToken);
        await NotifyObserversAsync(envelope, drainResult, cancellationToken);
        return drainResult;
    }

    // Immediate persistence for a single drain. While this live drain owns the execution (bounded by the RT-2
    // single-writer lease), push a delivery scope so the post-commit outbox processor delivers EnqueueSchedulerWork
    // intents in-memory (idempotent enqueue + direct Delivered mark) instead of taking the durable claim round-trip
    // (WU-2). Deliberately NOT used on the coalescing path: there the overlay session is authoritative and folds
    // continuations itself. Reused both when no coalescing factory is registered and when a per-run authored Immediate
    // cadence opts this run out of coalescing on an otherwise-coalesced host (ADR 0032 R5).
    private async ValueTask<RuntimeSchedulerDrainResult> DrainImmediateAsync(
        WorkflowExecutionCommandEnvelope envelope,
        RuntimeSchedulerDrainRequest request,
        CancellationToken cancellationToken)
    {
        using var liveDrainScope = _liveDrainDeliveryAccessor is { } accessor
            ? accessor.Push(new RuntimeLiveDrainDeliveryScope(request.WorkflowExecutionId))
            : null;
        var plainResult = await DrainSchedulerAndPostCommitWorkAsync(request, cancellationToken);
        await NotifyObserversAsync(envelope, plainResult, cancellationToken);
        return plainResult;
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
            throw new DrainCycleLimitExceededException(request.WorkflowExecutionId, _options.MaxDrainCycles);

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
