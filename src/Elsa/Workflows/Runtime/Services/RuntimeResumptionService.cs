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
    IWorkflowExecutionPartitionAccessor? partitionAccessor = null) : IRuntimeResumptionService
{
    private const string DispatchSource = "runtime-resumption";

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

        var executionIds = await DiscoverExecutionIdsAsync(request, cancellationToken);

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

    private async ValueTask<IReadOnlyCollection<string>> DiscoverExecutionIdsAsync(RuntimeResumptionSweepRequest request, CancellationToken cancellationToken)
    {
        var backlog = await workQueue.ListPendingWorkflowExecutionIdsAsync(request.BacklogBatchSize, cancellationToken);

        var candidates = await recoveryScanner.ScanAsync(
            new RuntimeRecoveryScanRequest(
                now: timeProvider.GetUtcNow(),
                leaseTimeout: request.LeaseTimeout,
                heartbeatTimeout: request.HeartbeatTimeout,
                limit: request.RecoveryScanBatchSize),
            cancellationToken);

        return backlog
            .Concat(candidates.Select(candidate => candidate.WorkflowExecutionId))
            .Where(id => !request.ExcludedWorkflowExecutionIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Take(request.MaxExecutionsPerSweep ?? int.MaxValue)
            .ToArray();
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
}
