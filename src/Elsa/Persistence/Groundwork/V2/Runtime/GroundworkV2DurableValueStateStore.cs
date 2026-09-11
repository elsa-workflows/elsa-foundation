using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 durable-value state store.</summary>
/// <remarks>
/// Durable-value rows use the shared injective (workflow execution ID, durable value ID) physical identity,
/// while both logical components remain projected for the declared workflow index. Saves and deletes use
/// provider optimistic concurrency. A save conflict is surfaced as a deterministic retryable failure;
/// delete returns false when the row changed or disappeared before the conditional delete completed.
/// </remarks>
public sealed class GroundworkV2DurableValueStateStore : GroundworkV2RuntimeStoreBase, IDurableValueStateStore
{

    public GroundworkV2DurableValueStateStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
        : base(sessions, accessContextAccessor, targetName, "durable-value state", ElsaRuntimeV2StorageManifest.DurableValueStateDocumentKind)
    {
    }

    public ValueTask<DurableValueState> SaveAsync(
        DurableValueState state,
        CancellationToken cancellationToken = default)
    {
        ValidateState(state);
        cancellationToken.ThrowIfCancellationRequested();

        var session = Open();
        var physicalId = GroundworkV2DurableValueStorageConventions.PhysicalId(
            state.WorkflowExecutionId,
            state.DurableValueId);
        var key = GroundworkRuntimeRowStore.Key(physicalId);
        var values = GroundworkV2DurableValueStorageConventions.Values(state);
        var result = session.Read(key) is { } existing
            ? UpdateExisting(session, values, existing, state)
            : session.Insert(values, WriteOptions.CreateOnly);
        if (!IsSaved(result.Status))
        {
            throw new InvalidOperationException(
                "Groundwork durable-value save lost a concurrent write; retry the operation.");
        }

        return ValueTask.FromResult(state);
    }

    public ValueTask<bool> DeleteAsync(
        string workflowExecutionId,
        string durableValueId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, durableValueId);
        cancellationToken.ThrowIfCancellationRequested();

        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2DurableValueStorageConventions.PhysicalId(workflowExecutionId, durableValueId));
        if (session.Read(key) is not { } existing)
            return ValueTask.FromResult(false);

        var state = Deserialize(existing.Values.Values);
        if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(state.DurableValueId, durableValueId))
        {
            throw new InvalidDataException("Groundwork durable-value row identity does not match its requested key.");
        }

        var revision = existing.Version ??
                       throw new InvalidDataException("Groundwork durable-value row did not return an optimistic revision.");
        var result = session.Delete(key, WriteOptions.IfVersion(revision));
        if (result.Status is not (WriteOutcomeStatus.Deleted or WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.NotFound))
            throw new InvalidOperationException("Groundwork durable-value delete failed; retry the operation.");

        return ValueTask.FromResult(result.Status == WriteOutcomeStatus.Deleted);
    }

    public ValueTask<DurableValueState?> FindAsync(
        string workflowExecutionId,
        string durableValueId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, durableValueId);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = Open().Read(
            GroundworkRuntimeRowStore.Key(
                GroundworkV2DurableValueStorageConventions.PhysicalId(workflowExecutionId, durableValueId)));
        if (entry is null)
            return ValueTask.FromResult<DurableValueState?>(null);

        var state = Deserialize(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(state.DurableValueId, durableValueId))
        {
            throw new InvalidDataException("Groundwork durable-value row identity does not match its requested key.");
        }

        return ValueTask.FromResult<DurableValueState?>(state);
    }

    public ValueTask<RuntimeStorePage<DurableValueState>> ListPageAsync(
        DurableValueStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var table = new TableId(Unit.Name);
        var workflow = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var durableValue = Column(table, ElsaRuntimeV2StorageManifest.DurableValueIdField);
        var request = new QueryRequest(
            table,
            Equal(workflow, query.WorkflowExecutionId),
            [new OrderTerm(durableValue, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            PagingFor(query.Limit, query.ContinuationToken));
        var result = Open().Query(request);
        return ValueTask.FromResult(new RuntimeStorePage<DurableValueState>(
            query,
            result.Rows.Select(Deserialize).ToArray(),
            result.NextContinuationToken));
    }

    private WriteOutcome UpdateExisting(
        IStorageSession session,
        StorageValues values,
        StoredEntry existing,
        DurableValueState state)
    {
        var previous = Deserialize(existing.Values.Values);
        if (!StringComparer.Ordinal.Equals(previous.WorkflowExecutionId, state.WorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(previous.DurableValueId, state.DurableValueId))
        {
            throw new InvalidDataException("Groundwork durable-value row identity does not match its current content.");
        }

        var revision = existing.Version ??
                       throw new InvalidDataException("Groundwork durable-value row did not return an optimistic revision.");
        return ConditionalUpsert(session, values, revision);
    }

    private static DurableValueState Deserialize(IReadOnlyDictionary<string, object?> values) =>
        GroundworkV2DurableValueStorageConventions.Deserialize(values);

    private static void ValidateState(DurableValueState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateIdentity(state.WorkflowExecutionId, state.DurableValueId);
        _ = GroundworkV2DurableValueStorageConventions.PhysicalId(state.WorkflowExecutionId, state.DurableValueId);
    }

    private static void ValidateIdentity(string workflowExecutionId, string durableValueId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableValueId);
    }
}
