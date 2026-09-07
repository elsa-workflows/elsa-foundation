using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 workflow-execution state store.</summary>
public sealed class GroundworkV2WorkflowExecutionStateStore : IWorkflowExecutionStateStore
{
    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit unit;

    public GroundworkV2WorkflowExecutionStateStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        unit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind, targetName);
    }

    public ValueTask<WorkflowExecutionState> SaveAsync(
        WorkflowExecutionState state,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2WorkflowExecutionStorageConventions.Validate(state);
        cancellationToken.ThrowIfCancellationRequested();
        AccessContext.EnsureTenantScope(state.TenantId);

        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(state.WorkflowExecutionId);
        var values = GroundworkV2WorkflowExecutionStorageConventions.Values(state);
        var result = session.Read(key) is { } existing
            ? UpdateExisting(session, values, existing, state.WorkflowExecutionId)
            : session.Insert(values, WriteOptions.CreateOnly);
        if (!IsSaved(result.Status))
        {
            throw new InvalidOperationException(
                "Groundwork workflow-execution save lost a concurrent write; retry the operation.");
        }

        return ValueTask.FromResult(state);
    }

    public ValueTask<WorkflowExecutionState?> FindAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var entry = Open().Read(GroundworkRuntimeRowStore.Key(workflowExecutionId));
        if (entry is null)
            return ValueTask.FromResult<WorkflowExecutionState?>(null);
        var state = GroundworkV2WorkflowExecutionStorageConventions.Deserialize(entry.Values.Values);
        EnsureIdentity(state, workflowExecutionId);
        return ValueTask.FromResult<WorkflowExecutionState?>(state);
    }

    public ValueTask<bool> DeleteAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(workflowExecutionId);
        if (session.Read(key) is not { } existing)
            return ValueTask.FromResult(false);
        EnsureIdentity(
            GroundworkV2WorkflowExecutionStorageConventions.Deserialize(existing.Values.Values),
            workflowExecutionId);
        var version = existing.Version ?? throw new InvalidDataException(
            "Groundwork workflow-execution row did not return an optimistic revision.");
        var result = session.Delete(key, WriteOptions.IfVersion(version));
        if (result.Status is not (WriteOutcomeStatus.Deleted or
            WriteOutcomeStatus.ConcurrencyConflict or
            WriteOutcomeStatus.NotFound))
        {
            throw new InvalidOperationException(
                "Groundwork workflow-execution delete failed; retry the operation.");
        }

        return ValueTask.FromResult(result.Status == WriteOutcomeStatus.Deleted);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var states = new List<WorkflowExecutionState>();
        string? cursor = null;
        do
        {
            var page = await QueryPageAsync(
                new WorkflowExecutionStatePageQuery(
                    RuntimeStorePageRequest.MaximumLimit,
                    Cursor: cursor),
                cancellationToken);
            states.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);

        return states;
    }

    public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(
        WorkflowExecutionStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        AccessContext.EnsureTenantScope(query.TenantId);

        var table = new TableId(unit.Name);
        var predicates = BuildHistoryPredicates(table, query);
        var sortTicks = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistorySortTicksField);
        var executionId = Column(
            table,
            ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField);
        var request = new QueryRequest(
            table,
            Combine(predicates),
            [
                new OrderTerm(sortTicks, OrderDirection.Descending, NullOrder.Last),
                new OrderTerm(executionId, OrderDirection.Ascending, NullOrder.Last)
            ],
            Projection.All,
            PagingFor(query.PageSize, query.Cursor),
            ResultShape.TotalCount.Instance);
        var result = QueryWithBoundCursor(
            request,
            query.Cursor,
            "The workflow execution history cursor is invalid or does not belong to this query.");

        var totalCount = result.TotalCount ?? throw new InvalidDataException(
            "Groundwork workflow-execution history did not return its requested filtered total count.");
        var items = result.Rows.Select(ReadAndValidateTenant).ToArray();
        return ValueTask.FromResult(new WorkflowExecutionStatePage(
            items,
            result.NextContinuationToken,
            result.NextContinuationToken is not null,
            totalCount));
    }

    public ValueTask<WorkflowExecutionAlterationCapturePage> QueryAlterationCapturePageAsync(
        WorkflowExecutionAlterationCaptureQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        AccessContext.EnsureTenantScope(query.TenantPartition);

        var table = new TableId(unit.Name);
        var predicates = BuildSelectorPredicates(table, query.Selector);
        AddEqual(
            predicates,
            table,
            ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryTenantIdField,
            query.TenantPartition);
        AddEqual(
            predicates,
            table,
            ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryAuthorityPartitionField,
            query.AuthorityPartitionKey);
        var executionId = Column(
            table,
            ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField);
        var request = new QueryRequest(
            table,
            Combine(predicates),
            [new OrderTerm(executionId, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            PagingFor(query.PageSize, query.Cursor));
        var result = QueryWithBoundCursor(
            request,
            query.Cursor,
            "The alteration capture cursor is invalid or does not belong to this query.");
        var items = result.Rows.Select(values => ReadAndValidateAlteration(values, query)).ToArray();
        return ValueTask.FromResult(new WorkflowExecutionAlterationCapturePage(
            items,
            result.NextContinuationToken,
            result.NextContinuationToken is not null));
    }

    public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TableId(unit.Name);
        var collection = Column(table, ElsaRuntimeV2StorageManifest.CollectionField);
        var artifact = Column(
            table,
            ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryArtifactIdField);
        var executionId = Column(
            table,
            ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField);
        var artifactTimestamp = Column(
            table,
            ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryArtifactTimestampField);
        var where = new Predicate.And([
            new Predicate.Equal(collection, QueryConstant.Of(
                collection,
                ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind)),
            new Predicate.Not(new Predicate.Equal(artifact, QueryConstant.Of(artifact, null))),
            new Predicate.Not(new Predicate.Equal(executionId, QueryConstant.Of(executionId, null)))
        ]);
        var artifactIds = new List<string>();
        string? cursor = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new QueryRequest(
                table,
                where,
                [
                    new OrderTerm(artifact, OrderDirection.Ascending, NullOrder.Last),
                    new OrderTerm(executionId, OrderDirection.Ascending, NullOrder.Last)
                ],
                Projection.ColumnsOnly(artifact),
                PagingFor(RuntimeStorePageRequest.MaximumLimit, cursor),
                new LatestPerKey(artifact, artifactTimestamp));
            var result = QueryWithBoundCursor(
                request,
                cursor,
                "The workflow execution retention cursor is invalid or no longer belongs to this query.");
            artifactIds.AddRange(result.Rows.Select(row => RequiredProjectedString(
                row,
                ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryArtifactIdField)));
            cursor = result.NextContinuationToken;
        } while (cursor is not null);

        return ValueTask.FromResult<IReadOnlyCollection<string>>(artifactIds);
    }

    private IStorageSession Open()
    {
        var context = AccessContext;
        if (context.Scope is null || context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Groundwork workflow-execution state requires one explicit persistence scope; " +
                "global and across-scope access are refused.");
        }

        return sessions.Open(
            unit.Id.Value,
            StorageAccess.Scoped(new StorageScope(context.Scope.Value)),
            targetName);
    }

    private PersistenceAccessContext AccessContext => accessContextAccessor.Current ??
        throw new InvalidOperationException(
            "Groundwork workflow-execution persistence access context is missing.");

    private static WriteOutcome UpdateExisting(
        IStorageSession session,
        StorageValues values,
        StoredEntry existing,
        string workflowExecutionId)
    {
        EnsureIdentity(
            GroundworkV2WorkflowExecutionStorageConventions.Deserialize(existing.Values.Values),
            workflowExecutionId);
        var version = existing.Version ?? throw new InvalidDataException(
            "Groundwork workflow-execution row did not return an optimistic revision.");
        if (session is not IConcurrencyStorageSession concurrency)
        {
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic workflow-execution concurrency.");
        }

        return concurrency.ConditionalUpsert(values, WriteOptions.IfVersion(version));
    }

    private static void EnsureIdentity(WorkflowExecutionState state, string expectedId)
    {
        if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, expectedId))
        {
            throw new InvalidDataException(
                "Groundwork workflow-execution row identity does not match its requested key.");
        }
    }

    private static bool IsSaved(WriteOutcomeStatus status) =>
        status is WriteOutcomeStatus.Inserted or
        WriteOutcomeStatus.Updated or
        WriteOutcomeStatus.Upserted or
        WriteOutcomeStatus.Replayed;

    private WorkflowExecutionState ReadAndValidateTenant(IReadOnlyDictionary<string, object?> values)
    {
        var state = GroundworkV2WorkflowExecutionStorageConventions.Deserialize(values);
        AccessContext.EnsureTenantScope(state.TenantId);
        return state;
    }

    private WorkflowExecutionState ReadAndValidateAlteration(
        IReadOnlyDictionary<string, object?> values,
        WorkflowExecutionAlterationCaptureQuery query)
    {
        var state = ReadAndValidateTenant(values);
        if (!StringComparer.Ordinal.Equals(state.TenantId, query.TenantPartition) ||
            state.Authority is not { } authority ||
            !WorkflowExecutionAuthoritySnapshot.Matches(
                authority,
                query.SystemIdentity,
                query.RootInitiator,
                query.AuthorityMetadata))
        {
            throw new InvalidDataException(
                "Groundwork alteration capture returned a row outside its authorized tenant partition.");
        }

        return state;
    }

    private IReadOnlyList<Predicate> BuildHistoryPredicates(
        TableId table,
        WorkflowExecutionStatePageQuery query)
    {
        var predicates = BuildSelectorPredicates(
            table,
            new WorkflowAlterationQuerySelector(
                query.DefinitionId,
                query.Status,
                query.RunKind,
                query.From,
                query.To,
                query.CorrelationId,
                query.WorkflowExecutionId,
                query.ArtifactId,
                matchAllAuthorized: true));
        AddEqual(
            predicates,
            table,
            ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryTenantIdField,
            query.TenantId);
        return predicates;
    }

    private List<Predicate> BuildSelectorPredicates(
        TableId table,
        WorkflowAlterationQuerySelector selector)
    {
        var predicates = new List<Predicate>();
        AddEqual(
            predicates,
            table,
            ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryDefinitionIdField,
            selector.DefinitionId);
        AddEqual(
            predicates,
            table,
            ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryStatusField,
            selector.Status is { } status ? (int)status : null);
        AddEqual(
            predicates,
            table,
            ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryRunKindField,
            selector.RunKind is { } runKind ? (int)runKind : null);
        AddEqual(
            predicates,
            table,
            ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryCorrelationIdField,
            selector.CorrelationId);
        AddEqual(
            predicates,
            table,
            ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField,
            selector.WorkflowExecutionId);
        AddEqual(
            predicates,
            table,
            ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryArtifactIdField,
            selector.ArtifactId);
        var sortTicks = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistorySortTicksField);
        if (selector.From is { } from)
        {
            predicates.Add(new Predicate.Range(
                sortTicks,
                Bound.Inclusive(QueryConstant.Of(sortTicks, from.UtcTicks)),
                null));
        }

        if (selector.To is { } to)
        {
            predicates.Add(new Predicate.Range(
                sortTicks,
                null,
                Bound.Inclusive(QueryConstant.Of(sortTicks, to.UtcTicks))));
        }

        return predicates;
    }

    private QueryMaterializedResult QueryWithBoundCursor(
        QueryRequest request,
        string? cursor,
        string message)
    {
        try
        {
            return Open().Query(request);
        }
        catch (Exception exception) when (
            cursor is not null &&
            (exception is QueryRenderException { Code: "GW-QUERY-013" } ||
             exception is FormatException ||
             exception.InnerException is FormatException))
        {
            throw new ArgumentException(message, "cursor", exception);
        }
    }

    private static string RequiredProjectedString(
        IReadOnlyDictionary<string, object?> values,
        string field)
    {
        if (values.TryGetValue(field, out var value))
        {
            var text = value switch
            {
                string stringValue => stringValue,
                JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        throw new InvalidDataException(
            $"Groundwork workflow-execution retention row is missing projected field '{field}'.");
    }

    private void AddEqual(
        ICollection<Predicate> predicates,
        TableId table,
        string field,
        object? value)
    {
        if (value is null)
            return;
        var column = Column(table, field);
        predicates.Add(new Predicate.Equal(column, QueryConstant.Of(column, value)));
    }

    private static Predicate Combine(IReadOnlyList<Predicate> predicates) => predicates.Count switch
    {
        0 => Predicate.AlwaysTrue.Instance,
        1 => predicates[0],
        _ => new Predicate.And(predicates)
    };

    private ColumnRef Column(TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork workflow-execution unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork workflow-execution query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private static Paging PagingFor(int limit, string? continuationToken) =>
        continuationToken is null
            ? Paging.Keyset(limit)
            : Paging.Continuation(continuationToken, limit);
}
