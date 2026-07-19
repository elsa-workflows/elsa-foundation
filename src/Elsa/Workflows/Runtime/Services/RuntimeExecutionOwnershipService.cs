using System.Globalization;
using Elsa.Primitives.Identity;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Default execution ownership service (single-writer fencing, RT-2). Backs ownership with the operational state store:
/// each workflow execution has a single ownership record whose metadata carries the highest fencing token ever issued
/// for it. Acquisition issues a strictly greater token; release clears the live lease/heartbeat while preserving that
/// counter so tokens are never reused; the checkpoint-commit funnel calls <see cref="EnsureCurrentAsync"/> to fence
/// stale writers out.
/// </summary>
public sealed class RuntimeExecutionOwnershipService : IRuntimeExecutionOwnershipService
{
    private readonly IExecutionLivenessStateStore _operationalStateStore;
    private readonly TimeProvider _timeProvider;
    private readonly RuntimeExecutionOwnershipOptions _options;

    public RuntimeExecutionOwnershipService(IExecutionLivenessStateStore operationalStateStore)
        : this(operationalStateStore, TimeProvider.System, new RuntimeExecutionOwnershipOptions())
    {
    }

    public RuntimeExecutionOwnershipService(
        IExecutionLivenessStateStore operationalStateStore,
        TimeProvider timeProvider,
        RuntimeExecutionOwnershipOptions options)
    {
        ArgumentNullException.ThrowIfNull(operationalStateStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OwnerId);

        if (options.LeaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Lease duration must be greater than zero.");

        _operationalStateStore = operationalStateStore;
        _timeProvider = timeProvider;
        _options = options;
    }

    public async ValueTask<RuntimeExecutionLease> AcquireAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loaded = await FindVersionedOwnershipStateAsync(workflowExecutionId, cancellationToken);
            var existing = loaded?.State;
            var nextToken = checked(ReadHighestIssuedToken(existing) + 1);
            var now = _timeProvider.GetUtcNow();

            var lease = new RuntimeExecutionLease(
                leaseId: ShortIdentityGenerator.Generate(now),
                workflowExecutionId: workflowExecutionId,
                ownerId: _options.OwnerId,
                acquiredAt: now,
                expiresAt: now + _options.LeaseDuration,
                fencingToken: nextToken);

            var heartbeat = NewHeartbeat(workflowExecutionId, lease, now);
            var state = NewOwnershipState(workflowExecutionId, existing, lease, heartbeat, nextToken);
            var result = await _operationalStateStore.TrySaveAsync(state, loaded?.Revision ?? 0, cancellationToken);
            if (result.Succeeded)
                return lease;
            if (result.Status is not ExecutionLivenessStateWriteStatus.RevisionConflict and
                not ExecutionLivenessStateWriteStatus.NotFound)
            {
                throw new InvalidOperationException($"Unsupported ownership acquisition write outcome '{result.Status}'.");
            }
        }
    }

    public async ValueTask<RuntimeExecutionOwnershipTransitionResult> HeartbeatAsync(
        RuntimeExecutionLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loaded = await FindVersionedOwnershipStateAsync(lease.WorkflowExecutionId, cancellationToken);
            if (loaded is null)
                return Transition(RuntimeExecutionOwnershipTransitionStatus.NotFound, null);

            var existing = loaded.State;
            var highestToken = ReadHighestIssuedToken(existing);
            var now = _timeProvider.GetUtcNow();
            if (!Matches(existing.ExecutionLease, lease))
                return Transition(RuntimeExecutionOwnershipTransitionStatus.Stale, highestToken);
            if (existing.ExecutionLease!.IsExpired(now))
                return Transition(RuntimeExecutionOwnershipTransitionStatus.Expired, highestToken);

            var refreshedLease = new RuntimeExecutionLease(
                leaseId: lease.LeaseId,
                workflowExecutionId: lease.WorkflowExecutionId,
                ownerId: lease.OwnerId,
                acquiredAt: lease.AcquiredAt,
                expiresAt: now + _options.LeaseDuration,
                fencingToken: lease.FencingToken);

            var heartbeat = NewHeartbeat(lease.WorkflowExecutionId, refreshedLease, now);
            var state = NewOwnershipState(lease.WorkflowExecutionId, existing, refreshedLease, heartbeat, highestToken);
            var result = await _operationalStateStore.TrySaveAsync(state, loaded.Revision, cancellationToken);
            if (result.Succeeded)
                return Transition(RuntimeExecutionOwnershipTransitionStatus.Applied, highestToken);
            if (result.Status is not ExecutionLivenessStateWriteStatus.RevisionConflict and
                not ExecutionLivenessStateWriteStatus.NotFound)
            {
                throw new InvalidOperationException($"Unsupported ownership heartbeat write outcome '{result.Status}'.");
            }
        }
    }

    public async ValueTask<RuntimeExecutionOwnershipTransitionResult> ReleaseAsync(
        RuntimeExecutionLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loaded = await FindVersionedOwnershipStateAsync(lease.WorkflowExecutionId, cancellationToken);
            if (loaded is null)
                return Transition(RuntimeExecutionOwnershipTransitionStatus.NotFound, null);

            var existing = loaded.State;
            var highestToken = ReadHighestIssuedToken(existing);
            if (existing.ExecutionLease is null)
            {
                return Transition(
                    highestToken == lease.FencingToken
                        ? RuntimeExecutionOwnershipTransitionStatus.AlreadyApplied
                        : RuntimeExecutionOwnershipTransitionStatus.Stale,
                    highestToken);
            }
            if (!Matches(existing.ExecutionLease, lease))
                return Transition(RuntimeExecutionOwnershipTransitionStatus.Stale, highestToken);
            if (existing.ExecutionLease.IsExpired(_timeProvider.GetUtcNow()))
                return Transition(RuntimeExecutionOwnershipTransitionStatus.Expired, highestToken);

            // Clear the live lease/heartbeat so a completed execution is not mistaken for an interrupted one, but keep
            // the fencing-token counter so the next acquisition never reuses a token.
            var state = NewOwnershipState(lease.WorkflowExecutionId, existing, executionLease: null, heartbeat: null, highestToken);
            var result = await _operationalStateStore.TrySaveAsync(state, loaded.Revision, cancellationToken);
            if (result.Succeeded)
                return Transition(RuntimeExecutionOwnershipTransitionStatus.Applied, highestToken);
            if (result.Status is not ExecutionLivenessStateWriteStatus.RevisionConflict and
                not ExecutionLivenessStateWriteStatus.NotFound)
            {
                throw new InvalidOperationException($"Unsupported ownership release write outcome '{result.Status}'.");
            }
        }
    }

    public async ValueTask EnsureCurrentAsync(string workflowExecutionId, long fencingToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var existing = (await FindVersionedOwnershipStateAsync(workflowExecutionId, cancellationToken))?.State;
        var highestToken = ReadHighestIssuedToken(existing);

        // No ownership has ever been established for this execution — nothing to fence against.
        if (highestToken == 0)
            return;

        if (existing!.ExecutionLease is null)
        {
            throw new RuntimeStaleFencingTokenException(
                workflowExecutionId,
                fencingToken,
                highestToken,
                RuntimeFencingRejectionReason.NoActiveLease);
        }
        if (existing.ExecutionLease.IsExpired(_timeProvider.GetUtcNow()))
        {
            throw new RuntimeStaleFencingTokenException(
                workflowExecutionId,
                fencingToken,
                highestToken,
                RuntimeFencingRejectionReason.ExpiredLease);
        }
        if (fencingToken != highestToken || existing.ExecutionLease.FencingToken != fencingToken)
            throw new RuntimeStaleFencingTokenException(workflowExecutionId, fencingToken, highestToken);
    }

    private RuntimeHeartbeat NewHeartbeat(string workflowExecutionId, RuntimeExecutionLease lease, DateTimeOffset now) =>
        new(
            heartbeatId: ShortIdentityGenerator.Generate(now),
            workflowExecutionId: workflowExecutionId,
            ownerId: lease.OwnerId,
            leaseId: lease.LeaseId,
            recordedAt: now);

    private static ExecutionLivenessState NewOwnershipState(
        string workflowExecutionId,
        ExecutionLivenessState? existing,
        RuntimeExecutionLease? executionLease,
        RuntimeHeartbeat? heartbeat,
        long highestIssuedToken)
    {
        var metadata = new Dictionary<string, string>(existing?.Metadata ?? new Dictionary<string, string>(), StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.OwnershipFencingToken] = highestIssuedToken.ToString(CultureInfo.InvariantCulture)
        };

        return new ExecutionLivenessState(
            operationalStateId: OwnershipStateId(workflowExecutionId),
            workflowExecutionId: workflowExecutionId,
            executionLease: executionLease,
            heartbeat: heartbeat,
            drain: existing?.Drain,
            interruptedExecution: existing?.InterruptedExecution,
            pendingPostCommitIntentIds: existing?.PendingPostCommitIntentIds,
            metadata: metadata);
    }

    private ValueTask<VersionedExecutionLivenessState?> FindVersionedOwnershipStateAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken) =>
        _operationalStateStore.FindVersionedAsync(workflowExecutionId, OwnershipStateId(workflowExecutionId), cancellationToken);

    private static long ReadHighestIssuedToken(ExecutionLivenessState? state)
    {
        if (state is null)
            return 0;

        if (state.Metadata.TryGetValue(RuntimeMetadataKeys.OwnershipFencingToken, out var raw))
        {
            if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var token) || token < 0)
                throw new InvalidOperationException("Execution ownership state contains an invalid fencing-token counter.");
            if (state.ExecutionLease is { } lease && lease.FencingToken != token)
                throw new InvalidOperationException("Execution ownership state contains a fencing-token counter that does not match its active lease.");
            return token;
        }

        return state.ExecutionLease?.FencingToken ?? 0;
    }

    private static bool Matches(RuntimeExecutionLease? current, RuntimeExecutionLease presented) =>
        current is not null &&
        StringComparer.Ordinal.Equals(current.WorkflowExecutionId, presented.WorkflowExecutionId) &&
        StringComparer.Ordinal.Equals(current.LeaseId, presented.LeaseId) &&
        StringComparer.Ordinal.Equals(current.OwnerId, presented.OwnerId) &&
        current.FencingToken == presented.FencingToken;

    private static RuntimeExecutionOwnershipTransitionResult Transition(
        RuntimeExecutionOwnershipTransitionStatus status,
        long? currentFencingToken) => new(status, currentFencingToken);

    private static string OwnershipStateId(string workflowExecutionId) => $"ownership:{workflowExecutionId}";
}
