using Elsa.Persistence.Core;
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
public sealed class GroundworkV2DurableValueStateStore : IDurableValueStateStore
{
    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit unit;

    public GroundworkV2DurableValueStateStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        unit = sessions.Unit(ElsaRuntimeV2StorageManifest.DurableValueStateDocumentKind, targetName);
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

        var table = new TableId(unit.Name);
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

    private IStorageSession Open()
    {
        var context = accessContextAccessor.Current;
        if (context.Scope is null || context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Groundwork durable-value state requires one explicit persistence scope; global and across-scope access are refused.");
        }

        return sessions.Open(
            unit.Id.Value,
            StorageAccess.Scoped(new StorageScope(context.Scope.Value)),
            targetName);
    }

    private static WriteOutcome UpdateExisting(
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
        if (session is not IConcurrencyStorageSession concurrency)
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic durable-value concurrency.");

        return concurrency.ConditionalUpsert(values, WriteOptions.IfVersion(revision));
    }

    private static DurableValueState Deserialize(IReadOnlyDictionary<string, object?> values) =>
        GroundworkV2DurableValueStorageConventions.Deserialize(values);

    private static bool IsSaved(WriteOutcomeStatus status) =>
        status is WriteOutcomeStatus.Inserted or WriteOutcomeStatus.Updated or WriteOutcomeStatus.Upserted or WriteOutcomeStatus.Replayed;

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

    private static Predicate Equal(ColumnRef column, string value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    private ColumnRef Column(TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork durable-value unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork durable-value query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private static Paging PagingFor(int limit, string? continuationToken) =>
        continuationToken is null
            ? Paging.Keyset(limit)
            : Paging.Continuation(continuationToken, limit);
}
