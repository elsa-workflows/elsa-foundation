using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 bookmark state and stimulus index.</summary>
/// <remarks>
/// Bookmark rows use an injective length-prefixed (workflow execution ID, bookmark ID) physical identity,
/// while the logical bookmark ID and workflow identity remain independently projected fields. This preserves
/// the V1 contract that equal bookmark IDs may coexist in different workflow executions and keeps cleanup and
/// checkpoint state changes on the same row key. A concurrent save is reported as a deterministic retryable
/// failure; it never falls back to an unconditional write.
/// </remarks>
public sealed class GroundworkV2BookmarkStateStore : IBookmarkStateStore, IBookmarkStimulusIndex
{
    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit unit;

    public GroundworkV2BookmarkStateStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        unit = sessions.Unit(ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind, targetName);
    }

    public ValueTask<BookmarkState> SaveAsync(
        BookmarkState state,
        CancellationToken cancellationToken = default)
    {
        ValidateState(state);
        cancellationToken.ThrowIfCancellationRequested();

        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2BookmarkStorageConventions.PhysicalId(state.WorkflowExecutionId, state.BookmarkId));
        var values = Values(state);
        var result = session.Read(key) is { } existing
            ? UpdateExisting(session, values, existing, state)
            : session.Insert(values, WriteOptions.CreateOnly);
        if (!IsSaved(result.Status))
        {
            throw new InvalidOperationException(
                "Groundwork bookmark-state save lost a concurrent write; retry the operation.");
        }

        return ValueTask.FromResult(state);
    }

    public ValueTask<bool> DeleteAsync(
        string workflowExecutionId,
        string bookmarkId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, bookmarkId);
        cancellationToken.ThrowIfCancellationRequested();

        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2BookmarkStorageConventions.PhysicalId(workflowExecutionId, bookmarkId));
        if (session.Read(key) is not { } existing)
            return ValueTask.FromResult(false);

        var state = Deserialize(existing.Values.Values);
        if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(state.BookmarkId, bookmarkId))
            throw new InvalidDataException("Groundwork bookmark row identity does not match its requested key.");

        var revision = existing.Version ??
                       throw new InvalidDataException("Groundwork bookmark row did not return an optimistic revision.");
        var result = session.Delete(key, WriteOptions.IfVersion(revision));
        if (result.Status is not (WriteOutcomeStatus.Deleted or WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.NotFound))
        {
            throw new InvalidOperationException("Groundwork bookmark-state delete failed; retry the operation.");
        }

        return ValueTask.FromResult(result.Status == WriteOutcomeStatus.Deleted);
    }

    public ValueTask<BookmarkState?> FindAsync(
        string workflowExecutionId,
        string bookmarkId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, bookmarkId);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = Open().Read(
            GroundworkRuntimeRowStore.Key(
                GroundworkV2BookmarkStorageConventions.PhysicalId(workflowExecutionId, bookmarkId)));
        if (entry is null)
            return ValueTask.FromResult<BookmarkState?>(null);

        var state = Deserialize(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(state.BookmarkId, bookmarkId))
            throw new InvalidDataException("Groundwork bookmark row identity does not match its requested key.");

        return ValueTask.FromResult<BookmarkState?>(state);
    }

    public ValueTask<RuntimeStorePage<BookmarkState>> ListPageAsync(
        BookmarkStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var table = new TableId(unit.Name);
        var workflow = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var bookmark = Column(table, ElsaRuntimeV2StorageManifest.BookmarkIdField);
        var request = new QueryRequest(
            table,
            Equal(workflow, query.WorkflowExecutionId),
            [new OrderTerm(bookmark, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            PagingFor(query.Limit, query.ContinuationToken));
        return ValueTask.FromResult(ReadPage(query, Open().Query(request)));
    }

    public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusPageAsync(
        BookmarkStimulusPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(ReadStimulusPage(
            query,
            ElsaRuntimeV2StorageManifest.StimulusLookupKeyField,
            GroundworkV2BookmarkStorageConventions.StimulusLookupKey(query.StimulusType, query.StimulusHash),
            Open()));
    }

    public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusTypePageAsync(
        BookmarkStimulusTypePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(ReadStimulusPage(
            query,
            ElsaRuntimeV2StorageManifest.StimulusTypeLookupKeyField,
            GroundworkV2BookmarkStorageConventions.StimulusTypeLookupKey(query.StimulusType),
            Open()));
    }

    private RuntimeStorePage<BookmarkState> ReadStimulusPage(
        RuntimeStorePageRequest query,
        string lookupField,
        string lookupValue,
        IStorageSession session)
    {
        var table = new TableId(unit.Name);
        var lookup = Column(table, lookupField);
        var workflow = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var bookmark = Column(table, ElsaRuntimeV2StorageManifest.BookmarkIdField);
        var request = new QueryRequest(
            table,
            Equal(lookup, lookupValue),
            [
                new OrderTerm(workflow, OrderDirection.Ascending, NullOrder.Last),
                new OrderTerm(bookmark, OrderDirection.Ascending, NullOrder.Last)
            ],
            Projection.All,
            PagingFor(query.Limit, query.ContinuationToken));
        return ReadPage(query, session.Query(request));
    }

    private static RuntimeStorePage<BookmarkState> ReadPage(
        RuntimeStorePageRequest query,
        QueryMaterializedResult result) =>
        new(
            query,
            result.Rows.Select(Deserialize).ToArray(),
            result.NextContinuationToken);

    private IStorageSession Open()
    {
        var context = accessContextAccessor.Current;
        if (context.Scope is null || context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Groundwork bookmark state requires one explicit persistence scope; global and across-scope access are refused.");
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
        BookmarkState state)
    {
        var previous = Deserialize(existing.Values.Values);
        if (!StringComparer.Ordinal.Equals(previous.WorkflowExecutionId, state.WorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(previous.BookmarkId, state.BookmarkId))
        {
            throw new InvalidDataException("Groundwork bookmark row identity does not match its current content.");
        }

        var revision = existing.Version ??
                       throw new InvalidDataException("Groundwork bookmark row did not return an optimistic revision.");
        if (session is not IConcurrencyStorageSession concurrency)
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic bookmark concurrency.");

        return concurrency.ConditionalUpsert(values, WriteOptions.IfVersion(revision));
    }

    private static StorageValues Values(BookmarkState state) =>
        GroundworkV2BookmarkStorageConventions.Values(state);

    private static BookmarkState Deserialize(IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Groundwork bookmark row returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{ElsaRuntimeV2StorageManifest.SchemaVersion}'.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork bookmark row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork bookmark row did not contain JSON content.");

        BookmarkState state;
        try
        {
            state = GroundworkV2BookmarkStorageConventions.Deserialize(content);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Groundwork bookmark row content was not valid current JSON.", exception);
        }

        ValidateState(state);
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.IdField,
            GroundworkV2BookmarkStorageConventions.PhysicalId(state.WorkflowExecutionId, state.BookmarkId));
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.BookmarkIdField, state.BookmarkId);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, state.WorkflowExecutionId);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.StimulusHashField, state.StimulusHash);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.StimulusTypeField, state.StimulusType);
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.StimulusLookupKeyField,
            GroundworkV2BookmarkStorageConventions.StimulusLookupKey(state.StimulusType, state.StimulusHash));
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.StimulusTypeLookupKeyField,
            GroundworkV2BookmarkStorageConventions.StimulusTypeLookupKey(state.StimulusType));
        return state;
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        string expected)
    {
        var actual = RequiredString(values, field);
        if (!StringComparer.Ordinal.Equals(actual, expected))
            throw new InvalidDataException($"Groundwork bookmark row projection '{field}' does not match its current content.");
    }

    private static string RequiredString(IReadOnlyDictionary<string, object?> values, string field)
    {
        if (values.TryGetValue(field, out var value))
        {
            if (value is string text)
                return text;
            if (value is JsonElement { ValueKind: JsonValueKind.String } element)
            {
                return element.GetString() ?? string.Empty;
            }
        }

        throw new InvalidDataException($"Groundwork bookmark row is missing required string field '{field}'.");
    }

    private static bool IsSaved(WriteOutcomeStatus status) =>
        status is WriteOutcomeStatus.Inserted or WriteOutcomeStatus.Updated or WriteOutcomeStatus.Upserted or WriteOutcomeStatus.Replayed;

    private static void ValidateState(BookmarkState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateIdentity(state.WorkflowExecutionId, state.BookmarkId);
        _ = GroundworkV2BookmarkStorageConventions.PhysicalId(state.WorkflowExecutionId, state.BookmarkId);
    }

    private static void ValidateIdentity(string workflowExecutionId, string bookmarkId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId);
    }

    private static Predicate Equal(ColumnRef column, string value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    private ColumnRef Column(TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork bookmark unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork bookmark query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private static Paging PagingFor(int limit, string? continuationToken) =>
        continuationToken is null
            ? Paging.Keyset(limit)
            : Paging.Continuation(continuationToken, limit);

}
