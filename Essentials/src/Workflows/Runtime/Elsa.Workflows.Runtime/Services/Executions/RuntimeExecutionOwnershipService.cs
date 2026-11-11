using System.Globalization;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executions;

/// <summary>
/// Default execution ownership service (single-writer fencing). Backs ownership with the operational state store:
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

    /// <summary>
    /// The compatibility constructor can safely share its ownership service with the foreground drain only when the
    /// backing store is the built-in concurrency-safe in-memory store. Scoped stores require an isolated operation
    /// scope for renewal.
    /// </summary>
    internal bool RequiresIsolatedHeartbeatScope => _operationalStateStore is not InMemoryExecutionLivenessStateStore;

    /// <summary>
    /// Is told the revision at which the ownership row holds a lease this service wrote, and when a lease is given
    /// up. Null when nothing listens, which is the case unless the EF checkpoint store is registered.
    /// </summary>
    internal IOwnershipRevisionObserver? RevisionObserver { private get; init; }

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

    public async ValueTask<RuntimeExecutionLease> AcquireAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
        (await AcquireAsync(workflowExecutionId, borrow: false, cancellationToken)).Lease;

    /// <summary>
    /// Acquires the run for one short write that is not a drain, such as an alteration. Like
    /// <see cref="AcquireAsync(string, CancellationToken)"/> it issues the next fencing token, so whoever held the run
    /// is refused at its next heartbeat or commit. What differs is what the ownership record says:
    /// <list type="bullet">
    /// <item>The run had no owner: the lease is marked as borrowed from no owner. It records no drain, so a later
    /// borrower that finds it sees no owner.</item>
    /// <item>The run had an owner (a lease on the record, live or expired): the lease stands in for that owner. The
    /// owner's heartbeat stays on the record, so the run is listed under the owner as well as under the borrower.
    /// The lease is marked with the expiry of the owner's lease, for a later borrower that finds it.</item>
    /// </list>
    /// Either way the lease is the borrower's own: its id, acquired now.
    /// </summary>
    internal ValueTask<ExecutionLeaseBorrow> BorrowAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
        AcquireAsync(workflowExecutionId, borrow: true, cancellationToken);

    /// <summary>
    /// Ends a borrow. A lease borrowed from no owner is released. A lease that stands in for an owner is not
    /// released, because that would erase the record of an owner whose work may need recovery. It stays on the
    /// record under a new fencing token, and expires when the owner's lease would have, or at once if that lease
    /// had already expired, so recovery finds the run no later than the owner's lease expiry. The owner's heartbeat
    /// is still beside it. The owner itself, if it is still running, stays fenced out.
    /// Returns <see cref="RuntimeExecutionOwnershipTransitionStatus.Stale"/> without writing when the run has been
    /// taken from the borrower.
    /// </summary>
    internal async ValueTask<RuntimeExecutionOwnershipTransitionResult> ReturnAsync(
        ExecutionLeaseBorrow borrow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(borrow);
        var borrowed = borrow.Lease;
        if (borrow.OwnersLeaseExpiresAt is not { } ownersExpiry)
            return await ReleaseAsync(borrowed, cancellationToken);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loaded = await FindVersionedOwnershipStateAsync(borrowed.WorkflowExecutionId, cancellationToken);
            RevisionObserver?.LeaseEnded(_operationalStateStore, borrowed);
            if (loaded is null)
                return Transition(RuntimeExecutionOwnershipTransitionStatus.NotFound, null);

            var existing = loaded.State;
            var highestToken = ReadHighestIssuedToken(existing);
            if (!Matches(existing.ExecutionLease, borrowed))
                return Transition(RuntimeExecutionOwnershipTransitionStatus.Stale, highestToken);

            var nextToken = checked(highestToken + 1);
            var left = new RuntimeExecutionLease(
                leaseId: borrowed.LeaseId,
                workflowExecutionId: borrowed.WorkflowExecutionId,
                ownerId: borrowed.OwnerId,
                acquiredAt: borrowed.AcquiredAt,
                // A lease cannot expire before it was acquired. One tick after is "already expired" to every reader.
                expiresAt: ownersExpiry > borrowed.AcquiredAt ? ownersExpiry : borrowed.AcquiredAt.AddTicks(1),
                fencingToken: nextToken,
                metadata: existing.ExecutionLease!.Metadata);
            var state = NewOwnershipState(borrowed.WorkflowExecutionId, existing, left, existing.Heartbeat, nextToken);
            var result = await _operationalStateStore.TrySaveAsync(state, loaded.Revision, cancellationToken);
            if (result.Succeeded)
                return Transition(RuntimeExecutionOwnershipTransitionStatus.Applied, nextToken);
            if (result.Status is not ExecutionLivenessStateWriteStatus.RevisionConflict and
                not ExecutionLivenessStateWriteStatus.NotFound)
            {
                throw new InvalidOperationException($"Unsupported ownership return write outcome '{result.Status}'.");
            }
        }
    }

    private async ValueTask<ExecutionLeaseBorrow> AcquireAsync(
        string workflowExecutionId,
        bool borrow,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loaded = await FindVersionedOwnershipStateAsync(workflowExecutionId, cancellationToken);
            var existing = loaded?.State;
            if (IsDeletionTombstoned(existing))
                throw new InvalidOperationException("A deleted workflow execution cannot reacquire ownership.");

            var nextToken = checked(ReadHighestIssuedToken(existing) + 1);
            var now = _timeProvider.GetUtcNow();

            // Read from the revision the write below is conditioned on, so it is the lease this acquisition replaces.
            var ownersExpiry = borrow ? FindOwnersLeaseExpiry(existing) : null;
            var lease = new RuntimeExecutionLease(
                leaseId: RuntimeExecutionIdGenerator.NewId(now),
                workflowExecutionId: workflowExecutionId,
                ownerId: _options.OwnerId,
                acquiredAt: now,
                expiresAt: now + _options.LeaseDuration,
                fencingToken: nextToken,
                metadata: !borrow ? null
                    : ownersExpiry is { } expiry ? BorrowedLeaseMark.ForOwner(expiry)
                    : BorrowedLeaseMark.ForNoOwner());

            // A lease that stands in for an owner leaves the owner's heartbeat on the record. That heartbeat is what
            // still lists the run under the owner, for host reclaim and for the migration quiescence probe.
            var heartbeat = ownersExpiry is null ? NewHeartbeat(workflowExecutionId, lease, now) : existing!.Heartbeat;
            var state = NewOwnershipState(workflowExecutionId, existing, lease, heartbeat, nextToken);
            var result = await _operationalStateStore.TrySaveAsync(state, loaded?.Revision ?? 0, cancellationToken);
            if (result.Succeeded)
            {
                LeaseHeld(lease, result);
                return new ExecutionLeaseBorrow(lease, ownersExpiry);
            }
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
                fencingToken: lease.FencingToken,
                metadata: existing.ExecutionLease.Metadata);

            // Renewing a lease that stands in for an owner keeps the owner's heartbeat, as borrowing it did.
            var heartbeat = BorrowedLeaseMark.StandsInForAnOwner(refreshedLease)
                ? existing.Heartbeat
                : NewHeartbeat(lease.WorkflowExecutionId, refreshedLease, now);
            var state = NewOwnershipState(lease.WorkflowExecutionId, existing, refreshedLease, heartbeat, highestToken);
            var result = await _operationalStateStore.TrySaveAsync(state, loaded.Revision, cancellationToken);
            if (result.Succeeded)
            {
                LeaseHeld(refreshedLease, result);
                return Transition(RuntimeExecutionOwnershipTransitionStatus.Applied, highestToken);
            }
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
            RevisionObserver?.LeaseEnded(_operationalStateStore, lease);
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

    // The save was conditioned on the row's revision and wrote this lease, so the row holds it at the saved revision.
    private void LeaseHeld(RuntimeExecutionLease lease, ExecutionLivenessStateWriteResult saved)
    {
        if (saved.Revision is { } revision)
            RevisionObserver?.LeaseHeld(_operationalStateStore, lease, revision);
    }

    private RuntimeHeartbeat NewHeartbeat(string workflowExecutionId, RuntimeExecutionLease lease, DateTimeOffset now) =>
        new(
            heartbeatId: RuntimeExecutionIdGenerator.NewId(now),
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
            executionLivenessStateId: RuntimeExecutionOwnershipStateId.For(workflowExecutionId),
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
        _operationalStateStore.FindVersionedAsync(workflowExecutionId, RuntimeExecutionOwnershipStateId.For(workflowExecutionId), cancellationToken);

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

    private static bool IsDeletionTombstoned(ExecutionLivenessState? state)
    {
        if (state?.Metadata.TryGetValue(RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone, out var value) != true)
            return false;
        if (!StringComparer.Ordinal.Equals(value, "true"))
            throw new InvalidDataException("The persisted workflow-execution deletion tombstone is corrupt.");
        return true;
    }

    // When the lease of the owner a borrower finds on the record expires. A lease borrowed from no owner records no
    // owner. A lease that stands in for an owner carries that owner's expiry, so a second borrower leaves the same
    // expiry as the first would have, whether the first is still running, has returned, or stopped.
    private static DateTimeOffset? FindOwnersLeaseExpiry(ExecutionLivenessState? existing) =>
        existing?.ExecutionLease is not { } lease || BorrowedLeaseMark.IsFromNoOwner(lease) ? null
        : BorrowedLeaseMark.StandsInForAnOwner(lease) ? BorrowedLeaseMark.ReadOwnersLeaseExpiry(lease)
        : lease.ExpiresAt;

    private static bool Matches(RuntimeExecutionLease? current, RuntimeExecutionLease presented) =>
        current is not null &&
        StringComparer.Ordinal.Equals(current.WorkflowExecutionId, presented.WorkflowExecutionId) &&
        StringComparer.Ordinal.Equals(current.LeaseId, presented.LeaseId) &&
        StringComparer.Ordinal.Equals(current.OwnerId, presented.OwnerId) &&
        current.FencingToken == presented.FencingToken;

    private static RuntimeExecutionOwnershipTransitionResult Transition(
        RuntimeExecutionOwnershipTransitionStatus status,
        long? currentFencingToken) => new(status, currentFencingToken);
}
