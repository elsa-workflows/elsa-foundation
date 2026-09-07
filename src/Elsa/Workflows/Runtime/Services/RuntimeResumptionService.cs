using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

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
/// <b>Terminal-execution short-circuit (spec 113).</b> Backlog discovery (<see cref="IWorkflowSchedulerWorkQueue.ListPendingWorkflowExecutionIdsAsync"/>)
/// has no terminal-status filter, so a scheduler work item stranded by the drainer's terminal-status guard
/// (a sibling item enqueued before a parallel fork ran <c>Finish</c>, or a post-commit <c>EnqueueSchedulerWork</c>
/// intent delivered after the terminal checkpoint) keeps the completed execution discoverable forever. Re-driving
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
    IPersistenceAccessContextAccessor? persistenceAccessContextAccessor = null) : IRuntimeResumptionService
{
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

    private async ValueTask<RecoveryDiscovery> DiscoverExecutionIdsAsync(RuntimeResumptionSweepRequest request, CancellationToken cancellationToken)
    {
        var backlog = await workQueue.ListPendingWorkflowExecutionIdsAsync(request.BacklogBatchSize, cancellationToken);
        var scope = persistenceAccessContextAccessor?.Current.Scope?.Value ?? PersistenceScope.DefaultValue;
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

        var discoverableBacklog = backlog
            .Where(id => !request.ExcludedWorkflowExecutionIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var recoveryCapacity = request.MaxExecutionsPerSweep is { } maxExecutions
            ? Math.Max(0, maxExecutions - discoverableBacklog.Length)
            : scanLimit;
        RuntimeRecoveryCandidate[] candidates;
        if (recoveryCapacity == 0)
        {
            // The backlog already fills this sweep's dispatch cap. Keep the recovery cursor untouched so no
            // candidate can be skipped merely because unrelated backlog occupied the available slots.
            candidates = [];
            return new(
                MergeExecutionIds(backlog, candidates, request),
                scope,
                scannerName,
                cursor,
                CursorToCommit: null,
                ShouldUpdateCursor: false);
        }
        else
        {
            var scanNow = cursor?.ScanNow ?? timeProvider.GetUtcNow();
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
                candidates = legacyCandidates
                    .Take(Math.Min(scanLimit, recoveryCapacity))
                    .ToArray();
                return new(
                    MergeExecutionIds(backlog, candidates, request),
                    scope,
                    scannerName,
                    PreviousCursor: null,
                    CursorToCommit: null,
                    ShouldUpdateCursor: false);
            }

            var page = await recoveryScanner.ScanPageAsync(
                new RuntimeRecoveryScanRequest(
                    now: scanNow,
                    leaseTimeout: request.LeaseTimeout,
                    heartbeatTimeout: request.HeartbeatTimeout,
                    limit: Math.Min(scanLimit, recoveryCapacity),
                    continuationToken: cursor?.ContinuationToken),
                cancellationToken);
            candidates = page.Items.ToArray();
            var cursorToCommit = page.NextContinuationToken is { } next
                ? new RuntimeRecoverySweepCursor(
                    scanNow,
                    request.LeaseTimeout,
                    request.HeartbeatTimeout,
                    scanLimit,
                    next)
                : null;
            return new(
                MergeExecutionIds(backlog, candidates, request),
                scope,
                scannerName,
                cursor,
                cursorToCommit,
                ShouldUpdateCursor: true);
        }

    }

    private static IReadOnlyCollection<string> MergeExecutionIds(
        IReadOnlyCollection<string> backlog,
        IReadOnlyCollection<RuntimeRecoveryCandidate> candidates,
        RuntimeResumptionSweepRequest request) =>
        backlog
            .Concat(candidates.Select(candidate => candidate.WorkflowExecutionId))
            .Where(id => !request.ExcludedWorkflowExecutionIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Take(request.MaxExecutionsPerSweep ?? int.MaxValue)
            .ToArray();

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
        bool ShouldUpdateCursor);
}
