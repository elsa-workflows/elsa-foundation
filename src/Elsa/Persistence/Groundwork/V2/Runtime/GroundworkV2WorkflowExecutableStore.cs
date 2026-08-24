using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 immutable workflow executable and retention-coordination store.</summary>
public sealed class GroundworkV2WorkflowExecutableStore : IWorkflowExecutableStore
{
    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly ILogger<GroundworkV2WorkflowExecutableStore> logger;
    private readonly StorageUnit executableUnit;
    private readonly StorageUnit coordinationUnit;

    public GroundworkV2WorkflowExecutableStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null,
        ILogger<GroundworkV2WorkflowExecutableStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        this.logger = logger ?? NullLogger<GroundworkV2WorkflowExecutableStore>.Instance;
        executableUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind, targetName);
        coordinationUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind, targetName);
    }

    public async ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
    {
        GroundworkV2WorkflowExecutableStorageConventions.Validate(executable);
        cancellationToken.ThrowIfCancellationRequested();
        var key = GroundworkRuntimeRowStore.Key(executable.Identity.ArtifactId);
        var current = OpenExecutable().Read(key);
        if (current is not null)
        {
            _ = GroundworkV2WorkflowExecutableStorageConventions.Deserialize(current.Values.Values);
            var coordination = OpenCoordination().Read(key);
            if (coordination is null)
                throw new InvalidDataException($"Workflow executable '{executable.Identity.ArtifactId}' has no current coordination row.");
            _ = GroundworkV2WorkflowExecutableStorageConventions.DeserializeCoordination(
                coordination.Values.Values,
                executable.Identity.ArtifactId);
            return;
        }

        RequireAtomicCommit();
        using var unitOfWork = BeginAtomicUnitOfWork();
        unitOfWork.Stage(RowWrite.Insert(
            executableUnit,
            GroundworkV2WorkflowExecutableStorageConventions.Values(executable),
            WriteOptions.CreateOnly));
        unitOfWork.Stage(RowWrite.Insert(
            coordinationUnit,
            GroundworkV2WorkflowExecutableStorageConventions.EmptyCoordinationValues(executable.Identity.ArtifactId),
            WriteOptions.CreateOnly));
        BatchWriteReport report;
        try
        {
            report = await CommitAsync(unitOfWork, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (HasCompleteWinningArtifact(executable.Identity.ArtifactId))
                return;
            throw;
        }
        if (report.IsSuccessful)
            return;

        if (HasCompleteWinningArtifact(executable.Identity.ArtifactId))
            return;

        throw new InvalidOperationException(
            $"Groundwork rejected workflow executable '{executable.Identity.ArtifactId}' without a complete winning artifact.");
    }

    public ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        cancellationToken.ThrowIfCancellationRequested();
        var row = OpenExecutable().Read(GroundworkRuntimeRowStore.Key(artifactId));
        return ValueTask.FromResult(row is null
            ? null
            : GroundworkV2WorkflowExecutableStorageConventions.Deserialize(row.Values.Values));
    }

    public async ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        cancellationToken.ThrowIfCancellationRequested();
        RequireAtomicCommit();
        for (var attempt = 0; attempt < 32; attempt++)
        {
            using var unitOfWork = BeginAtomicUnitOfWork();
            var key = GroundworkRuntimeRowStore.Key(artifactId);
            var executable = unitOfWork.OpenSession(executableUnit).Read(key);
            var coordination = unitOfWork.OpenSession(coordinationUnit).Read(key);
            if (executable is null && coordination is null)
                return false;
            if (executable is null || coordination is null)
                throw new InvalidDataException($"Workflow executable '{artifactId}' has incomplete current storage state.");

            unitOfWork.Stage(RowWrite.Delete(executableUnit, key, WriteOptions.IfVersion(RequiredVersion(executable, artifactId))));
            unitOfWork.Stage(RowWrite.Delete(coordinationUnit, key, WriteOptions.IfVersion(RequiredVersion(coordination, artifactId))));
            if ((await CommitAsync(unitOfWork, cancellationToken)).IsSuccessful)
                return true;
        }

        throw ConcurrentChange(artifactId);
    }

    public ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(
        string artifactId,
        string leaseId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ValidateTransition(artifactId, leaseId, expiresAt, now, nameof(leaseId));
        cancellationToken.ThrowIfCancellationRequested();
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var loaded = ReadCoordination(artifactId);
            if (loaded is null)
                return ValueTask.FromResult<WorkflowExecutableRootWriteLease?>(null);
            var leases = LiveLeases(loaded.Value.State, now);
            if (IsLive(loaded.Value.State.DeletionGuard, now))
                return ValueTask.FromResult<WorkflowExecutableRootWriteLease?>(null);
            if (leases.TryGetValue(leaseId, out var existing))
                return ValueTask.FromResult<WorkflowExecutableRootWriteLease?>(ToLease(artifactId, existing));

            var created = new GroundworkV2WorkflowExecutableStorageConventions.RootWriteLeaseState(
                leaseId,
                NewFencingToken(),
                expiresAt);
            leases.Add(leaseId, created);
            if (TryUpdateCoordination(
                    artifactId,
                    new GroundworkV2WorkflowExecutableStorageConventions.CoordinationState(leases, null),
                    loaded.Value.Version))
                return ValueTask.FromResult<WorkflowExecutableRootWriteLease?>(ToLease(artifactId, created));
        }

        throw ConcurrentChange(artifactId);
    }

    public ValueTask<bool> RenewRootWriteLeaseAsync(
        WorkflowExecutableRootWriteLease lease,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ValidateTransition(lease.ArtifactId, lease.LeaseId, expiresAt, now, nameof(lease.LeaseId));
        cancellationToken.ThrowIfCancellationRequested();
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var loaded = ReadCoordination(lease.ArtifactId);
            if (loaded is null)
                return ValueTask.FromResult(false);
            var leases = LiveLeases(loaded.Value.State, now);
            if (!leases.TryGetValue(lease.LeaseId, out var current) ||
                !StringComparer.Ordinal.Equals(current.FencingToken, lease.ConcurrencyToken) ||
                IsLive(loaded.Value.State.DeletionGuard, now))
                return ValueTask.FromResult(false);

            leases[lease.LeaseId] = current with { ExpiresAt = expiresAt };
            if (TryUpdateCoordination(
                    lease.ArtifactId,
                    new GroundworkV2WorkflowExecutableStorageConventions.CoordinationState(leases, null),
                    loaded.Value.Version))
                return ValueTask.FromResult(true);
        }

        throw ConcurrentChange(lease.ArtifactId);
    }

    public ValueTask ReleaseRootWriteLeaseAsync(
        WorkflowExecutableRootWriteLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(lease.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(lease.LeaseId);
        cancellationToken.ThrowIfCancellationRequested();
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var loaded = ReadCoordination(lease.ArtifactId);
            if (loaded is null)
                return ValueTask.CompletedTask;
            var leases = CopyLeases(loaded.Value.State);
            if (!leases.TryGetValue(lease.LeaseId, out var current) ||
                !StringComparer.Ordinal.Equals(current.FencingToken, lease.ConcurrencyToken))
                return ValueTask.CompletedTask;
            leases.Remove(lease.LeaseId);
            if (TryUpdateCoordination(
                    lease.ArtifactId,
                    loaded.Value.State with { RootWriteLeases = leases },
                    loaded.Value.Version))
                return ValueTask.CompletedTask;
        }

        throw ConcurrentChange(lease.ArtifactId);
    }

    public ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(
        string artifactId,
        string operationId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ValidateTransition(artifactId, operationId, expiresAt, now, nameof(operationId));
        cancellationToken.ThrowIfCancellationRequested();
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var loaded = ReadCoordination(artifactId);
            if (loaded is null)
                return ValueTask.FromResult<WorkflowExecutableDeletionGuard?>(null);
            var leases = LiveLeases(loaded.Value.State, now);
            if (leases.Count != 0)
                return ValueTask.FromResult<WorkflowExecutableDeletionGuard?>(null);
            if (IsLive(loaded.Value.State.DeletionGuard, now))
            {
                var existing = loaded.Value.State.DeletionGuard!;
                return ValueTask.FromResult<WorkflowExecutableDeletionGuard?>(
                    StringComparer.Ordinal.Equals(existing.OperationId, operationId)
                        ? ToGuard(artifactId, existing)
                        : null);
            }

            var created = new GroundworkV2WorkflowExecutableStorageConventions.DeletionGuardState(
                operationId,
                NewFencingToken(),
                expiresAt);
            if (TryUpdateCoordination(
                    artifactId,
                    new GroundworkV2WorkflowExecutableStorageConventions.CoordinationState(leases, created),
                    loaded.Value.Version))
                return ValueTask.FromResult<WorkflowExecutableDeletionGuard?>(ToGuard(artifactId, created));
        }

        throw ConcurrentChange(artifactId);
    }

    public ValueTask<bool> CancelDeletionAsync(
        WorkflowExecutableDeletionGuard guard,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.OperationId);
        cancellationToken.ThrowIfCancellationRequested();
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var loaded = ReadCoordination(guard.ArtifactId);
            if (loaded is null || !Matches(loaded.Value.State.DeletionGuard, guard))
                return ValueTask.FromResult(false);
            if (TryUpdateCoordination(
                    guard.ArtifactId,
                    loaded.Value.State with { DeletionGuard = null },
                    loaded.Value.Version))
                return ValueTask.FromResult(true);
        }

        throw ConcurrentChange(guard.ArtifactId);
    }

    public async ValueTask<bool> DeleteAsync(
        WorkflowExecutableDeletionGuard guard,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.OperationId);
        cancellationToken.ThrowIfCancellationRequested();
        RequireAtomicCommit();
        for (var attempt = 0; attempt < 32; attempt++)
        {
            using var unitOfWork = BeginAtomicUnitOfWork();
            var key = GroundworkRuntimeRowStore.Key(guard.ArtifactId);
            var executable = unitOfWork.OpenSession(executableUnit).Read(key);
            var coordination = unitOfWork.OpenSession(coordinationUnit).Read(key);
            if (executable is null || coordination is null)
                return false;
            var state = GroundworkV2WorkflowExecutableStorageConventions.DeserializeCoordination(
                coordination.Values.Values,
                guard.ArtifactId);
            if (!Matches(state.DeletionGuard, guard) || !IsLive(state.DeletionGuard, now) ||
                LiveLeases(state, now).Count != 0)
                return false;

            unitOfWork.Stage(RowWrite.Delete(
                executableUnit,
                key,
                WriteOptions.IfVersion(RequiredVersion(executable, guard.ArtifactId))));
            unitOfWork.Stage(RowWrite.Delete(
                coordinationUnit,
                key,
                WriteOptions.IfVersion(RequiredVersion(coordination, guard.ArtifactId))));
            if ((await CommitAsync(unitOfWork, cancellationToken)).IsSuccessful)
                return true;
        }

        throw ConcurrentChange(guard.ArtifactId);
    }

    public ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(
        RuntimeStorePageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var continuation = request.ContinuationToken;
        var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
        if (continuation is not null)
            seenContinuations.Add(continuation);
        while (true)
        {
            var table = new TableId(executableUnit.Name);
            var collection = Column(table, ElsaRuntimeV2StorageManifest.CollectionField);
            var artifact = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutableArtifactIdField);
            var query = new QueryRequest(
                table,
                new Predicate.Equal(
                    collection,
                    QueryConstant.Of(collection, ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind)),
                [new OrderTerm(artifact, OrderDirection.Ascending, NullOrder.Last)],
                Projection.All,
                continuation is null
                    ? Paging.Keyset(request.Limit)
                    : Paging.Continuation(continuation, request.Limit));
            var result = OpenExecutable().Query(query);
            var executables = new List<WorkflowExecutable>(result.Rows.Count);
            foreach (var row in result.Rows)
            {
                try
                {
                    executables.Add(GroundworkV2WorkflowExecutableStorageConventions.Deserialize(row));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(
                        exception,
                        "Skipping undeserializable Groundwork v2 workflow executable row '{RowId}' while listing artifacts.",
                        row.TryGetValue(ElsaRuntimeV2StorageManifest.IdField, out var rowId) ? rowId : null);
                    // A poison artifact cannot prevent healthy immutable artifacts from loading. The
                    // caller continues through provider-owned pages; direct identity reads still fail
                    // loudly so corruption remains observable at the requested artifact boundary.
                }
            }

            if (executables.Count != 0 || result.NextContinuationToken is null)
            {
                return ValueTask.FromResult(new RuntimeStorePage<WorkflowExecutable>(
                    request,
                    executables,
                    result.NextContinuationToken));
            }

            continuation = result.NextContinuationToken;
            if (!seenContinuations.Add(continuation))
            {
                throw new InvalidDataException(
                    "Groundwork workflow executable listing returned a repeated continuation token.");
            }
        }
    }

    private IStorageSession OpenExecutable() => sessions.Open(executableUnit.Id.Value, Access, targetName);

    private IStorageSession OpenCoordination() => sessions.Open(coordinationUnit.Id.Value, Access, targetName);

    private ColumnRef Column(TableId table, string name)
    {
        var definition = executableUnit.Columns.Single(column => StringComparer.Ordinal.Equals(column.Name, name));
        return new ColumnRef(
            table,
            name,
            QueryType.String,
            definition.IsNullable,
            definition.MaxLength);
    }

    private (GroundworkV2WorkflowExecutableStorageConventions.CoordinationState State, long Version)? ReadCoordination(
        string artifactId)
    {
        var key = GroundworkRuntimeRowStore.Key(artifactId);
        var row = OpenCoordination().Read(key);
        if (row is null)
            return null;
        if (OpenExecutable().Read(key) is null)
            return null;
        return (
            GroundworkV2WorkflowExecutableStorageConventions.DeserializeCoordination(row.Values.Values, artifactId),
            RequiredVersion(row, artifactId));
    }

    private bool HasCompleteWinningArtifact(string artifactId)
    {
        var key = GroundworkRuntimeRowStore.Key(artifactId);
        var winner = OpenExecutable().Read(key);
        var coordination = OpenCoordination().Read(key);
        if (winner is null || coordination is null)
            return false;
        _ = GroundworkV2WorkflowExecutableStorageConventions.Deserialize(winner.Values.Values);
        _ = GroundworkV2WorkflowExecutableStorageConventions.DeserializeCoordination(
            coordination.Values.Values,
            artifactId);
        return true;
    }

    private bool TryUpdateCoordination(
        string artifactId,
        GroundworkV2WorkflowExecutableStorageConventions.CoordinationState state,
        long expectedVersion)
    {
        if (OpenCoordination() is not IConcurrencyStorageSession concurrency)
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic workflow executable coordination.");
        var outcome = concurrency.ConditionalUpsert(
            GroundworkV2WorkflowExecutableStorageConventions.CoordinationValues(artifactId, state),
            WriteOptions.IfVersion(expectedVersion));
        return IsSaved(outcome.Status);
    }

    private IUnitOfWork BeginAtomicUnitOfWork() => sessions.BeginUnitOfWork(
        Access,
        BatchWriteOptions.Exact,
        [
            ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind,
            ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind
        ],
        targetName);

    private StorageAccess Access
    {
        get
        {
            var context = accessContextAccessor.Current ??
                          throw new InvalidOperationException("Workflow executable persistence access context is missing.");
            if (context.Scope is null || context.AcrossScopes)
            {
                throw new InvalidOperationException(
                    "Groundwork workflow executables require one explicit persistence scope; global and across-scope access are refused.");
            }

            return StorageAccess.Scoped(new StorageScope(context.Scope.Value));
        }
    }

    private void RequireAtomicCommit()
    {
        if (sessions is not IGroundworkStorageCapabilitySource capabilitySource ||
            !capabilitySource.Capabilities(targetName).Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit)))
        {
            throw new NotSupportedException(
                "Groundwork workflow executable changes require the provider's evidenced atomic-commit capability.");
        }
    }

    private static async ValueTask<BatchWriteReport> CommitAsync(
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        try
        {
            var report = await unitOfWork.CommitWithOutcomesAsync(cancellationToken);
            if (!report.IsSuccessful)
            {
                try
                {
                    unitOfWork.Rollback();
                }
                catch
                {
                    // Preserve the provider's attributed row outcomes.
                }
            }

            return report;
        }
        catch
        {
            try
            {
                unitOfWork.Rollback();
            }
            catch
            {
                // Preserve the provider's original exception.
            }

            throw;
        }
    }

    private static long RequiredVersion(StoredEntry row, string artifactId) =>
        row.Version ?? throw new InvalidDataException(
            $"Groundwork workflow executable row '{artifactId}' did not expose an optimistic revision.");

    private static Dictionary<string, GroundworkV2WorkflowExecutableStorageConventions.RootWriteLeaseState> CopyLeases(
        GroundworkV2WorkflowExecutableStorageConventions.CoordinationState state) =>
        new(state.RootWriteLeases, StringComparer.Ordinal);

    private static Dictionary<string, GroundworkV2WorkflowExecutableStorageConventions.RootWriteLeaseState> LiveLeases(
        GroundworkV2WorkflowExecutableStorageConventions.CoordinationState state,
        DateTimeOffset now) =>
        state.RootWriteLeases
            .Where(pair => pair.Value.ExpiresAt > now)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static bool IsLive(
        GroundworkV2WorkflowExecutableStorageConventions.DeletionGuardState? guard,
        DateTimeOffset now) => guard is not null && guard.ExpiresAt > now;

    private static bool Matches(
        GroundworkV2WorkflowExecutableStorageConventions.DeletionGuardState? state,
        WorkflowExecutableDeletionGuard guard) =>
        state is not null &&
        StringComparer.Ordinal.Equals(state.OperationId, guard.OperationId) &&
        StringComparer.Ordinal.Equals(state.FencingToken, guard.ConcurrencyToken);

    private static WorkflowExecutableRootWriteLease ToLease(
        string artifactId,
        GroundworkV2WorkflowExecutableStorageConventions.RootWriteLeaseState state) =>
        new(artifactId, state.LeaseId, state.FencingToken);

    private static WorkflowExecutableDeletionGuard ToGuard(
        string artifactId,
        GroundworkV2WorkflowExecutableStorageConventions.DeletionGuardState state) =>
        new(artifactId, state.OperationId, state.FencingToken);

    private static string NewFencingToken() => Guid.NewGuid().ToString("N");

    private static bool IsSaved(WriteOutcomeStatus status) =>
        status is WriteOutcomeStatus.Inserted or WriteOutcomeStatus.Updated or WriteOutcomeStatus.Upserted or WriteOutcomeStatus.Replayed;

    private static void ValidateTransition(
        string artifactId,
        string ownerId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        string ownerParameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId, ownerParameterName);
        if (expiresAt <= now)
            throw new ArgumentOutOfRangeException(nameof(expiresAt), expiresAt, "The retention guard expiry must be later than now.");
    }

    private static InvalidOperationException ConcurrentChange(string artifactId) =>
        new($"Workflow executable coordination for '{artifactId}' changed too frequently; retry the operation.");
}
