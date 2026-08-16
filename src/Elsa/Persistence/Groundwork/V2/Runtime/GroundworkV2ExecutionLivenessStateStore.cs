using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Clean-current Groundwork v2 execution-liveness store.</summary>
public sealed class GroundworkV2ExecutionLivenessStateStore : IExecutionLivenessStateStore
{
    private readonly GroundworkV2RuntimeLivenessContext context;

    public GroundworkV2ExecutionLivenessStateStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        context = new GroundworkV2RuntimeLivenessContext(sessions, accessContextAccessor, targetName);
    }

    public ValueTask<ExecutionLivenessState> SaveAsync(ExecutionLivenessState state, CancellationToken cancellationToken = default)
    {
        ValidateState(state);
        cancellationToken.ThrowIfCancellationRequested();
        var row = context.Open();
        var values = GroundworkV2RuntimeLivenessCodec.Values(state);
        var result = row.Session.Upsert(values, WriteOptions.Unconditional);
        if (!IsSaved(result.Status))
            throw new InvalidOperationException($"Groundwork runtime liveness save failed with status '{result.Status}'.");
        return ValueTask.FromResult(state);
    }

    public ValueTask<ExecutionLivenessStateWriteResult> TrySaveAsync(
        ExecutionLivenessState state,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateState(state);
        if (expectedRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        cancellationToken.ThrowIfCancellationRequested();

        var row = context.Open();
        var values = GroundworkV2RuntimeLivenessCodec.Values(state);
        var result = expectedRevision == 0
            ? row.Session.Insert(values, WriteOptions.CreateOnly)
            : row.ConditionalUpsert(
                GroundworkV2RuntimeLivenessCodec.Identity(state.WorkflowExecutionId, state.OperationalStateId),
                ElsaRuntimeV2StorageManifest.SchemaVersion,
                values.Values[ElsaRuntimeV2StorageManifest.ContentField]!,
                expectedRevision,
                values.Values
                    .Where(pair => pair.Key is not ElsaRuntimeV2StorageManifest.IdField and
                                   not ElsaRuntimeV2StorageManifest.SchemaVersionField and
                                   not ElsaRuntimeV2StorageManifest.ContentField)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

        return ValueTask.FromResult(MapWriteResult(result));
    }

    public ValueTask<ExecutionLivenessState?> FindAsync(
        string workflowExecutionId,
        string operationalStateId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, operationalStateId);
        cancellationToken.ThrowIfCancellationRequested();
        var entry = context.Open().Read(GroundworkV2RuntimeLivenessCodec.Identity(workflowExecutionId, operationalStateId));
        return ValueTask.FromResult(entry is null ? null : GroundworkV2RuntimeLivenessCodec.Deserialize(entry.Values.Values));
    }

    public ValueTask<VersionedExecutionLivenessState?> FindVersionedAsync(
        string workflowExecutionId,
        string operationalStateId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, operationalStateId);
        cancellationToken.ThrowIfCancellationRequested();
        var entry = context.Open().Read(GroundworkV2RuntimeLivenessCodec.Identity(workflowExecutionId, operationalStateId));
        if (entry is null)
            return ValueTask.FromResult<VersionedExecutionLivenessState?>(null);
        var revision = entry.Version ?? throw new InvalidDataException("Groundwork runtime liveness row did not return an optimistic revision.");
        return ValueTask.FromResult<VersionedExecutionLivenessState?>(
            new VersionedExecutionLivenessState(GroundworkV2RuntimeLivenessCodec.Deserialize(entry.Values.Values), revision));
    }

    public ValueTask<RuntimeStorePage<ExecutionLivenessState>> ListPageAsync(
        ExecutionLivenessStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateIdentity(query.WorkflowExecutionId, "page workflow execution ID");
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TableId(context.Unit.Name);
        var workflow = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, QueryType.String, true, ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength);
        var operationalState = Column(table, ElsaRuntimeV2StorageManifest.ExecutionLivenessOperationalStateIdField, QueryType.String, true, ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength);
        var request = new QueryRequest(
            table,
            Equal(workflow, query.WorkflowExecutionId),
            [new OrderTerm(operationalState, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            PagingFor(query.Limit, query.ContinuationToken));
        var page = context.Open().Query(request);
        return ValueTask.FromResult(new RuntimeStorePage<ExecutionLivenessState>(
            query,
            page.Rows.Select(GroundworkV2RuntimeLivenessCodec.Deserialize).ToArray(),
            page.NextContinuationToken));
    }

    public ValueTask<RuntimeStorePage<ExecutionLivenessState>> ListAllPageAsync(
        RuntimeStorePageRequest query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TableId(context.Unit.Name);
        var collection = Column(table, ElsaRuntimeV2StorageManifest.CollectionField, QueryType.String, true, ElsaRuntimeV2StorageManifest.RuntimeCollectionProjectionLength);
        var workflow = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, QueryType.String, true, ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength);
        var operationalState = Column(table, ElsaRuntimeV2StorageManifest.ExecutionLivenessOperationalStateIdField, QueryType.String, true, ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength);
        var request = new QueryRequest(
            table,
            Equal(collection, ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind),
            [
                new OrderTerm(workflow, OrderDirection.Ascending, NullOrder.Last),
                new OrderTerm(operationalState, OrderDirection.Ascending, NullOrder.Last)
            ],
            Projection.All,
            PagingFor(query.Limit, query.ContinuationToken));
        var page = context.Open().Query(request);
        return ValueTask.FromResult(new RuntimeStorePage<ExecutionLivenessState>(
            query,
            page.Rows.Select(GroundworkV2RuntimeLivenessCodec.Deserialize).ToArray(),
            page.NextContinuationToken));
    }

    private static ExecutionLivenessStateWriteResult MapWriteResult(WriteOutcome result) =>
        result.Status switch
        {
            WriteOutcomeStatus.Inserted or WriteOutcomeStatus.Updated or WriteOutcomeStatus.Upserted =>
                new(ExecutionLivenessStateWriteStatus.Saved, result.Version),
            WriteOutcomeStatus.NotFound => new(ExecutionLivenessStateWriteStatus.NotFound, result.Version),
            WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.UniqueViolation or WriteOutcomeStatus.Superseded =>
                new(ExecutionLivenessStateWriteStatus.RevisionConflict, result.Version),
            _ => throw new InvalidOperationException($"Groundwork runtime liveness CAS failed with status '{result.Status}'.")
        };

    private static bool IsSaved(WriteOutcomeStatus status) =>
        status is WriteOutcomeStatus.Inserted or WriteOutcomeStatus.Updated or WriteOutcomeStatus.Upserted or WriteOutcomeStatus.Replayed;

    private static void ValidateState(ExecutionLivenessState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateIdentity(state.WorkflowExecutionId, state.OperationalStateId);
    }

    private static void ValidateIdentity(string workflowExecutionId, string operationalStateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationalStateId);
    }

    private static Predicate Equal(ColumnRef column, object value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    private static Paging PagingFor(int limit, string? continuation) =>
        continuation is null ? Paging.Keyset(limit) : Paging.Continuation(continuation, limit);

    private static ColumnRef Column(TableId table, string name, QueryType type, bool nullable, int? maxLength = null) =>
        new(table, name, type, nullable, maxLength);
}
