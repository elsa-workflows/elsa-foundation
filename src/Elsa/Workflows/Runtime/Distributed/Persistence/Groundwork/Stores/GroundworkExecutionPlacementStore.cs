using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;

/// <summary>Scoped, optimistic-concurrency placement authority backed by Groundwork v2.</summary>
public sealed class GroundworkExecutionPlacementStore(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    string? targetName = null) : IExecutionPlacementStore
{
    private const int MaxCasAttempts = 8;

    public ValueTask<ExecutionPlacementLease?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();
        var entry = Session().Read(Key(workflowExecutionId));
        return ValueTask.FromResult(entry is null ? null : Deserialize(entry.Values.Values));
    }

    public ValueTask<ExecutionPlacementClaimResult> TryClaimAsync(ExecutionPlacementClaim claim, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        for (var attempt = 0; attempt < MaxCasAttempts; attempt++)
        {
            var session = Session();
            var key = Key(claim.WorkflowExecutionId);
            var current = session.Read(key);
            var currentLease = current is null ? null : Deserialize(current.Values.Values);
            if (currentLease is not null && !currentLease.IsExpired(now) &&
                !StringComparer.Ordinal.Equals(currentLease.OwnerId, claim.OwnerId))
            {
                return ValueTask.FromResult(new ExecutionPlacementClaimResult(ExecutionPlacementClaimOutcome.Denied, currentLease));
            }

            var outcome = currentLease is not null &&
                          StringComparer.Ordinal.Equals(currentLease.OwnerId, claim.OwnerId) &&
                          !currentLease.IsExpired(now)
                ? ExecutionPlacementClaimOutcome.Renewed
                : ExecutionPlacementClaimOutcome.Granted;
            var lease = new ExecutionPlacementLease(claim.WorkflowExecutionId, claim.OwnerId, (currentLease?.PlacementToken ?? 0) + 1, claim.RequestedAt, claim.ExpiresAt);
            var options = current?.Version is { } version ? WriteOptions.IfVersion(version) : WriteOptions.CreateOnly;
            var result = ConditionalUpsert(session, Values(lease), options);
            if (result.Succeeded)
                return ValueTask.FromResult(new ExecutionPlacementClaimResult(outcome, lease));
            if (!IsContention(result.Status))
                throw new InvalidOperationException($"Placement claim for workflow execution '{claim.WorkflowExecutionId}' failed with status '{result.Status}'.");
        }

        throw new InvalidOperationException($"Placement claim for workflow execution '{claim.WorkflowExecutionId}' did not settle after {MaxCasAttempts} compare-and-swap attempts.");
    }

    public ValueTask ReleaseAsync(ExecutionPlacementLease lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();

        for (var attempt = 0; attempt < MaxCasAttempts; attempt++)
        {
            var session = Session();
            var key = Key(lease.WorkflowExecutionId);
            var current = session.Read(key);
            if (current is null)
                return ValueTask.CompletedTask;
            var currentLease = Deserialize(current.Values.Values);
            if (!StringComparer.Ordinal.Equals(currentLease.OwnerId, lease.OwnerId) || currentLease.PlacementToken != lease.PlacementToken)
                return ValueTask.CompletedTask;

            var version = current.Version ?? throw new InvalidOperationException("The placement row has no optimistic revision.");
            var result = session.Delete(key, WriteOptions.IfVersion(version));
            if (result.Status is WriteOutcomeStatus.Deleted or WriteOutcomeStatus.NotFound)
                return ValueTask.CompletedTask;
            if (!IsContention(result.Status))
                throw new InvalidOperationException($"Releasing placement '{lease.WorkflowExecutionId}' failed with status '{result.Status}'.");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<ExecutionPlacementLease>> ListOwnedAsync(ExecutionPlacementLeaseListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var result = Session().Query(new QueryRequest(
            new TableId(DistributedGroundworkStorageManifest.PlacementUnitName),
            new Predicate.And([
                Equal(Columns.OwnerId, request.OwnerId),
                new Predicate.Range(Columns.ExpiresAt, Bound.Exclusive(QueryConstant.Of(Columns.ExpiresAt, request.Now)), null)
            ]),
            [new OrderTerm(Columns.ExpiresAt, OrderDirection.Ascending, NullOrder.Last), new OrderTerm(Columns.WorkflowExecutionId, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            Paging.Keyset(request.Take)));
        IReadOnlyList<ExecutionPlacementLease> leases = result.Rows.Select(Deserialize).ToArray();
        return ValueTask.FromResult(leases);
    }

    private IStorageSession Session() => sessions.Open(
        DistributedGroundworkStorageManifest.PlacementUnitId,
        StorageAccess.Scoped(new StorageScope(RequireScope())),
        targetName);

    private string RequireScope() => accessContextAccessor.Current.Scope?.Value ??
        throw new InvalidOperationException("Groundwork distributed stores require a scoped persistence access context.");

    private static StorageKey Key(string workflowExecutionId) => new(new Dictionary<string, object?>
    {
        [DistributedGroundworkStorageManifest.WorkflowExecutionIdField] = workflowExecutionId
    });

    private static StorageValues Values(ExecutionPlacementLease lease) => new(new Dictionary<string, object?>
    {
        [DistributedGroundworkStorageManifest.WorkflowExecutionIdField] = lease.WorkflowExecutionId,
        [DistributedGroundworkStorageManifest.OwnerIdField] = lease.OwnerId,
        [DistributedGroundworkStorageManifest.PlacementTokenField] = lease.PlacementToken,
        [DistributedGroundworkStorageManifest.AcquiredAtField] = lease.AcquiredAt,
        [DistributedGroundworkStorageManifest.ExpiresAtField] = lease.ExpiresAt,
        [DistributedGroundworkStorageManifest.PayloadField] = DistributedGroundworkDocuments.Serialize(lease)
    });

    private static ExecutionPlacementLease Deserialize(IReadOnlyDictionary<string, object?> values) =>
        DistributedGroundworkDocuments.Deserialize<ExecutionPlacementLease>(values, DistributedGroundworkStorageManifest.PayloadField);

    private static Predicate Equal(ColumnRef column, object value) => new Predicate.Equal(column, QueryConstant.Of(column, value));

    private static WriteOutcome ConditionalUpsert(IStorageSession session, StorageValues values, WriteOptions options)
    {
        if (session is not IConcurrencyStorageSession concurrency)
            throw new NotSupportedException("The selected Groundwork provider does not support placement compare-and-swap.");
        return concurrency.ConditionalUpsert(values, options);
    }

    private static bool IsContention(WriteOutcomeStatus status) => status is WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.UniqueViolation or WriteOutcomeStatus.NotFound or WriteOutcomeStatus.Superseded;

    private static class Columns
    {
        private static readonly TableId Table = new(DistributedGroundworkStorageManifest.PlacementUnitName);
        internal static ColumnRef WorkflowExecutionId { get; } = String(DistributedGroundworkStorageManifest.WorkflowExecutionIdField, false);
        internal static ColumnRef OwnerId { get; } = String(DistributedGroundworkStorageManifest.OwnerIdField, false);
        internal static ColumnRef ExpiresAt { get; } = new(Table, DistributedGroundworkStorageManifest.ExpiresAtField, QueryType.DateTimeOffset, false);

        private static ColumnRef String(string name, bool nullable) => new(Table, name, QueryType.String, nullable, DistributedRuntimeIdentityConstraints.MaximumLength);
    }
}
