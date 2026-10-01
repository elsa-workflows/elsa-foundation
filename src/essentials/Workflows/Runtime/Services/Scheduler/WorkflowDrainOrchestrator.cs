using System.Runtime.ExceptionServices;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Scheduler;

public sealed class WorkflowDrainOrchestrator : IWorkflowDrainOrchestrator
{
    // Polling for another deliverer's claim on a continuation: a live deliverer finishes within a few store round trips,
    // so the first polls are short; a stuck or dead one is waited out at a slower pace until its claim lapses.
    private static readonly TimeSpan FirstClaimPoll = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan MaxClaimPoll = TimeSpan.FromMilliseconds(500);

    private readonly IWorkflowSchedulerDrainer _schedulerDrainer;
    private readonly IRuntimePostCommitOutboxProcessor _postCommitOutboxProcessor;
    private readonly IReadOnlyCollection<IWorkflowSchedulerDrainObserver> _schedulerDrainObservers;
    private readonly CheckpointRuleViolationWorkflowFaulter _checkpointRuleViolationFaulter;
    private readonly WorkflowDrainOrchestratorOptions _options;
    private readonly IRuntimeExecutionOwnershipService _ownershipService;
    private readonly IRuntimeExecutionOwnershipContextAccessor _ownershipContextAccessor;
    private readonly IPersistenceOperationScopeFactory? _heartbeatScopeFactory;
    private readonly IRuntimeCoalescingDrainScopeFactory? _coalescingScopeFactory;
    private readonly IRuntimeLiveDrainDeliveryAccessor? _liveDrainDeliveryAccessor;
    private readonly IRuntimeCheckpointCadenceResolver? _cadenceResolver;
    private readonly IRuntimePostCommitOutboxClaimStore _claimStore;
    private readonly IPostCommitOutboxLookupStore _outboxLookupStore;
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the orchestrator for a caller-owned ownership service. C1 (#1227): the six telescoping constructors
    /// collapsed into this single primary constructor: the required collaborators followed by optional collaborators
    /// that default to their no-op/system implementations. The ownership service and the ownership context accessor are <b>required by
    /// construction</b> so the single-writer lease, which fences every checkpoint commit made during the drain
    /// and cancels the drain when the lease is lost, can never be silently disabled by picking a narrower
    /// constructor. The drain observers are required for the same reason: they decide fault outcomes (blocking
    /// incidents, poison projection, incident strategy resolution), so the set must be handed in deliberately. So is
    /// the checkpoint rule violation faulter (#1780), which decides the outcome of an execution a rule refused. The
    /// outbox claim store, the outbox lookup store and the scheduler work queue are required too (#2225): they are how
    /// a drain finds a continuation another deliverer took from it before it reports quiescence, and a drain without
    /// them would report <see cref="RuntimeSchedulerDrainStopReason.Quiesced"/>, and its command Accepted, with its own
    /// next step undrained. The legacy path is rejected when the default ownership service is backed by scoped
    /// persistence; use <see cref="CreateScoped"/> to renew through an isolated partition-bound operation scope.
    /// </summary>
    public WorkflowDrainOrchestrator(
        IWorkflowSchedulerDrainer schedulerDrainer,
        IRuntimePostCommitOutboxProcessor postCommitOutboxProcessor,
        IEnumerable<IWorkflowSchedulerDrainObserver> schedulerDrainObservers,
        CheckpointRuleViolationWorkflowFaulter checkpointRuleViolationFaulter,
        IRuntimeExecutionOwnershipService ownershipService,
        IRuntimeExecutionOwnershipContextAccessor ownershipContextAccessor,
        IRuntimePostCommitOutboxClaimStore outboxClaimStore,
        IPostCommitOutboxLookupStore outboxLookupStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        WorkflowDrainOrchestratorOptions? options = null,
        IRuntimeCoalescingDrainScopeFactory? coalescingScopeFactory = null,
        IRuntimeLiveDrainDeliveryAccessor? liveDrainDeliveryAccessor = null,
        IRuntimeCheckpointCadenceResolver? cadenceResolver = null,
        TimeProvider? timeProvider = null)
        : this(
            schedulerDrainer,
            postCommitOutboxProcessor,
            schedulerDrainObservers,
            checkpointRuleViolationFaulter,
            ownershipService,
            ownershipContextAccessor,
            outboxClaimStore,
            outboxLookupStore,
            schedulerWorkQueue,
            options,
            coalescingScopeFactory,
            liveDrainDeliveryAccessor,
            cadenceResolver,
            timeProvider,
            heartbeatScopeFactory: null)
    {
    }

    /// <summary>
    /// Creates an orchestrator whose lease heartbeats run in a fresh persistence operation scope bound to the command
    /// partition. The foreground scope continues to own acquisition, checkpoint work, and release.
    /// </summary>
    public static WorkflowDrainOrchestrator CreateScoped(
        IPersistenceOperationScopeFactory heartbeatScopeFactory,
        IWorkflowSchedulerDrainer schedulerDrainer,
        IRuntimePostCommitOutboxProcessor postCommitOutboxProcessor,
        IEnumerable<IWorkflowSchedulerDrainObserver> schedulerDrainObservers,
        CheckpointRuleViolationWorkflowFaulter checkpointRuleViolationFaulter,
        IRuntimeExecutionOwnershipService ownershipService,
        IRuntimeExecutionOwnershipContextAccessor ownershipContextAccessor,
        IRuntimePostCommitOutboxClaimStore outboxClaimStore,
        IPostCommitOutboxLookupStore outboxLookupStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        WorkflowDrainOrchestratorOptions? options = null,
        IRuntimeCoalescingDrainScopeFactory? coalescingScopeFactory = null,
        IRuntimeLiveDrainDeliveryAccessor? liveDrainDeliveryAccessor = null,
        IRuntimeCheckpointCadenceResolver? cadenceResolver = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(heartbeatScopeFactory);
        return new WorkflowDrainOrchestrator(
            schedulerDrainer,
            postCommitOutboxProcessor,
            schedulerDrainObservers,
            checkpointRuleViolationFaulter,
            ownershipService,
            ownershipContextAccessor,
            outboxClaimStore,
            outboxLookupStore,
            schedulerWorkQueue,
            options,
            coalescingScopeFactory,
            liveDrainDeliveryAccessor,
            cadenceResolver,
            timeProvider,
            heartbeatScopeFactory);
    }

    private WorkflowDrainOrchestrator(
        IWorkflowSchedulerDrainer schedulerDrainer,
        IRuntimePostCommitOutboxProcessor postCommitOutboxProcessor,
        IEnumerable<IWorkflowSchedulerDrainObserver> schedulerDrainObservers,
        CheckpointRuleViolationWorkflowFaulter checkpointRuleViolationFaulter,
        IRuntimeExecutionOwnershipService ownershipService,
        IRuntimeExecutionOwnershipContextAccessor ownershipContextAccessor,
        IRuntimePostCommitOutboxClaimStore outboxClaimStore,
        IPostCommitOutboxLookupStore outboxLookupStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        WorkflowDrainOrchestratorOptions? options,
        IRuntimeCoalescingDrainScopeFactory? coalescingScopeFactory,
        IRuntimeLiveDrainDeliveryAccessor? liveDrainDeliveryAccessor,
        IRuntimeCheckpointCadenceResolver? cadenceResolver,
        TimeProvider? timeProvider,
        IPersistenceOperationScopeFactory? heartbeatScopeFactory)
    {
        ArgumentNullException.ThrowIfNull(schedulerDrainer);
        ArgumentNullException.ThrowIfNull(postCommitOutboxProcessor);
        ArgumentNullException.ThrowIfNull(schedulerDrainObservers);
        ArgumentNullException.ThrowIfNull(checkpointRuleViolationFaulter);
        ArgumentNullException.ThrowIfNull(ownershipService);
        ArgumentNullException.ThrowIfNull(ownershipContextAccessor);
        ArgumentNullException.ThrowIfNull(outboxClaimStore);
        ArgumentNullException.ThrowIfNull(outboxLookupStore);
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);

        if (heartbeatScopeFactory is null && ownershipService is RuntimeExecutionOwnershipService runtimeOwnership &&
            runtimeOwnership.RequiresIsolatedHeartbeatScope)
        {
            throw new InvalidOperationException(
                "The default ownership service uses scoped persistence and requires heartbeat isolation. " +
                "Construct the orchestrator with WorkflowDrainOrchestrator.CreateScoped and an " +
                "IPersistenceOperationScopeFactory.");
        }

        _schedulerDrainer = schedulerDrainer;
        _postCommitOutboxProcessor = postCommitOutboxProcessor;
        _schedulerDrainObservers = schedulerDrainObservers.ToArray();
        _checkpointRuleViolationFaulter = checkpointRuleViolationFaulter;
        _options = options ?? new WorkflowDrainOrchestratorOptions();
        _ownershipService = ownershipService;
        _ownershipContextAccessor = ownershipContextAccessor;
        _heartbeatScopeFactory = heartbeatScopeFactory;
        _coalescingScopeFactory = coalescingScopeFactory;
        _liveDrainDeliveryAccessor = liveDrainDeliveryAccessor;
        _cadenceResolver = cadenceResolver;
        _claimStore = outboxClaimStore;
        _outboxLookupStore = outboxLookupStore;
        _schedulerWorkQueue = schedulerWorkQueue;
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

        // Single-writer ownership: claim a fencing lease for this drain and expose it as the active ownership
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
            var renewalTask = RenewOwnershipUntilStoppedAsync(lease, envelope.Partition, renewalStop.Token, drainCancellation);
            RuntimeSchedulerDrainResult? result = null;
            Exception? drainFailure = null;
            Exception? renewalFailure = null;
            Exception? releaseFailure = null;
            try
            {
                try
                {
                    result = await DrainCoreAsync(envelope, request, drainCancellation.Token);
                }
                catch (Exception exception)
                {
                    drainFailure = exception;
                }

                // #1780: a commit a checkpoint rule refused is refused again on every redelivery, so the execution is
                // faulted instead of left non-terminal. This runs while the lease is still held, so the fault commit is
                // fenced like the drain's own. The drain's result or failure is reported unchanged; only a fault commit
                // that itself fails is added to it, because then the execution was not faulted. The envelope goes with
                // it for #1799: a refused FIRST commit leaves no state to fault, and for a dispatched child the command
                // is the only place its identity still exists.
                try
                {
                    await _checkpointRuleViolationFaulter.FaultIfCheckpointRuleViolatedAsync(
                        request.WorkflowExecutionId,
                        result,
                        drainFailure,
                        envelope,
                        drainCancellation.Token);
                }
                catch (Exception exception)
                {
                    drainFailure = drainFailure is null ? exception : new AggregateException(drainFailure, exception);
                }
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
        WorkflowExecutionPartition partition,
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
                heartbeat = await HeartbeatAsync(lease, partition, stopToken);
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

    private async ValueTask<RuntimeExecutionOwnershipTransitionResult> HeartbeatAsync(
        RuntimeExecutionLease lease,
        WorkflowExecutionPartition partition,
        CancellationToken cancellationToken)
    {
        if (_heartbeatScopeFactory is null)
            return await _ownershipService.HeartbeatAsync(lease, cancellationToken);

        await using var scope = await _heartbeatScopeFactory.CreateAsync(
            new PersistenceScope(partition.Value),
            cancellationToken);
        var ownershipService = scope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>();
        return await ownershipService.HeartbeatAsync(lease, cancellationToken);
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
        // quiescence. The flush runs inside the active ownership scope so single-writer fencing gates the single durable write. If
        // the drain throws, the flush is skipped and the scope is disposed with its buffer discarded, so a crash
        // mid-segment replays from the last flushed state plus durable scheduler-queue redelivery. A null cadence (no
        // resolver registered) coalesces with the host-configured cap, byte-identical to pre-R5 behavior.
        await using var scope = _coalescingScopeFactory.Begin(request.WorkflowExecutionId, cadence?.MaxSegmentCheckpoints);
        var drainResult = await DrainSchedulerAndPostCommitWorkAsync(request, cancellationToken);
        await scope.FlushAtQuiescenceAsync(cancellationToken);
        await NotifyObserversAsync(envelope, drainResult, cancellationToken);
        return drainResult;
    }

    // Immediate persistence for a single drain. While this live drain owns the execution (bounded by the
    // single-writer lease), push a delivery scope so the post-commit outbox processor delivers EnqueueSchedulerWork
    // intents in-memory (idempotent enqueue + direct Delivered mark) instead of taking the durable claim round-trip.
    // Deliberately NOT used on the coalescing path: there the overlay session is authoritative and folds
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
        var completed = false;
        var continuationWait = new ContinuationWaitDeadline(_options.ContinuationClaimWaitLimit);

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
                ContinuationDelivery(request.WorkflowExecutionId),
                cancellationToken);
            outboxDeliveryResults.Add(outboxResult);

            if (outboxResult.DeliveredCount > 0)
                continue;

            var settlement = outboxDeliveryResults.Any(result => result.FailedCount > 0)
                ? ContinuationSettlement.Undelivered
                : await SettleContinuationsAsync(request, drainResult, outboxResult, outboxDeliveryResults, continuationWait, cancellationToken);
            if (settlement == ContinuationSettlement.DrainAgain)
                continue;

            stopReason = settlement == ContinuationSettlement.Quiesced
                ? RuntimeSchedulerDrainStopReason.Quiesced
                : RuntimeSchedulerDrainStopReason.OutboxDeliveryFailed;
            completed = true;
            break;
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

    private RuntimePostCommitOutboxProcessRequest ContinuationDelivery(string workflowExecutionId) =>
        new(
            limit: _options.OutboxDeliveryBatchSize,
            workflowExecutionId: workflowExecutionId,
            intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork);

    /// <summary>
    /// Decides whether a delivery step that delivered nothing ends the drain (#2225). It does only when no other deliverer
    /// has this execution's continuations. The resumption sweep claims across every execution, so it can take one between
    /// this drain's commit and its delivery step: the step then finds nothing deliverable, or reports the item superseded.
    /// Reporting quiescence there returned the command before its own next step had run, so a start answered Accepted
    /// before its first bookmark existed. Instead the drain waits, bounded, for a continuation another deliverer holds,
    /// takes it back once that claim lapses, and drains whatever the other deliverer queued. The drain still holds the
    /// execution's ownership lease, so nothing else would drain that work before the command returned. A continuation the
    /// other deliverer failed ends the drain as a failed delivery of its own would.
    /// </summary>
    private async ValueTask<ContinuationSettlement> SettleContinuationsAsync(
        RuntimeSchedulerDrainRequest request,
        RuntimeSchedulerDrainResult drainResult,
        RuntimePostCommitOutboxProcessResult outboxResult,
        List<RuntimePostCommitOutboxProcessResult> outboxDeliveryResults,
        ContinuationWaitDeadline continuationWait,
        CancellationToken cancellationToken)
    {
        var workflowExecutionId = request.WorkflowExecutionId;
        var taken = outboxResult.Items
            .Where(item => item.IsSuperseded)
            .Select(item => item.OutboxItemId)
            .ToHashSet(StringComparer.Ordinal);
        var poll = FirstClaimPoll;
        var held = await ListHeldContinuationsAsync(workflowExecutionId, cancellationToken);
        while (held.Count > 0)
        {
            // A failed attempt is not known to have queued anything, and its retry, after the policy's delay, is the sweep's
            // to make, so it ends the drain exactly as a failed delivery of the drain's own would. This also covers an attempt
            // that failed before this drain's first read.
            if (held.Any(item => item.Status == RuntimePostCommitOutboxStatus.FailedRetryable))
                return ContinuationSettlement.Undelivered;

            taken.UnionWith(held.Select(item => item.OutboxItemId));
            var now = _timeProvider.GetUtcNow();
            if (held.Any(item => RuntimePostCommitOutboxClaimTransitions.ClaimableAt(item) <= now))
            {
                var delivery = await DeliverLapsedContinuationsAsync(workflowExecutionId, cancellationToken);
                outboxDeliveryResults.Add(delivery);
                if (delivery.FailedCount > 0)
                    return ContinuationSettlement.Undelivered;
                if (delivery.DeliveredCount > 0)
                    return ContinuationSettlement.DrainAgain;
            }

            var deadline = continuationWait.StartOrGet(now);
            if (now >= deadline)
                return ContinuationSettlement.Undelivered;

            await Task.Delay(UntilNextPoll(poll, held, now, deadline), _timeProvider, cancellationToken);
            poll = poll * 2 < MaxClaimPoll ? poll * 2 : MaxClaimPoll;
            held = await ListHeldContinuationsAsync(workflowExecutionId, cancellationToken);
        }

        // The other deliverer finished. A delivery that failed for good is no longer listed, so the items are looked up: a
        // failure ends the drain as above, otherwise the work they queued is drained next.
        if (taken.Count > 0)
            return await AnyDeliveryFailedAsync(taken, cancellationToken)
                ? ContinuationSettlement.Undelivered
                : ContinuationSettlement.DrainAgain;

        return await HasUndrainedWorkAsync(request, drainResult, cancellationToken)
            ? ContinuationSettlement.DrainAgain
            : ContinuationSettlement.Quiesced;
    }

    // The execution's continuations another deliverer has taken on and not settled: claimed, or failed and awaiting a retry.
    // A continuation's every failed attempt but its last is FailedRetryable (RuntimeSchedulerPostCommitIntentDispatcher
    // .RetryPolicy), so a transient failure on another deliverer is listed whenever it happened. A FailedFinal item is
    // terminal and stays in the outbox, so it is not listed: every later drain of the execution would report a failed
    // delivery, and skip incident resolution, for good. The cost is that a continuation whose attempts were all made and
    // failed by other deliverers before this drain's first read goes unseen (#2225, docs/runtime-durable-resumption.md).
    private ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> ListHeldContinuationsAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken) =>
        _claimStore.ListClaimedAsync(
            new RuntimePostCommitOutboxClaimedQuery(
                workflowExecutionId,
                RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
                _options.OutboxDeliveryBatchSize),
            cancellationToken);

    // A lapsed claim keeps its fencing token, and a claim-free delivery cannot complete a fenced item (#1798), so the drain
    // takes its continuation back through the durable claim path: with no live-drain scope ambient, the processor claims
    // what is claimable, the lapsed item included. If the other deliverer is still alive, its renewal before dispatch now
    // fails and it skips the item.
    private async ValueTask<RuntimePostCommitOutboxProcessResult> DeliverLapsedContinuationsAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        using (_liveDrainDeliveryAccessor?.Push(null))
            return await _postCommitOutboxProcessor.ProcessAsync(ContinuationDelivery(workflowExecutionId), cancellationToken);
    }

    // Wakes no later than the earliest lapse, so a dead deliverer's item is taken back as soon as it can be.
    private static TimeSpan UntilNextPoll(
        TimeSpan poll,
        IReadOnlyCollection<RuntimePostCommitOutboxItem> claimed,
        DateTimeOffset now,
        DateTimeOffset deadline)
    {
        var wait = deadline - now < poll ? deadline - now : poll;
        var untilLapse = claimed.Min(RuntimePostCommitOutboxClaimTransitions.ClaimableAt) - now;
        return untilLapse > TimeSpan.Zero && untilLapse < wait ? untilLapse : wait;
    }

    private async ValueTask<bool> AnyDeliveryFailedAsync(IEnumerable<string> outboxItemIds, CancellationToken cancellationToken)
    {
        foreach (var outboxItemId in outboxItemIds)
        {
            var item = await _outboxLookupStore.FindAsync(outboxItemId, cancellationToken);
            if (item?.Status is RuntimePostCommitOutboxStatus.FailedRetryable or RuntimePostCommitOutboxStatus.FailedFinal)
                return true;
        }

        return false;
    }

    // Another deliverer can also finish a continuation completely between this drain's commit and its delivery step. That
    // leaves no claim to wait for, only work in the queue. The queue is read only after a scheduler drain that ran dry: one
    // that stopped at the request's work-item budget leaves work queued on purpose, one that reached a terminal status must
    // not run the rest, and one that claimed nothing could not run what is there.
    private async ValueTask<bool> HasUndrainedWorkAsync(
        RuntimeSchedulerDrainRequest request,
        RuntimeSchedulerDrainResult drainResult,
        CancellationToken cancellationToken)
    {
        if (drainResult.Items.Count == 0 ||
            drainResult.StoppedOnTerminalStatus ||
            request.MaxWorkItems is { } budget && drainResult.Items.Count >= budget)
            return false;

        var queued = await _schedulerWorkQueue.ListAsync(
            new RuntimeSchedulerWorkQuery(request.WorkflowExecutionId, limit: 1),
            cancellationToken);
        return queued.Items.Count > 0;
    }

    private enum ContinuationSettlement
    {
        /// <summary>No continuation of the execution is out of this drain's hands and no queued work is left to it.</summary>
        Quiesced,

        /// <summary>Work for the execution may be queued that this drain has not run yet.</summary>
        DrainAgain,

        /// <summary>
        /// A continuation was not delivered: another deliverer still held it when the wait limit passed, or failed it. The
        /// drain stops with <see cref="RuntimeSchedulerDrainStopReason.OutboxDeliveryFailed"/>, the status it reports for a
        /// failed delivery of its own; the durable item stays with the sweep.
        /// </summary>
        Undelivered
    }

    /// <summary>
    /// The one deadline every wait for another deliverer shares within a drain request (#2225). It starts at the drain's
    /// first wait and later waits do not reset it, so the drain's cycles cannot multiply
    /// <see cref="WorkflowDrainOrchestratorOptions.ContinuationClaimWaitLimit"/>.
    /// </summary>
    private sealed class ContinuationWaitDeadline(TimeSpan limit)
    {
        private DateTimeOffset? _deadline;

        public DateTimeOffset StartOrGet(DateTimeOffset now) => _deadline ??= now + limit;
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
