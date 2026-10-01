using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

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
/// <b>Backlog discovery and the recovery share (#2188).</b> Backlog discovery lists only executions whose scheduler
/// work is claimable now (<see cref="IWorkflowSchedulerWorkQueue.ListClaimableWorkflowExecutionIdsAsync"/>), so work
/// hidden by a live claim or a backoff cannot fill the page, and it walks the backlog with a bound retained between
/// sweeps, so no fixed set of executions can hold the window. Under
/// <see cref="RuntimeResumptionSweepRequest.MaxExecutionsPerSweep"/> the recovery scanner always runs and keeps half
/// the cap (at least one slot, at most its batch size); either side may use what the other leaves. A queue without claimable discovery keeps the earlier first-page listing through
/// <see cref="IWorkflowSchedulerWorkQueue.ListPendingWorkflowExecutionIdsAsync"/>.
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
    IWorkflowExecutionPartitionAccessor? partitionAccessor = null,
    IRuntimeRecoverySweepCursorStore? recoveryCursorStore = null,
    IPersistenceAccessContextAccessor? persistenceAccessContextAccessor = null,
    IEnumerable<IRuntimeRecoveryCandidateSource>? recoveryCandidateSources = null) : IRuntimeResumptionService
{
    // Keep the pre-candidate-source signature in the binary surface for already compiled hosts.
    public RuntimeResumptionService(
        IRuntimePostCommitOutboxProcessor outboxProcessor,
        IWorkflowSchedulerWorkQueue workQueue,
        IRuntimeRecoveryScanner recoveryScanner,
        IWorkflowExecutionActorProvider agentProvider,
        IRuntimeExecutionIdGenerator idGenerator,
        TimeProvider timeProvider,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IWorkflowExecutionPartitionAccessor? partitionAccessor,
        IRuntimeRecoverySweepCursorStore? recoveryCursorStore,
        IPersistenceAccessContextAccessor? persistenceAccessContextAccessor)
        : this(
            outboxProcessor,
            workQueue,
            recoveryScanner,
            agentProvider,
            idGenerator,
            timeProvider,
            workflowExecutionStateStore,
            partitionAccessor,
            recoveryCursorStore,
            persistenceAccessContextAccessor,
            null)
    {
    }

    // Keep both pre-paging constructor signatures in the binary surface. Optional parameters preserve source
    // compatibility but do not preserve metadata constructors used by already compiled hosts.
    public RuntimeResumptionService(
        IRuntimePostCommitOutboxProcessor outboxProcessor,
        IWorkflowSchedulerWorkQueue workQueue,
        IRuntimeRecoveryScanner recoveryScanner,
        IWorkflowExecutionActorProvider agentProvider,
        IRuntimeExecutionIdGenerator idGenerator,
        TimeProvider timeProvider,
        IWorkflowExecutionStateStore workflowExecutionStateStore)
        : this(
            outboxProcessor,
            workQueue,
            recoveryScanner,
            agentProvider,
            idGenerator,
            timeProvider,
            workflowExecutionStateStore,
            null,
            null,
            null)
    {
    }

    public RuntimeResumptionService(
        IRuntimePostCommitOutboxProcessor outboxProcessor,
        IWorkflowSchedulerWorkQueue workQueue,
        IRuntimeRecoveryScanner recoveryScanner,
        IWorkflowExecutionActorProvider agentProvider,
        IRuntimeExecutionIdGenerator idGenerator,
        TimeProvider timeProvider,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IWorkflowExecutionPartitionAccessor? partitionAccessor)
        : this(
            outboxProcessor,
            workQueue,
            recoveryScanner,
            agentProvider,
            idGenerator,
            timeProvider,
            workflowExecutionStateStore,
            partitionAccessor,
            null,
            null)
    {
    }

    private const string DispatchSource = "runtime-resumption";
    private readonly IRuntimeRecoverySweepCursorStore sweepCursorStore = recoveryCursorStore ?? new InMemoryRuntimeRecoverySweepCursorStore();
    private readonly IRuntimeRecoveryCandidateSource[] candidateSources = recoveryCandidateSources?.ToArray() ?? [];
    private readonly string backlogBoundKey = $"scheduler-backlog|{workQueue.GetType().AssemblyQualifiedName ?? workQueue.GetType().FullName ?? workQueue.GetType().Name}";

    // Safety cap on residual-item purge pages per terminal execution per sweep, so a provider that never actually
    // removes an item (Delete returning false) cannot spin this loop forever. Bounded residue is expected — one
    // stranded RunSchedulerWork row per prior sweep — so a handful of BacklogBatchSize pages always suffices.
    private const int MaxPurgePagesPerExecution = 16;

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
        CommitBacklogBound(discovery, request);
        await SettleSourcedCandidatesAsync(discovery, dispatches, cancellationToken);

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
        // are durable queue outcomes and may advance the page; faulted/rejected outcomes explicitly rewind it.
        var failed = dispatches.Any(dispatch => dispatch.Outcome is
            RuntimeResumptionDispatchOutcome.Faulted or RuntimeResumptionDispatchOutcome.Rejected);
        var cursor = failed ? discovery.PreviousCursor : discovery.CursorToCommit;
        if (cursor is null)
            sweepCursorStore.Clear(discovery.Scope, discovery.Scanner);
        else
            sweepCursorStore.Set(discovery.Scope, discovery.Scanner, cursor);
    }

    // Unlike the recovery cursor, the backlog bound always moves on, failed dispatches included: it is a position in a
    // walk, not a claim on a page. A failed execution keeps its queued work and the pump's per-execution backoff, and
    // rewinding would let a page of failing executions hold the window again.
    private void CommitBacklogBound(RecoveryDiscovery discovery, RuntimeResumptionSweepRequest request)
    {
        if (discovery.BacklogBound is not { } bound)
            return;

        if (bound.AfterWorkflowExecutionId is null)
        {
            sweepCursorStore.Clear(discovery.Scope, backlogBoundKey);
            return;
        }

        // The bound shares the sweep cursor store under its own key. Only the continuation (the last execution ID the
        // sweep visited) is read back; the remaining fields record the sweep that wrote it.
        sweepCursorStore.Set(
            discovery.Scope,
            backlogBoundKey,
            new RuntimeRecoverySweepCursor(
                bound.ListedAt,
                request.LeaseTimeout,
                request.HeartbeatTimeout,
                bound.Limit,
                bound.AfterWorkflowExecutionId));
    }

    private async ValueTask<RecoveryDiscovery> DiscoverExecutionIdsAsync(RuntimeResumptionSweepRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var scope = persistenceAccessContextAccessor?.Current.Scope?.Value ?? PersistenceScope.DefaultValue;
        var sourced = await ListSourcedCandidatesAsync(request, cancellationToken);
        var backlog = await ListBacklogAsync(request, scope, now, cancellationToken);
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

        var sourcedIds = sourced.SelectMany(candidates => candidates.WorkflowExecutionIds).ToArray();
        var recoveryLimit = RecoveryLimit(request, scanLimit, sourcedIds.Concat(backlog.ExecutionIds));
        var scanNow = cursor?.ScanNow ?? now;
        if (recoveryScanner is not IRuntimeRecoveryPagedScanner { SupportsPaging: true })
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
            var legacySelection = Select(request, legacyCandidates.Take(recoveryLimit).ToArray(), sourcedIds, backlog);
            return new(
                legacySelection.ExecutionIds,
                scope,
                scannerName,
                PreviousCursor: null,
                CursorToCommit: null,
                ShouldUpdateCursor: false,
                legacySelection.BacklogBound,
                sourced);
        }

        var page = await recoveryScanner.ScanPageAsync(
            new RuntimeRecoveryScanRequest(
                now: scanNow,
                leaseTimeout: request.LeaseTimeout,
                heartbeatTimeout: request.HeartbeatTimeout,
                limit: recoveryLimit,
                continuationToken: cursor?.ContinuationToken),
            cancellationToken);
        var cursorToCommit = page.NextContinuationToken is { } next
            ? new RuntimeRecoverySweepCursor(
                scanNow,
                request.LeaseTimeout,
                request.HeartbeatTimeout,
                scanLimit,
                next)
            : null;
        var selection = Select(request, page.Items.ToArray(), sourcedIds, backlog);
        return new(
            selection.ExecutionIds,
            scope,
            scannerName,
            cursor,
            cursorToCommit,
            ShouldUpdateCursor: true,
            selection.BacklogBound,
            sourced);
    }

    // Lists one page of durable backlog (#2188). A provider with claimable discovery lists only executions whose head a
    // claim would serve right now, so work hidden by a live claim or a backoff cannot fill the page, and it resumes
    // after the last execution the previous sweep visited, so no fixed set of executions holds the window: the bound
    // walks the whole backlog and starts over once a page comes back short. The bound is deliberately not partitioned
    // by the exclusion set, unlike the recovery cursor: exclusions change from sweep to sweep, and restarting the walk
    // whenever they did would bring the starvation back. An excluded execution keeps its queued work, so the next walk
    // finds it again. A provider without claimable discovery keeps its earlier first-page listing and no bound.
    private async ValueTask<BacklogPage> ListBacklogAsync(
        RuntimeResumptionSweepRequest request,
        string scope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!workQueue.SupportsClaimableBacklogDiscovery)
        {
            var pending = await workQueue.ListPendingWorkflowExecutionIdsAsync(request.BacklogBatchSize, cancellationToken);
            return new BacklogPage(pending.ToArray(), request.BacklogBatchSize, now, Resumable: false);
        }

        // Reading past the per-sweep cap cannot add a dispatch, so the cap bounds the page as well.
        var limit = Math.Min(request.BacklogBatchSize, request.MaxExecutionsPerSweep ?? int.MaxValue);
        var after = sweepCursorStore.Get(scope, backlogBoundKey)?.ContinuationToken;
        var executionIds = await workQueue.ListClaimableWorkflowExecutionIdsAsync(
            new RuntimeSchedulerClaimableBacklogQuery(now, limit, after),
            cancellationToken);
        return new BacklogPage(executionIds.ToArray(), limit, now, Resumable: true);
    }

    // The recovery scanner keeps a guaranteed share of every capped sweep (#2188). Before the scanner is asked, the
    // backlog side (sourced candidates and the durable backlog) counts for at most the cap minus that share, so the
    // scanner always runs; whatever the backlog side does not use, the scanner may take, up to its batch size.
    private static int RecoveryLimit(RuntimeResumptionSweepRequest request, int scanLimit, IEnumerable<string> backlogIds)
    {
        if (request.MaxExecutionsPerSweep is not { } max)
            return scanLimit;

        var share = Math.Min(scanLimit, Math.Max(1, max / 2));
        var backlog = backlogIds
            .Where(id => !request.ExcludedWorkflowExecutionIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .Count();
        return Math.Min(scanLimit, max - Math.Min(backlog, max - share));
    }

    // Recovery candidates are selected first and all of them fit: the scanner's page was sized to the slots the backlog
    // side left, and the scanner's cursor moves past every candidate it returned. Sourced candidates and then the backlog
    // page fill the remaining slots in listed order. The backlog bound stops at the last ID this sweep visited, so an ID
    // that did not fit is listed again by the next sweep instead of waiting for the walk to come round.
    private static Selection Select(
        RuntimeResumptionSweepRequest request,
        IReadOnlyCollection<RuntimeRecoveryCandidate> candidates,
        IReadOnlyCollection<string> sourcedIds,
        BacklogPage backlog)
    {
        var capacity = request.MaxExecutionsPerSweep ?? int.MaxValue;
        var selected = new HashSet<string>(StringComparer.Ordinal);

        // True when the ID is dealt with by this sweep: selected now, already selected, or excluded by the caller.
        bool Visit(string workflowExecutionId)
        {
            if (request.ExcludedWorkflowExecutionIds.Contains(workflowExecutionId) || selected.Contains(workflowExecutionId))
                return true;
            if (selected.Count >= capacity)
                return false;
            selected.Add(workflowExecutionId);
            return true;
        }

        foreach (var candidate in candidates)
            Visit(candidate.WorkflowExecutionId);
        foreach (var workflowExecutionId in sourcedIds)
            Visit(workflowExecutionId);

        string? lastVisited = null;
        var visitedAll = true;
        foreach (var workflowExecutionId in backlog.ExecutionIds)
        {
            if (!Visit(workflowExecutionId))
            {
                visitedAll = false;
                break;
            }

            lastVisited = workflowExecutionId;
        }

        // A short page that was visited to the end closes the walk, so the next sweep starts over; otherwise the bound
        // resumes after the last visited ID. A sweep that visited none of the page leaves the stored bound as it was.
        var backlogBound = !backlog.Resumable || (!visitedAll && lastVisited is null)
            ? null
            : new BacklogBoundUpdate(
                visitedAll && backlog.ExecutionIds.Count < backlog.Limit ? null : lastVisited,
                backlog.Limit,
                backlog.ListedAt);

        return new Selection(selected.Order(StringComparer.Ordinal).ToArray(), backlogBound);
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
    // or purged as terminal. One whose re-drive faulted or was rejected, or that this sweep did not reach, stays listed.
    private static async ValueTask SettleSourcedCandidatesAsync(
        RecoveryDiscovery discovery,
        IReadOnlyCollection<RuntimeResumptionDispatch> dispatches,
        CancellationToken cancellationToken)
    {
        if (discovery.Sourced.Count == 0)
            return;

        var reached = discovery.ExecutionIds.ToHashSet(StringComparer.Ordinal);
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
        string Scope,
        string Scanner,
        RuntimeRecoverySweepCursor? PreviousCursor,
        RuntimeRecoverySweepCursor? CursorToCommit,
        bool ShouldUpdateCursor,
        BacklogBoundUpdate? BacklogBound,
        IReadOnlyList<SourcedCandidates> Sourced);

    private sealed record BacklogPage(IReadOnlyList<string> ExecutionIds, int Limit, DateTimeOffset ListedAt, bool Resumable);

    // A null AfterWorkflowExecutionId closes the walk: the next sweep lists from the first execution.
    private sealed record BacklogBoundUpdate(string? AfterWorkflowExecutionId, int Limit, DateTimeOffset ListedAt);

    private sealed record Selection(IReadOnlyCollection<string> ExecutionIds, BacklogBoundUpdate? BacklogBound);

    private sealed record SourcedCandidates(IRuntimeRecoveryCandidateSource Source, IReadOnlyCollection<string> WorkflowExecutionIds);
}
