using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryWorkflowExecutableStore : IWorkflowExecutableStore
{
    private readonly Dictionary<string, WorkflowExecutable> _executables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, RootWriteLeaseState>> _rootWriteLeases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeletionGuardState> _deletionGuards = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);

        return SaveBatchAsync([executable], cancellationToken);
    }

    public ValueTask SaveBatchAsync(
        IReadOnlyList<WorkflowExecutable> executables,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executables);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var executable in executables)
        {
            ArgumentNullException.ThrowIfNull(executable);
            ArgumentException.ThrowIfNullOrWhiteSpace(executable.Identity.ArtifactId);
        }

        if (executables.Select(executable => executable.Identity.ArtifactId).Distinct(StringComparer.Ordinal).Count() != executables.Count)
            throw new ArgumentException("A workflow executable batch must contain distinct artifact ids.", nameof(executables));

        lock (_gate)
        {
            // Idempotent by artifact id: artifacts are content-addressed and immutable, so an already-stored
            // artifact is authoritative — a behaviorally identical republish must not overwrite it (ADR 0038).
            foreach (var executable in executables)
                _executables.TryAdd(executable.Identity.ArtifactId, executable);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        lock (_gate)
        {
            _rootWriteLeases.Remove(artifactId);
            _deletionGuards.Remove(artifactId);
            return ValueTask.FromResult(_executables.Remove(artifactId));
        }
    }

    public ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(
        string artifactId,
        string leaseId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);
        EnsureFutureExpiry(expiresAt, now);

        lock (_gate)
        {
            RecoverExpiredState(artifactId, now);
            if (!_executables.ContainsKey(artifactId) || _deletionGuards.ContainsKey(artifactId))
                return ValueTask.FromResult<WorkflowExecutableRootWriteLease?>(null);

            if (!_rootWriteLeases.TryGetValue(artifactId, out var leases))
            {
                leases = new Dictionary<string, RootWriteLeaseState>(StringComparer.Ordinal);
                _rootWriteLeases.Add(artifactId, leases);
            }

            if (leases.TryGetValue(leaseId, out var existing))
                return ValueTask.FromResult<WorkflowExecutableRootWriteLease?>(existing.Lease);

            var lease = new WorkflowExecutableRootWriteLease(artifactId, leaseId, NewConcurrencyToken());
            leases.Add(leaseId, new RootWriteLeaseState(lease, expiresAt));
            return ValueTask.FromResult<WorkflowExecutableRootWriteLease?>(lease);
        }
    }

    public ValueTask<bool> RenewRootWriteLeaseAsync(
        WorkflowExecutableRootWriteLease lease,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        EnsureFutureExpiry(expiresAt, now);

        lock (_gate)
        {
            RecoverExpiredState(lease.ArtifactId, now);
            if (!_rootWriteLeases.TryGetValue(lease.ArtifactId, out var leases) ||
                !leases.TryGetValue(lease.LeaseId, out var current) ||
                current.Lease.ConcurrencyToken != lease.ConcurrencyToken)
                return ValueTask.FromResult(false);

            current.ExpiresAt = expiresAt;
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask ReleaseRootWriteLeaseAsync(
        WorkflowExecutableRootWriteLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);

        lock (_gate)
        {
            if (!_rootWriteLeases.TryGetValue(lease.ArtifactId, out var leases) ||
                !leases.TryGetValue(lease.LeaseId, out var current) ||
                current.Lease.ConcurrencyToken != lease.ConcurrencyToken)
                return ValueTask.CompletedTask;

            leases.Remove(lease.LeaseId);
            if (leases.Count == 0)
                _rootWriteLeases.Remove(lease.ArtifactId);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(
        string artifactId,
        string operationId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        EnsureFutureExpiry(expiresAt, now);

        lock (_gate)
        {
            RecoverExpiredState(artifactId, now);
            if (!_executables.ContainsKey(artifactId) || _rootWriteLeases.ContainsKey(artifactId))
                return ValueTask.FromResult<WorkflowExecutableDeletionGuard?>(null);

            if (_deletionGuards.TryGetValue(artifactId, out var existing))
            {
                if (existing.Guard.OperationId != operationId)
                    return ValueTask.FromResult<WorkflowExecutableDeletionGuard?>(null);

                return ValueTask.FromResult<WorkflowExecutableDeletionGuard?>(existing.Guard);
            }

            var guard = new WorkflowExecutableDeletionGuard(artifactId, operationId, NewConcurrencyToken());
            _deletionGuards.Add(artifactId, new DeletionGuardState(guard, expiresAt));
            return ValueTask.FromResult<WorkflowExecutableDeletionGuard?>(guard);
        }
    }

    public ValueTask<bool> CancelDeletionAsync(
        WorkflowExecutableDeletionGuard guard,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(guard);

        lock (_gate)
        {
            if (!_deletionGuards.TryGetValue(guard.ArtifactId, out var current) || current.Guard != guard)
                return ValueTask.FromResult(false);

            _deletionGuards.Remove(guard.ArtifactId);
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> DeleteAsync(
        WorkflowExecutableDeletionGuard guard,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(guard);

        lock (_gate)
        {
            RecoverExpiredState(guard.ArtifactId, now);
            if (!_deletionGuards.TryGetValue(guard.ArtifactId, out var current) ||
                current.Guard != guard ||
                _rootWriteLeases.ContainsKey(guard.ArtifactId))
                return ValueTask.FromResult(false);

            _deletionGuards.Remove(guard.ArtifactId);
            return ValueTask.FromResult(_executables.Remove(guard.ArtifactId));
        }
    }

    public ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        lock (_gate)
            return ValueTask.FromResult(_executables.GetValueOrDefault(artifactId));
    }

    public ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(
        RuntimeStorePageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            const string queryBinding = "workflow-executable:list";
            var lastId = InMemoryRuntimeStoreContinuation.Decode(request.ContinuationToken, queryBinding, nameof(request.ContinuationToken));
            var ordered = _executables.Values
                .OrderBy(executable => executable.Identity.ArtifactId, StringComparer.Ordinal)
                .Where(executable => lastId is null || StringComparer.Ordinal.Compare(executable.Identity.ArtifactId, lastId) > 0)
                .Take(request.Limit + 1)
                .ToArray();
            var items = ordered.Take(request.Limit).ToArray();
            var nextContinuation = ordered.Length > request.Limit
                ? InMemoryRuntimeStoreContinuation.Encode(queryBinding, items[^1].Identity.ArtifactId)
                : null;
            return ValueTask.FromResult(new RuntimeStorePage<WorkflowExecutable>(request, items, nextContinuation));
        }
    }

    private void RecoverExpiredState(string artifactId, DateTimeOffset now)
    {
        if (_deletionGuards.TryGetValue(artifactId, out var guard) && guard.ExpiresAt <= now)
            _deletionGuards.Remove(artifactId);

        if (!_rootWriteLeases.TryGetValue(artifactId, out var leases))
            return;

        foreach (var leaseId in leases.Where(x => x.Value.ExpiresAt <= now).Select(x => x.Key).ToArray())
            leases.Remove(leaseId);

        if (leases.Count == 0)
            _rootWriteLeases.Remove(artifactId);
    }

    private static void EnsureFutureExpiry(DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (expiresAt <= now)
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "Expiry must be later than the operation time.");
    }

    private static string NewConcurrencyToken() => Guid.NewGuid().ToString("N");

    private sealed class RootWriteLeaseState(WorkflowExecutableRootWriteLease lease, DateTimeOffset expiresAt)
    {
        public WorkflowExecutableRootWriteLease Lease { get; } = lease;
        public DateTimeOffset ExpiresAt { get; set; } = expiresAt;
    }

    private sealed class DeletionGuardState(WorkflowExecutableDeletionGuard guard, DateTimeOffset expiresAt)
    {
        public WorkflowExecutableDeletionGuard Guard { get; } = guard;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
    }
}
