using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Services.Recovery;

/// <summary>
/// Default <see cref="IRuntimeResumptionService"/>. One sweep pass performs three steps:
/// system-wide post-commit outbox delivery (catches items stranded between checkpoint commit and
/// dispatch, including due <c>FailedRetryable</c> retries), backlog discovery (durably queued
/// scheduler work plus recovery-scanner candidates), and per-execution re-drive through the agent
/// mailbox with a <see cref="WorkflowExecutionCommandKind.RunSchedulerWork"/> envelope.
/// </summary>
/// <remarks>
/// <para>
/// Re-driven envelopes use <see cref="WorkflowExecutionCommandDeliveryMode.AtLeastOnce"/> with a fresh
/// idempotency key per sweep: an execution whose backlog remains (e.g. dispatch raced a crash) is
/// simply re-driven on the next sweep, while the enqueue path's own dedup keeps the underlying work
/// items single-instance. Failures re-driving one execution are recorded on the sweep result and do
/// not abort the sweep; callers (the resumption pump) own logging and backoff.
/// </para>
/// <para>
/// <b>Discovery (#2188).</b> Backlog discovery lists only executions whose scheduler work is claimable now
/// (<see cref="IWorkflowSchedulerWorkQueue.ListClaimableWorkflowExecutionIdsAsync"/>) and walks the backlog from the
/// position the previous pass reached (<see cref="RuntimeResumptionDiscoveryStateStore"/>), so neither hidden work nor
/// a fixed set of executions can hold the window. Every candidate, from the backlog, the recovery scanner or a
/// candidate source, is re-driven only when the drainer's own pause gate would let its next item advance: a held
/// execution's drain would stop at the gate and leave one more <c>RunSchedulerWork</c> row behind its head. A held
/// candidate counts as dealt with, and when the gate cannot be consulted the execution is not re-driven either, since
/// its drain would consult the same gate. Under <see cref="RuntimeResumptionSweepRequest.MaxExecutionsPerSweep"/> the
/// recovery scanner keeps half the cap (at least one slot, at most its batch size) and the backlog the rest; either
/// side may use what the other leaves, and a cap of one alternates between them. A queue without claimable discovery
/// keeps the earlier first-page listing through
/// <see cref="IWorkflowSchedulerWorkQueue.ListPendingWorkflowExecutionIdsAsync"/>, and the sweep warns once for its type.
/// </para>
/// <para>
/// <b>Terminal-execution short-circuit (spec 113).</b> Backlog discovery has no terminal-status filter, so a
/// scheduler work item stranded by the drainer's terminal-status guard (a sibling item enqueued before a parallel
/// fork ran <c>Finish</c>, or a post-commit <c>EnqueueSchedulerWork</c> intent delivered after the terminal
/// checkpoint) keeps the completed execution discoverable forever. Re-driving
/// it enqueues yet another <c>RunSchedulerWork</c> item that the terminal guard again refuses to dispatch, so the
/// residue grows by one row per sweep and the pump emits a fresh <c>elsa.runtime.drain</c> span every tick — pure
/// churn that never converges. This sweep therefore reads workflow status for every discovered execution and, for
/// executions already in a terminal status, <b>purges</b> the residual scheduler work instead of re-driving it:
/// terminal status is monotonic and the drainer already refuses to run post-terminal work, so removing the residue
/// is exactly the outcome the terminal guard intends. Genuinely-suspended (non-terminal) executions are re-driven
/// unchanged, preserving redelivery for late deliveries.
/// </para>
/// </remarks>
public sealed class RuntimeResumptionService(
    IRuntimePostCommitOutboxProcessor outboxProcessor,
    IWorkflowSchedulerWorkQueue workQueue,
    IRuntimeRecoveryScanner recoveryScanner,
    IWorkflowExecutionActorProvider agentProvider,
    IRuntimeExecutionIdGenerator idGenerator,
    TimeProvider timeProvider,
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IWorkflowSchedulerPauseGate pauseGate,
    RuntimeResumptionDiscoveryStateStore discoveryStateStore,
    IWorkflowExecutionPartitionAccessor? partitionAccessor = null,
    IRuntimeRecoverySweepCursorStore? recoveryCursorStore = null,
    IPersistenceAccessContextAccessor? persistenceAccessContextAccessor = null,
    IEnumerable<IRuntimeRecoveryCandidateSource>? recoveryCandidateSources = null,
    ILogger<RuntimeResumptionService>? logger = null) : IRuntimeResumptionService
{
    private const string DispatchSource = "runtime-resumption";
    private readonly IRuntimeRecoverySweepCursorStore sweepCursorStore = recoveryCursorStore ?? new InMemoryRuntimeRecoverySweepCursorStore();
    private readonly IRuntimeRecoveryCandidateSource[] candidateSources = recoveryCandidateSources?.ToArray() ?? [];
    private readonly ILogger<RuntimeResumptionService> logger = logger ?? NullLogger<RuntimeResumptionService>.Instance;

    // Safety cap on residual-item purge pages per terminal execution per sweep, so a provider that never actually
    // removes an item (Delete returning false) cannot spin this loop forever. Bounded residue is expected — one
    // stranded RunSchedulerWork row per prior sweep — so a handful of BacklogBatchSize pages always suffices.
    private const int MaxPurgePagesPerExecution = 16;

    // Bounds the backlog pages one pass reads while passing held or excluded executions, so a large paused set costs a
    // bounded amount of work per sweep; the walk carries on from where the pass stopped.
    private const int MaxBacklogPagesPerSweep = 10;

    public async ValueTask<RuntimeResumptionSweepResult> SweepAsync(RuntimeResumptionSweepRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var outboxResult = await outboxProcessor.ProcessAsync(
            new RuntimePostCommitOutboxProcessRequest(
                limit: request.OutboxBatchSize,
                workflowExecutionId: null,
                intentKind: null),
            cancellationToken);

        var discovery = await DiscoverExecutionIdsAsync(request, cancellationToken);
        var executionIds = discovery.ExecutionIds;

        var dispatches = new List<RuntimeResumptionDispatch>(executionIds.Count);
        var terminalExecutionsPurged = 0;
        var purgedWorkItemCount = 0;
        foreach (var workflowExecutionId in executionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Never re-drive a terminal execution: it can only accumulate stranded RunSchedulerWork items the drainer
            // refuses to dispatch. Purge its residue so backlog discovery stops resurfacing it and the perpetual
            // per-tick drain span ends. (spec 113)
            if (await IsTerminalAsync(workflowExecutionId, cancellationToken))
            {
                purgedWorkItemCount += await PurgeResidualSchedulerWorkAsync(request, workflowExecutionId, cancellationToken);
                await ReapTerminalMailboxAsync(workflowExecutionId, cancellationToken);
                terminalExecutionsPurged++;
                continue;
            }

            dispatches.Add(await RedriveAsync(workflowExecutionId, cancellationToken));
        }

        CommitRecoveryCursor(discovery, dispatches);
        // Unlike the recovery cursor, the backlog position always moves on, failed dispatches included: it is a position
        // in a walk, not a claim on a page. A failed execution keeps its queued work and the pump's per-execution
        // backoff, and rewinding would let a page of failing executions hold the window again.
        discoveryStateStore.Set(discovery.Scope, discovery.NextState);
        await SettleSourcedCandidatesAsync(discovery, dispatches, cancellationToken);
        WarnOfPauseCheckFailures(discovery.PauseCheck);

        var result = new RuntimeResumptionSweepResult(
            outboxAttemptedCount: outboxResult.AttemptedCount,
            outboxDeliveredCount: outboxResult.DeliveredCount,
            outboxFailedCount: outboxResult.FailedCount,
            dispatches: dispatches,
            terminalExecutionsPurged: terminalExecutionsPurged,
            purgedWorkItemCount: purgedWorkItemCount);

        return result;
    }

    private async ValueTask<bool> IsTerminalAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        var state = await workflowExecutionStateStore.FindAsync(workflowExecutionId, cancellationToken);
        return state is not null && state.Status.IsTerminal();
    }

    // Straggler reaper (#542 / spec 128). The eager terminal-eviction trigger runs at drain end on the node that owns
    // the mailbox; this reaps a mailbox that outlived its execution because eviction was disabled, skipped (e.g. a
    // cancelled dispatch token), or never fired (the terminal status was reached by a post-commit intent or a sibling
    // fork rather than the dispatched command). PassivateAsync is idempotent — a no-op when no mailbox exists — and on
    // the distributed provider it also releases the placement lease for the completed execution.
    private async ValueTask ReapTerminalMailboxAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        await agentProvider.PassivateAsync(
            new WorkflowExecutionActorPassivationRequest(
                workflowExecutionId: workflowExecutionId,
                boundary: WorkflowExecutionActorPassivationBoundary.ProviderSafeBoundary,
                requestedAt: timeProvider.GetUtcNow(),
                reason: "runtime-resumption terminal reaper",
                partition: CurrentPartition()),
            cancellationToken);
    }

    // Deletes every scheduler work item still queued for a terminal execution. Reads a bounded page, deletes each
    // item by identity (idempotent — a concurrent completion just yields Delete=false), and repeats until the queue
    // is empty or the safety cap is hit. Returns the number of items removed.
    private async ValueTask<int> PurgeResidualSchedulerWorkAsync(
        RuntimeResumptionSweepRequest request,
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        var purged = 0;
        for (var page = 0; page < MaxPurgePagesPerExecution; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var listed = await workQueue.ListAsync(
                new RuntimeSchedulerWorkQuery(workflowExecutionId, limit: request.BacklogBatchSize),
                cancellationToken);
            if (listed.Items.Count == 0)
                break;

            foreach (var item in listed.Items)
            {
                if (await workQueue.DeleteAsync(workflowExecutionId, item.WorkItemId, cancellationToken))
                    purged++;
            }
        }

        return purged;
    }

    private void CommitRecoveryCursor(
        RecoveryDiscovery discovery,
        IReadOnlyCollection<RuntimeResumptionDispatch> dispatches)
    {
        if (!discovery.ShouldUpdateCursor)
            return;

        // A scan cursor is a claim on the page it just returned. Do not commit that claim when a candidate could not
        // be re-driven: the next sweep must retry from the original cursor instead of silently partitioning the
        // failed execution out of recovery until the scan wraps around. Accepted, duplicate, and deferred dispatches
        // are durable queue outcomes and may advance the page; faulted/rejected outcomes explicitly rewind it. A held
        // candidate is not a failure: its drain would stop at the pause gate, so it is dealt with until the scan wraps.
        var failed = dispatches.Any(dispatch => dispatch.Outcome is
            RuntimeResumptionDispatchOutcome.Faulted or RuntimeResumptionDispatchOutcome.Rejected);
        var cursor = failed ? discovery.PreviousCursor : discovery.CursorToCommit;
        if (cursor is null)
            sweepCursorStore.Clear(discovery.Scope, discovery.Scanner);
        else
            sweepCursorStore.Set(discovery.Scope, discovery.Scanner, cursor);
    }

    private async ValueTask<RecoveryDiscovery> DiscoverExecutionIdsAsync(RuntimeResumptionSweepRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var scope = persistenceAccessContextAccessor?.Current.Scope?.Value ?? PersistenceScope.DefaultValue;
        var state = discoveryStateStore.Get(scope);
        var check = new PauseCheck(workQueue, pauseGate, request.ExcludedWorkflowExecutionIds);
        var sourced = await ListSourcedCandidatesAsync(request, cancellationToken);
        var sourcedIds = sourced.SelectMany(candidates => candidates.WorkflowExecutionIds).Distinct(StringComparer.Ordinal).ToArray();
        await check.ReadHeadsAsync(sourcedIds, cancellationToken);
        var backlog = await OpenBacklogWalkAsync(request, state, now, check, cancellationToken);
        var scannerName = RecoveryCursorKey(recoveryScanner, request.ExcludedWorkflowExecutionIds);
        var cursor = sweepCursorStore.Get(scope, scannerName);
        var scanLimit = Math.Min(request.RecoveryScanBatchSize, RuntimeStorePageRequest.MaximumLimit);
        if (cursor is not null &&
            (cursor.LeaseTimeout != request.LeaseTimeout ||
             cursor.HeartbeatTimeout != request.HeartbeatTimeout ||
             cursor.Limit != scanLimit))
        {
            // A provider cursor is only valid for the stable route/options set that created it. Restart the scan
            // cycle when a host changes those options rather than replaying a cursor under different predicates.
            sweepCursorStore.Clear(scope, scannerName);
            cursor = null;
        }

        var slots = new SlotPolicy(request.MaxExecutionsPerSweep, scanLimit, state.RecoveryHasSingleSlotTurn);
        var backlogDemand = await CountBacklogDemandAsync(slots.BacklogDemandWorthCounting, sourcedIds, backlog, check, cancellationToken);
        var recoveryLimit = slots.RecoveryLimit(backlogDemand);
        var scanNow = cursor?.ScanNow ?? now;
        RuntimeRecoveryCandidate[] candidates = [];
        RuntimeRecoverySweepCursor? cursorToCommit = null;
        var shouldUpdateCursor = false;
        if (recoveryLimit == 0)
        {
            // Only a cap of one, on the backlog's turn, leaves the scanner no slot. Its cursor stays untouched so no
            // candidate is skipped.
        }
        else if (recoveryScanner is not IRuntimeRecoveryPagedScanner { SupportsPaging: true })
        {
            // Keep source compatibility for a pre-paging/custom scanner (and for the in-memory scanner wrapped
            // around one), but do not silently treat its first bounded result as a resumable page. Its legacy
            // contract is a complete collection and has no cursor channel; the scanner owns any materialization,
            // while this sweep still clamps the dispatch contribution and never stores a fabricated continuation.
            var legacyCandidates = await recoveryScanner.ScanAsync(
                new RuntimeRecoveryScanRequest(
                    now: scanNow,
                    leaseTimeout: request.LeaseTimeout,
                    heartbeatTimeout: request.HeartbeatTimeout,
                    limit: scanLimit),
                cancellationToken);
            candidates = legacyCandidates.Take(recoveryLimit).ToArray();
        }
        else
        {
            var page = await recoveryScanner.ScanPageAsync(
                new RuntimeRecoveryScanRequest(
                    now: scanNow,
                    leaseTimeout: request.LeaseTimeout,
                    heartbeatTimeout: request.HeartbeatTimeout,
                    limit: recoveryLimit,
                    continuationToken: cursor?.ContinuationToken),
                cancellationToken);
            candidates = page.Items.ToArray();
            cursorToCommit = page.NextContinuationToken is { } next
                ? new RuntimeRecoverySweepCursor(scanNow, request.LeaseTimeout, request.HeartbeatTimeout, scanLimit, next)
                : null;
            shouldUpdateCursor = true;
        }

        await check.ReadHeadsAsync(candidates.Select(candidate => candidate.WorkflowExecutionId), cancellationToken);
        var selection = await SelectAsync(slots, check, candidates, sourcedIds, backlog, cancellationToken);
        return new RecoveryDiscovery(
            selection.ExecutionIds,
            selection.Held,
            scope,
            scannerName,
            cursor,
            cursorToCommit,
            shouldUpdateCursor,
            new RuntimeResumptionDiscoveryState(backlog.ResumeAfter(), selection.NextRecoveryTurn),
            check,
            sourced);
    }

    // Opens this pass's walk over durable backlog (#2188). A provider with claimable discovery lists only executions a
    // claim would serve right now, from where the previous pass stopped. The position is deliberately not partitioned by
    // the exclusion set, unlike the recovery cursor: exclusions change from sweep to sweep, and restarting the walk
    // whenever they did would bring the starvation back. An excluded execution keeps its queued work, so the next walk
    // finds it again. A provider without claimable discovery keeps its earlier first-page listing, with a warning.
    private async ValueTask<BacklogWalk> OpenBacklogWalkAsync(
        RuntimeResumptionSweepRequest request,
        RuntimeResumptionDiscoveryState state,
        DateTimeOffset now,
        PauseCheck check,
        CancellationToken cancellationToken)
    {
        if (!workQueue.SupportsClaimableBacklogDiscovery)
        {
            WarnOfFirstPageDiscovery(request.BacklogBatchSize);
            var pending = await workQueue.ListPendingWorkflowExecutionIdsAsync(request.BacklogBatchSize, cancellationToken);
            return await BacklogWalk.OverFirstPageAsync(check, pending, cancellationToken);
        }

        // Reading past the per-sweep cap cannot add a dispatch, so the cap bounds the page as well.
        var pageSize = Math.Min(request.BacklogBatchSize, request.MaxExecutionsPerSweep ?? int.MaxValue);
        return BacklogWalk.After(
            check,
            state.BacklogAfterWorkflowExecutionId,
            pageSize,
            (after, token) => workQueue.ListClaimableWorkflowExecutionIdsAsync(
                new RuntimeSchedulerClaimableBacklogQuery(now, pageSize, after),
                token));
    }

    // Counts re-drivable backlog-side executions (sourced candidates first, then the walk), but only as far as it can
    // change the scanner's limit, so nothing beyond that is checked before the sweep knows it can use it.
    private static async ValueTask<int> CountBacklogDemandAsync(
        int worthCounting,
        IReadOnlyCollection<string> sourcedIds,
        BacklogWalk backlog,
        PauseCheck check,
        CancellationToken cancellationToken)
    {
        var demand = 0;
        foreach (var workflowExecutionId in sourcedIds)
        {
            if (demand >= worthCounting)
                return demand;
            if (await check.ClassifyAsync(workflowExecutionId, cancellationToken) == Readiness.Ready)
                demand++;
        }

        return demand >= worthCounting ? demand : demand + await backlog.CountReadyAheadAsync(worthCounting - demand, cancellationToken);
    }

    // Recovery candidates are offered first and all re-drivable ones fit: the scanner's page was sized to the slots the
    // backlog side left, and the scanner's cursor moves past every candidate it returned. Sourced candidates and then
    // the backlog fill the remaining slots in listed order. Held candidates take no slot; a held recovery or sourced
    // candidate is still dealt with. The walk stops where the slots run out, so nothing it did not reach is skipped.
    private static async ValueTask<Selection> SelectAsync(
        SlotPolicy slots,
        PauseCheck check,
        IReadOnlyCollection<RuntimeRecoveryCandidate> candidates,
        IReadOnlyCollection<string> sourcedIds,
        BacklogWalk backlog,
        CancellationToken cancellationToken)
    {
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var held = new HashSet<string>(StringComparer.Ordinal);

        async ValueTask OfferAsync(string workflowExecutionId)
        {
            switch (await check.ClassifyAsync(workflowExecutionId, cancellationToken))
            {
                case Readiness.Ready:
                    selected.Add(workflowExecutionId);
                    break;
                case Readiness.Held:
                    held.Add(workflowExecutionId);
                    break;
            }
        }

        foreach (var candidate in candidates)
            await OfferAsync(candidate.WorkflowExecutionId);
        var recoveryUsedSlot = selected.Count > 0;
        foreach (var workflowExecutionId in sourcedIds)
        {
            if (selected.Count >= slots.Capacity)
                break;
            await OfferAsync(workflowExecutionId);
        }

        while (selected.Count < slots.Capacity && await backlog.TakeNextReadyAsync(cancellationToken) is { } workflowExecutionId)
            selected.Add(workflowExecutionId);

        return new Selection(
            selected.Order(StringComparer.Ordinal).ToArray(),
            held,
            slots.NextRecoveryTurn(recoveryUsedSlot, slotUsed: selected.Count > 0));
    }

    // Candidates a source supplies (spec 184, FR-027) are due now, so they join the sweep beside the durable backlog
    // rather than competing with the scanner's cursor. Each source bounds its own list to the scan batch.
    private async ValueTask<IReadOnlyList<SourcedCandidates>> ListSourcedCandidatesAsync(
        RuntimeResumptionSweepRequest request,
        CancellationToken cancellationToken)
    {
        if (candidateSources.Length == 0)
            return [];

        var limit = Math.Min(request.RecoveryScanBatchSize, RuntimeStorePageRequest.MaximumLimit);
        var sourced = new List<SourcedCandidates>(candidateSources.Length);
        foreach (var source in candidateSources)
        {
            var candidates = await source.ListAsync(limit, cancellationToken);
            if (candidates.Count > 0)
                sourced.Add(new SourcedCandidates(source, candidates.Select(candidate => candidate.WorkflowExecutionId).Distinct(StringComparer.Ordinal).ToArray()));
        }

        return sourced;
    }

    // A sourced candidate is settled once this sweep dealt with it: re-driven into a mailbox or the durable transport,
    // purged as terminal, or skipped as held. One whose re-drive faulted or was rejected, whose pause check failed, or
    // that this sweep did not reach, stays listed.
    private static async ValueTask SettleSourcedCandidatesAsync(
        RecoveryDiscovery discovery,
        IReadOnlyCollection<RuntimeResumptionDispatch> dispatches,
        CancellationToken cancellationToken)
    {
        if (discovery.Sourced.Count == 0)
            return;

        var reached = discovery.ExecutionIds.Concat(discovery.Held).ToHashSet(StringComparer.Ordinal);
        reached.ExceptWith(dispatches
            .Where(dispatch => dispatch.Outcome is RuntimeResumptionDispatchOutcome.Faulted or RuntimeResumptionDispatchOutcome.Rejected)
            .Select(dispatch => dispatch.WorkflowExecutionId));
        foreach (var sourced in discovery.Sourced)
        {
            var settled = sourced.WorkflowExecutionIds.Where(reached.Contains).ToArray();
            if (settled.Length > 0)
                await sourced.Source.SettleAsync(settled, cancellationToken);
        }
    }

    private void WarnOfFirstPageDiscovery(int limit)
    {
        var queueType = workQueue.GetType();
        if (!discoveryStateStore.FirstWarningFor(queueType))
            return;

        logger.LogWarning(
            new EventId(68111, "RuntimeResumptionFirstPageBacklogDiscovery"),
            "Scheduler work queue {QueueType} does not support claimable backlog discovery, so resumption sweeps list " +
            "only the first {Limit} executions with queued work. Executions past that page can wait indefinitely while " +
            "the page is taken by work no claim can take or that does not drain; implement {Capability} to remove the risk",
            queueType.FullName ?? queueType.Name,
            limit,
            nameof(IWorkflowSchedulerWorkQueue.SupportsClaimableBacklogDiscovery));
    }

    private void WarnOfPauseCheckFailures(PauseCheck check)
    {
        if (check.FirstFailure is not { } first)
            return;

        logger.LogWarning(
            new EventId(68112, "RuntimeResumptionPauseCheckFailed"),
            first.Exception,
            "Runtime resumption could not consult the pause gate for {FailureCount} execution(s) this sweep, the first " +
            "being {WorkflowExecutionId}. They were not re-driven, because their drains would consult the same gate; " +
            "their work stays queued and is picked up once the check succeeds",
            check.Failures,
            first.WorkflowExecutionId);
    }

    private static string RecoveryCursorKey(
        IRuntimeRecoveryScanner scanner,
        IReadOnlySet<string> excludedWorkflowExecutionIds)
    {
        var scannerName = scanner.GetType().AssemblyQualifiedName ?? scanner.GetType().FullName ?? scanner.GetType().Name;
        if (excludedWorkflowExecutionIds.Count == 0)
            return scannerName;

        // Exclusions are a sweep-level filter rather than scanner input. Partition retained cursors by a stable
        // fingerprint so a cursor that advanced past an excluded candidate cannot hide it on a later unexcluded
        // sweep. The bounded hash keeps the in-memory cursor key independent of the number/length of IDs.
        // Hash a length-prefixed sequence rather than a delimiter-joined string. Workflow IDs are caller data, so a
        // delimiter can otherwise make two distinct exclusion sets share one cursor partition.
        using var exclusionHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> lengthPrefix = stackalloc byte[sizeof(int)];
        foreach (var excludedId in excludedWorkflowExecutionIds.Order(StringComparer.Ordinal))
        {
            var bytes = Encoding.UTF8.GetBytes(excludedId);
            BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, bytes.Length);
            exclusionHash.AppendData(lengthPrefix);
            exclusionHash.AppendData(bytes);
        }

        var fingerprint = Convert.ToHexString(exclusionHash.GetHashAndReset());
        return $"{scannerName}|excluded:{fingerprint}";
    }

    private async ValueTask<RuntimeResumptionDispatch> RedriveAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        try
        {
            var now = timeProvider.GetUtcNow();
            var partition = CurrentPartition();
            var agent = await agentProvider.GetAgentAsync(
                new WorkflowExecutionActorActivationRequest(
                    workflowExecutionId: workflowExecutionId,
                    reason: WorkflowExecutionActorActivationReason.Recovery,
                    requestedAt: now,
                    requestedBy: DispatchSource,
                    requiredCapabilities: agentProvider.Capabilities,
                    partition: partition),
                cancellationToken);

            var commandId = idGenerator.NewWorkflowExecutionCommandId();
            var envelopeId = idGenerator.NewWorkflowExecutionCommandEnvelopeId();
            var metadata = new Dictionary<string, string> { ["source"] = DispatchSource };
            var envelope = new WorkflowExecutionCommandEnvelope(
                envelopeId: envelopeId,
                workflowExecutionId: workflowExecutionId,
                command: new WorkflowExecutionCommand(
                    CommandId: commandId,
                    WorkflowExecutionId: workflowExecutionId,
                    Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
                    EnqueuedAt: now,
                    Payload: null,
                    Metadata: metadata),
                idempotencyKey: $"{DispatchSource}:{workflowExecutionId}:{envelopeId}",
                deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
                enqueuedAt: now,
                metadata: metadata,
                partition: partition);

            var dispatchResult = await agent.EnqueueAsync(envelope, cancellationToken);

            return new RuntimeResumptionDispatch(
                workflowExecutionId,
                MapOutcome(dispatchResult.Status),
                envelopeId,
                dispatchResult.Status is WorkflowExecutionCommandDispatchStatus.Rejected
                    ? dispatchResult.Reason ?? "Command dispatch was rejected."
                    : null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new RuntimeResumptionDispatch(
                workflowExecutionId,
                RuntimeResumptionDispatchOutcome.Faulted,
                EnvelopeId: null,
                Failure: exception.Message);
        }
    }

    private WorkflowExecutionPartition CurrentPartition()
    {
        if (partitionAccessor is null)
            return new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue);

        return partitionAccessor.Current;
    }

    private static RuntimeResumptionDispatchOutcome MapOutcome(WorkflowExecutionCommandDispatchStatus status) => status switch
    {
        WorkflowExecutionCommandDispatchStatus.Accepted => RuntimeResumptionDispatchOutcome.Accepted,
        WorkflowExecutionCommandDispatchStatus.Duplicate => RuntimeResumptionDispatchOutcome.Duplicate,
        WorkflowExecutionCommandDispatchStatus.Deferred => RuntimeResumptionDispatchOutcome.Deferred,
        _ => RuntimeResumptionDispatchOutcome.Rejected
    };

    private sealed record RecoveryDiscovery(
        IReadOnlyCollection<string> ExecutionIds,
        IReadOnlyCollection<string> Held,
        string Scope,
        string Scanner,
        RuntimeRecoverySweepCursor? PreviousCursor,
        RuntimeRecoverySweepCursor? CursorToCommit,
        bool ShouldUpdateCursor,
        RuntimeResumptionDiscoveryState NextState,
        PauseCheck PauseCheck,
        IReadOnlyList<SourcedCandidates> Sourced);

    private sealed record Selection(IReadOnlyCollection<string> ExecutionIds, IReadOnlyCollection<string> Held, bool NextRecoveryTurn);

    private sealed record SourcedCandidates(IRuntimeRecoveryCandidateSource Source, IReadOnlyCollection<string> WorkflowExecutionIds);

    // What a re-drive of an execution could achieve this sweep.
    private enum Readiness
    {
        // Its drain could make progress, or it has nothing queued and the re-drive itself is the work (recovery).
        Ready,
        // The pause gate would stop its drain at the next item: re-driving it would only queue another trigger.
        Held,
        // The pause gate could not be consulted; its drain would consult the same gate.
        CheckFailed,
        // The caller is backing it off this sweep.
        Excluded
    }

    // The share each side of a capped sweep gets, in one place (#2188). The recovery scanner keeps half the cap (at
    // least one slot, at most its batch size) and the backlog the rest, and either side may use what the other leaves.
    // A cap of one cannot be halved, so its single slot alternates: the side that used it hands the turn to the other.
    private sealed record SlotPolicy(int? Max, int ScanLimit, bool RecoveryHasSingleSlotTurn)
    {
        public int Capacity => Max ?? int.MaxValue;

        private int RecoveryShare => Max switch
        {
            null => ScanLimit,
            1 => RecoveryHasSingleSlotTurn ? 1 : 0,
            { } max => Math.Min(ScanLimit, Math.Max(1, max / 2))
        };

        // Backlog demand beyond the backlog's own share leaves the scanner its share all the same, so counting further
        // would only check executions the sweep may not use.
        public int BacklogDemandWorthCounting => Max is { } max ? max - RecoveryShare : 0;

        public int RecoveryLimit(int backlogDemand) =>
            Max is { } max ? Math.Min(ScanLimit, max - Math.Min(backlogDemand, max - RecoveryShare)) : ScanLimit;

        public bool NextRecoveryTurn(bool recoveryUsedSlot, bool slotUsed) =>
            Max == 1 && slotUsed ? !recoveryUsedSlot : RecoveryHasSingleSlotTurn;
    }

    // Decides, once per execution per sweep, whether re-driving it could make progress. Next items are read in batches
    // (one request per page of executions), and the drainer's own pause gate is asked only when the sweep is about to
    // use an execution. Failures are counted for one warning per sweep.
    private sealed class PauseCheck(IWorkflowSchedulerWorkQueue queue, IWorkflowSchedulerPauseGate gate, IReadOnlySet<string> excluded)
    {
        private readonly Dictionary<string, RuntimeSchedulerWorkItem?> _nextItems = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Readiness> _readiness = new(StringComparer.Ordinal);

        public int Failures { get; private set; }

        public (string WorkflowExecutionId, Exception Exception)? FirstFailure { get; private set; }

        public async ValueTask ReadHeadsAsync(IEnumerable<string> workflowExecutionIds, CancellationToken cancellationToken)
        {
            var unread = workflowExecutionIds
                .Where(id => !excluded.Contains(id) && !_nextItems.ContainsKey(id) && !_readiness.ContainsKey(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (unread.Length == 0)
                return;

            try
            {
                Remember(unread, await queue.ListNextWorkItemsAsync(unread, cancellationToken));
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // One unreadable row must not cost the rest of the page their check: read them one by one instead.
                foreach (var workflowExecutionId in unread)
                {
                    try
                    {
                        Remember([workflowExecutionId], await queue.ListNextWorkItemsAsync([workflowExecutionId], cancellationToken));
                    }
                    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        Fail(workflowExecutionId, exception);
                    }
                }
            }
        }

        public async ValueTask<Readiness> ClassifyAsync(string workflowExecutionId, CancellationToken cancellationToken)
        {
            if (_readiness.TryGetValue(workflowExecutionId, out var known))
                return known;
            if (excluded.Contains(workflowExecutionId))
                return _readiness[workflowExecutionId] = Readiness.Excluded;
            if (!_nextItems.ContainsKey(workflowExecutionId))
            {
                await ReadHeadsAsync([workflowExecutionId], cancellationToken);
                if (_readiness.TryGetValue(workflowExecutionId, out known))
                    return known;
            }

            try
            {
                var held = _nextItems[workflowExecutionId] is { } next &&
                           await gate.EvaluateAsync(next, cancellationToken) is { CanAdvance: false };
                return _readiness[workflowExecutionId] = held ? Readiness.Held : Readiness.Ready;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                return Fail(workflowExecutionId, exception);
            }
        }

        private void Remember(IEnumerable<string> workflowExecutionIds, IReadOnlyDictionary<string, RuntimeSchedulerWorkItem> nextItems)
        {
            foreach (var workflowExecutionId in workflowExecutionIds)
                _nextItems[workflowExecutionId] = nextItems.GetValueOrDefault(workflowExecutionId);
        }

        private Readiness Fail(string workflowExecutionId, Exception exception)
        {
            Failures++;
            FirstFailure ??= (workflowExecutionId, exception);
            return _readiness[workflowExecutionId] = Readiness.CheckFailed;
        }
    }

    // One sweep's walk over durable backlog. It lists executions page by page (at most MaxBacklogPagesPerSweep), checks
    // each only when the sweep reaches it, passes those that are not ready without a slot, and hands out at most one
    // page's worth of ready executions, BacklogBatchSize being the backlog's bound per sweep.
    private sealed class BacklogWalk
    {
        private readonly PauseCheck _check;
        private readonly string? _startAfter;
        private readonly int _pageSize;
        private readonly Func<string?, CancellationToken, ValueTask<IReadOnlyCollection<string>>>? _listAfter;
        private readonly List<string> _listed = [];
        private int _visited;
        private int _pagesRead;
        private int _readyLeft;
        private bool _listedToBacklogEnd;

        private BacklogWalk(
            PauseCheck check,
            string? startAfter,
            int pageSize,
            Func<string?, CancellationToken, ValueTask<IReadOnlyCollection<string>>>? listAfter)
        {
            _check = check;
            _startAfter = startAfter;
            _pageSize = pageSize;
            _readyLeft = pageSize;
            _listAfter = listAfter;
        }

        public static BacklogWalk After(
            PauseCheck check,
            string? startAfter,
            int pageSize,
            Func<string?, CancellationToken, ValueTask<IReadOnlyCollection<string>>> listAfter) =>
            new(check, startAfter, pageSize, listAfter);

        // A provider that cannot resume after a position offers one page, which is all the backlog the sweep sees.
        public static async ValueTask<BacklogWalk> OverFirstPageAsync(
            PauseCheck check,
            IReadOnlyCollection<string> firstPage,
            CancellationToken cancellationToken)
        {
            var walk = new BacklogWalk(check, startAfter: null, firstPage.Count, listAfter: null) { _listedToBacklogEnd = true };
            walk._listed.AddRange(firstPage);
            await check.ReadHeadsAsync(firstPage, cancellationToken);
            return walk;
        }

        // Counts ready executions ahead of the walk, up to limit, without visiting them.
        public async ValueTask<int> CountReadyAheadAsync(int limit, CancellationToken cancellationToken)
        {
            var ready = 0;
            for (var index = _visited; ready < Math.Min(limit, _readyLeft); index++)
            {
                if (index == _listed.Count && !await ListMoreAsync(cancellationToken))
                    break;
                if (await _check.ClassifyAsync(_listed[index], cancellationToken) == Readiness.Ready)
                    ready++;
            }

            return ready;
        }

        // Visits executions up to and including the next ready one and returns it, passing the rest on the way; null
        // when the walk cannot go further this sweep.
        public async ValueTask<string?> TakeNextReadyAsync(CancellationToken cancellationToken)
        {
            while (_readyLeft > 0 && (_visited < _listed.Count || await ListMoreAsync(cancellationToken)))
            {
                var workflowExecutionId = _listed[_visited++];
                if (await _check.ClassifyAsync(workflowExecutionId, cancellationToken) == Readiness.Ready)
                {
                    _readyLeft--;
                    return workflowExecutionId;
                }
            }

            return null;
        }

        // Where the next sweep resumes: from the start once this one visited the end of the backlog, otherwise after the
        // last execution it visited, or where it started when it visited none.
        public string? ResumeAfter()
        {
            if (_listedToBacklogEnd && _visited == _listed.Count)
                return null;
            return _visited > 0 ? _listed[_visited - 1] : _startAfter;
        }

        private async ValueTask<bool> ListMoreAsync(CancellationToken cancellationToken)
        {
            if (_listAfter is null || _listedToBacklogEnd || _pagesRead == MaxBacklogPagesPerSweep)
                return false;

            var page = await _listAfter(_listed.Count > 0 ? _listed[^1] : _startAfter, cancellationToken);
            _pagesRead++;
            _listedToBacklogEnd = page.Count < _pageSize;
            _listed.AddRange(page);
            await _check.ReadHeadsAsync(page, cancellationToken);
            return page.Count > 0;
        }
    }
}
