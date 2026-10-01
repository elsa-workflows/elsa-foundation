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
/// dispatch, including due <c>FailedRetryable</c> retries; a scheduler-work continuation is left to the
/// drain that holds its execution's ownership lease, #2225), backlog discovery (durably queued
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
/// candidate counts as dealt with, and a held terminal one is still purged. When the gate cannot be consulted the
/// execution is not re-driven either, since its drain would consult the same gate, and a recovery candidate keeps the
/// scan cursor in place. Under <see cref="RuntimeResumptionSweepRequest.MaxExecutionsPerSweep"/> the
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

    public async ValueTask<RuntimeResumptionSweepResult> SweepAsync(RuntimeResumptionSweepRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // A continuation whose execution is draining belongs to that drain, which delivers it and drains the work it
        // enqueues. Claiming it here would let the drain report quiescence with its next step still undrained, so a
        // caller saw Accepted before the work its own command produced (#2225). The lease is the drain's liveness; a
        // dead drain's lease expires and the item is claimed then.
        var outboxResult = await outboxProcessor.ProcessAsync(
            new RuntimePostCommitOutboxProcessRequest(
                limit: request.OutboxBatchSize,
                workflowExecutionId: null,
                intentKind: null,
                deferContinuationsToExecutionOwner: true),
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
            if (await PurgeIfTerminalAsync(request, workflowExecutionId, cancellationToken) is { } purged)
            {
                purgedWorkItemCount += purged;
                terminalExecutionsPurged++;
                continue;
            }

            dispatches.Add(await RedriveAsync(workflowExecutionId, cancellationToken));
        }

        // A held execution is not re-driven, but a terminal one is still purged: terminal status is monotonic and the
        // purge re-drives nothing, so its residue stops costing every pass through the backlog a visit. (#2188)
        foreach (var workflowExecutionId in discovery.Held)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await PurgeIfTerminalAsync(request, workflowExecutionId, cancellationToken) is { } purged)
            {
                purgedWorkItemCount += purged;
                terminalExecutionsPurged++;
            }
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

    // Purges and reaps the execution when it is terminal, returning how many residual items it removed; null when the
    // execution is not terminal.
    private async ValueTask<int?> PurgeIfTerminalAsync(
        RuntimeResumptionSweepRequest request,
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        if (!await IsTerminalAsync(workflowExecutionId, cancellationToken))
            return null;

        var purged = await PurgeResidualSchedulerWorkAsync(request, workflowExecutionId, cancellationToken);
        await ReapTerminalMailboxAsync(workflowExecutionId, cancellationToken);
        return purged;
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
        // are durable queue outcomes and may advance the page; faulted/rejected outcomes explicitly rewind it, and so
        // does a candidate whose pause check failed, which was never looked at. A held candidate is not a failure: its
        // drain would stop at the pause gate, so it is dealt with until the scan wraps.
        var failed = discovery.RecoveryCandidateUnchecked || dispatches.Any(dispatch => dispatch.Outcome is
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
            selection.RecoveryCandidateUnchecked,
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

        async ValueTask<Readiness> OfferAsync(string workflowExecutionId)
        {
            var readiness = await check.ClassifyAsync(workflowExecutionId, cancellationToken);
            switch (readiness)
            {
                case Readiness.Ready:
                    selected.Add(workflowExecutionId);
                    break;
                case Readiness.Held:
                    held.Add(workflowExecutionId);
                    break;
            }

            return readiness;
        }

        var recoveryCandidateUnchecked = false;
        foreach (var candidate in candidates)
            recoveryCandidateUnchecked |= await OfferAsync(candidate.WorkflowExecutionId) == Readiness.CheckFailed;
        var recoveryUsedSlot = selected.Count > 0;
        foreach (var workflowExecutionId in sourcedIds)
        {
            if (selected.Count >= slots.Capacity)
                break;
            await OfferAsync(workflowExecutionId);
        }

        while (selected.Count < slots.Capacity && await backlog.TakeNextReadyAsync(cancellationToken) is { } workflowExecutionId)
            selected.Add(workflowExecutionId);
        held.UnionWith(backlog.Held);

        return new Selection(
            selected.Order(StringComparer.Ordinal).ToArray(),
            held,
            recoveryCandidateUnchecked,
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
            "being {WorkflowExecutionId}. None was re-driven, because its drain would consult the same gate, and its " +
            "work stays queued. Recovery-scanner candidates are offered again next sweep, since the scan cursor keeps " +
            "its place; source candidates stay listed; backlog executions are reached again on the next pass through " +
            "the backlog",
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
        bool RecoveryCandidateUnchecked,
        string Scope,
        string Scanner,
        RuntimeRecoverySweepCursor? PreviousCursor,
        RuntimeRecoverySweepCursor? CursorToCommit,
        bool ShouldUpdateCursor,
        RuntimeResumptionDiscoveryState NextState,
        PauseCheck PauseCheck,
        IReadOnlyList<SourcedCandidates> Sourced);

    // Held covers every held execution the sweep reached, from any source, so a terminal one can still be purged.
    private sealed record Selection(
        IReadOnlyCollection<string> ExecutionIds,
        IReadOnlyCollection<string> Held,
        bool RecoveryCandidateUnchecked,
        bool NextRecoveryTurn);

    private sealed record SourcedCandidates(IRuntimeRecoveryCandidateSource Source, IReadOnlyCollection<string> WorkflowExecutionIds);
}
