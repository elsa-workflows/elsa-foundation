using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryRuntimeRecoveryScanner : IRuntimeRecoveryScanner
{
    private const string SourceMetadataKey = "runtime.recovery.source";
    private readonly IOperationalStateStore _operationalStateStore;

    public InMemoryRuntimeRecoveryScanner(IOperationalStateStore operationalStateStore)
    {
        ArgumentNullException.ThrowIfNull(operationalStateStore);
        _operationalStateStore = operationalStateStore;
    }

    public async ValueTask<IReadOnlyCollection<RuntimeRecoveryCandidate>> ScanAsync(
        RuntimeRecoveryScanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var states = await _operationalStateStore.ListAllAsync(cancellationToken);

        return states
            .Select(state => TryCreateCandidate(state, request))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderBy(candidate => candidate.WorkflowExecutionId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.OperationalStateId, StringComparer.Ordinal)
            .Take(request.Limit)
            .ToArray();
    }

    private static RuntimeRecoveryCandidate? TryCreateCandidate(OperationalState state, RuntimeRecoveryScanRequest request)
    {
        if (!OwnerMatches(state, request.OwnerId))
            return null;

        if (state.InterruptedExecution is { Status: RuntimeInterruptionStatus.Detected } interruptedExecution)
            return CreateCandidate(state, request, interruptedExecution.Reason, interruptedExecution.LastCheckpointId, "InterruptedExecution");

        if (state.ExecutionLease is { } lease && IsLeaseExpired(lease, request))
            return CreateCandidate(state, request, RuntimeInterruptionReason.LeaseLost, state.InterruptedExecution?.LastCheckpointId, "ExecutionLease");

        if (state.Heartbeat is { } heartbeat && heartbeat.RecordedAt.Add(request.HeartbeatTimeout) <= request.Now)
            return CreateCandidate(state, request, RuntimeInterruptionReason.HeartbeatExpired, state.InterruptedExecution?.LastCheckpointId, "Heartbeat");

        return null;
    }

    private static RuntimeRecoveryCandidate CreateCandidate(
        OperationalState state,
        RuntimeRecoveryScanRequest request,
        RuntimeInterruptionReason reason,
        string? lastCheckpointId,
        string source)
    {
        var canRequeue = !string.IsNullOrWhiteSpace(lastCheckpointId);

        return new RuntimeRecoveryCandidate(
            workflowExecutionId: state.WorkflowExecutionId,
            operationalStateId: state.OperationalStateId,
            lastCheckpointId: lastCheckpointId,
            reason: reason,
            detectedAt: request.Now,
            requeueFromLastCheckpoint: canRequeue,
            metadata: new Dictionary<string, string>
            {
                [SourceMetadataKey] = source
            });
    }

    private static bool OwnerMatches(OperationalState state, string? ownerId) =>
        ownerId is null
        || StringComparer.Ordinal.Equals(state.ExecutionLease?.OwnerId, ownerId)
        || StringComparer.Ordinal.Equals(state.Heartbeat?.OwnerId, ownerId);

    private static bool IsLeaseExpired(RuntimeExecutionLease lease, RuntimeRecoveryScanRequest request) =>
        lease.IsExpired(request.Now) || lease.AcquiredAt.Add(request.LeaseTimeout) <= request.Now;
}
