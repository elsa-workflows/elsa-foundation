using System.Collections.Immutable;
using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork;

/// <summary>Public-v2 storage access for the workflow-design units.</summary>
public sealed class GroundworkDesignStorage(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    string? targetName = null,
    IGroundworkPrivilegedQueryAuditSink? auditSink = null)
{
    public const int ProviderPageSize = 256;
    public const int SearchTermMaximumMatches = 10_000;
    public const int SearchTermProbeLimit = SearchTermMaximumMatches + 1;
    public static readonly ScanAcceptance SearchTermProbeAcceptance = ScanAcceptance.Allow(
        "GW-SCAN-ELSA-WORKFLOWS-DESIGN-CATALOG-CARDINALITY",
        "The workflow-design SearchTerm catalog-cardinality probe is an explicitly bounded candidate read; it refuses above 10,000 candidates before any substring route enumeration.",
        "elsa-workflows-design",
        new DateTimeOffset(2027, 8, 16, 0, 0, 0, TimeSpan.Zero));
    private const QuerySearchKeyPolicy DefinitionIdSearchPolicy = QuerySearchKeyPolicy.UnicodeOrdinalIgnoreCase;
    private readonly GroundworkPrivilegedQueryAuditExecutor? privilegedQueryAuditExecutor =
        auditSink is null ? null : new GroundworkPrivilegedQueryAuditExecutor(sessions, accessContextAccessor, auditSink);

    public StorageUnit Unit(string unitId) => sessions.Unit(unitId, targetName);

    public bool AcrossScopes => (accessContextAccessor.Current ?? throw new InvalidOperationException(
        "Workflow-design persistence access context is missing.")).AcrossScopes;

    public static StorageKey Key(string id) => new(new Dictionary<string, object?>
    {
        [WorkflowsDesignStorageManifest.IdField] = id
    });

    public ColumnRef Column(string unitId, string name)
    {
        var unit = Unit(unitId);
        var table = new TableId(unit.Name);
        return Column(unit, table, name);
    }

    public Predicate Equal(string unitId, string field, object? value)
    {
        if (IsCaseInsensitiveField(unitId, field) && value is string text)
        {
            var searchColumn = SearchColumn(unitId, field);
            var policy = SearchPolicy(unitId, field);
            var lower = QueryConstant.Of(searchColumn, QuerySearchKeys.Encode(text, policy));
            var successor = QuerySearchKeys.Successor((string)lower.Value!, policy);
            return new Predicate.Range(
                searchColumn,
                Bound.Inclusive(lower),
                successor is null ? null : Bound.Exclusive(QueryConstant.Of(searchColumn, successor)));
        }

        var column = IsCaseInsensitiveField(unitId, field) ? SearchColumn(unitId, field) : Column(unitId, field);
        return new Predicate.Equal(column, QueryConstant.Of(column, value));
    }

    public Predicate In(string unitId, string field, IEnumerable<object?> values)
    {
        var valuesArray = values.ToArray();
        if (IsCaseInsensitiveField(unitId, field))
            return new Predicate.Or(valuesArray.Select(value => Equal(unitId, field, value)).ToArray());

        var column = Column(unitId, field);
        return new Predicate.In(column, valuesArray.Select(value => QueryConstant.Of(column, value)).ToImmutableArray());
    }

    public Predicate Contains(string unitId, string field, string value) =>
        IsCaseInsensitiveField(unitId, field)
            ? new Predicate.Substring(
                SearchColumn(unitId, field),
                QuerySearchKeys.Encode(value, SearchPolicy(unitId, field)),
                Anchor.Contains)
            : new Predicate.Substring(Column(unitId, field), value, Anchor.Contains);

    public OrderTerm Order(string unitId, string field, bool descending = false) =>
        new(Column(unitId, field), descending ? OrderDirection.Descending : OrderDirection.Ascending, NullOrder.Last);

    public GroundworkDesignEntry? Read(string unitId, string id, bool acrossScopes = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        RequireAcrossScopesIfRequested(acrossScopes);
        if (!acrossScopes)
        {
            var session = Open(unitId);
            try
            {
                var entry = session.Read(Key(id));
                return entry is null ? null : new GroundworkDesignEntry(entry, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new GroundworkProviderFailureException(
                    $"Provider point read for unit '{unitId}' failed.", exception);
            }
        }

        var result = ExecutePrivilegedQuery(
            unitId,
            "point-read-workflow-design-across-scopes",
            privileged =>
            {
                var unit = sessions.Unit(unitId, targetName);
                var table = new TableId(unit.Name);
                var idColumn = Column(unit, table, WorkflowsDesignStorageManifest.IdField);
                return privileged.QueryAcrossScopes(
                    new QueryRequest(
                        table,
                        new Predicate.Equal(idColumn, QueryConstant.Of(idColumn, id)),
                        [new OrderTerm(idColumn, OrderDirection.Ascending, NullOrder.Last)],
                        Projection.All,
                        Paging.Keyset(2)),
                    QueryOptions(unit, null));
            });
        if (result.Rows.Count == 0)
            return null;
        if (result.Rows.Count > 1)
            throw new GroundworkQueryReadinessException(
                $"Cross-scope point read for '{unitId}/{id}' is ambiguous across {result.Rows.Count} scopes.");
        var row = result.Rows[0];
        return new GroundworkDesignEntry(new StoredEntry(new StorageValues(row.Values), null), row.Scope);
    }

    public IReadOnlyList<GroundworkDesignEntry> Query(
        string unitId,
        Predicate predicate,
        IReadOnlyList<OrderTerm> order,
        string expectedIndex,
        bool acrossScopes = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedIndex);
        cancellationToken.ThrowIfCancellationRequested();
        RequireAcrossScopesIfRequested(acrossScopes);
        if (order.Count == 0)
            throw new GroundworkQueryReadinessException($"Named query '{expectedIndex}' requires a deterministic order.");

        var unit = sessions.Unit(unitId, targetName);
        var table = new TableId(unit.Name);
        var options = QueryOptions(unit, expectedIndex);
        if (acrossScopes)
            return ExecutePrivilegedQuery(
                unitId,
                "query-workflow-design-across-scopes",
                privileged => QueryAcrossScopesCore(unitId, unit, table, predicate, order, options, privileged, cancellationToken),
                cancellationToken);

        var session = Open(unitId);
        var rows = new List<GroundworkDesignEntry>();
        var seen = new HashSet<GroundworkDesignRowIdentity>();
        string? continuation = null;
        string? previousContinuation = null;
        var page = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paging = continuation is null
                ? Paging.Keyset(ProviderPageSize)
                : Paging.Continuation(continuation, ProviderPageSize);
            var request = new QueryRequest(table, predicate, order.ToImmutableArray(), Projection.All, paging);
            string? nextContinuation;
            try
            {
                var result = session.Query(request, options);
                if (result.Rows.Count == 0)
                {
                    if (result.NextContinuationToken is not null)
                        throw new GroundworkQueryReadinessException(
                            $"Provider query '{expectedIndex}' returned an empty page with a continuation token.");
                    break;
                }
                foreach (var row in result.Rows)
                {
                    var identity = Identity(row, null);
                    if (!seen.Add(identity))
                        throw new GroundworkQueryReadinessException(
                            $"Provider query '{expectedIndex}' returned a duplicate row identity '{identity}'.");
                    rows.Add(new GroundworkDesignEntry(new StoredEntry(new StorageValues(row), null), null));
                }
                nextContinuation = result.NextContinuationToken;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (GroundworkQueryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new GroundworkProviderFailureException(
                    $"Provider query '{expectedIndex}' for unit '{unitId}' failed.", exception);
            }

            previousContinuation = continuation;
            continuation = nextContinuation;
            if (continuation is null)
                break;
            if (StringComparer.Ordinal.Equals(previousContinuation, continuation))
                throw new GroundworkQueryReadinessException(
                    $"Provider query '{expectedIndex}' repeated its continuation token.");
            if (++page > 1_000_000)
                throw new GroundworkQueryReadinessException(
                    $"Provider query '{expectedIndex}' exceeded the continuation safety bound.");
        }

        return rows;
    }

    /// <summary>
    /// Executes one bounded provider probe for a public SearchTerm enumeration. It returns only
    /// the identity projection and never follows a continuation: a full page or a continuation
    /// proves that the caller-visible search cardinality exceeds the supported bound.
    /// </summary>
    public IReadOnlyList<GroundworkDesignEntry> Probe(
        string unitId,
        Predicate predicate,
        IReadOnlyList<OrderTerm> order,
        bool acrossScopes = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(order);
        cancellationToken.ThrowIfCancellationRequested();
        RequireAcrossScopesIfRequested(acrossScopes);
        var unit = sessions.Unit(unitId, targetName);
        var table = new TableId(unit.Name);
        var id = Column(unit, table, WorkflowsDesignStorageManifest.IdField);
        var request = new QueryRequest(
            table,
            predicate,
            order.ToImmutableArray(),
            Projection.ColumnsOnly(id),
            Paging.Keyset(SearchTermProbeLimit),
            acceptedScan: SearchTermProbeAcceptance);

        if (acrossScopes)
        {
            var result = ExecutePrivilegedQuery(
                unitId,
                "search-cardinality-workflow-design-across-scopes",
                privileged => privileged.QueryAcrossScopes(request, QueryOptions(unit, null)),
                cancellationToken);
            return ProbeRows(unitId, result.Rows.Select(row => (row.Values, (StorageScope?)row.Scope)), result.NextContinuationToken);
        }

        var session = Open(unitId);
        try
        {
            var result = session.Query(request, QueryOptions(unit, null));
            return ProbeRows(unitId, result.Rows.Select(row => (row, (StorageScope?)null)), result.NextContinuationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GroundworkQueryException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new GroundworkProviderFailureException(
                $"Provider SearchTerm cardinality probe for unit '{unitId}' failed.", exception);
        }
    }

    public bool Any(
        string unitId,
        Predicate predicate,
        string expectedIndex,
        bool acrossScopes = false,
        CancellationToken cancellationToken = default)
    {
        RequireAcrossScopesIfRequested(acrossScopes);
        var unit = sessions.Unit(unitId, targetName);
        var table = new TableId(unit.Name);
        var id = Column(unit, table, WorkflowsDesignStorageManifest.IdField);
        if (acrossScopes)
            return ExecutePrivilegedQuery(
                unitId,
                "any-workflow-design-across-scopes",
                privileged => privileged.QueryAcrossScopes(
                        new QueryRequest(table, predicate, [new OrderTerm(id, OrderDirection.Ascending, NullOrder.Last)], Projection.All, Paging.Keyset(1)),
                        QueryOptions(unit, expectedIndex)).Rows.Count > 0,
                cancellationToken);
        var session = Open(unitId);
        return session.Query(
                new QueryRequest(table, predicate, [new OrderTerm(id, OrderDirection.Ascending, NullOrder.Last)], Projection.All, Paging.Keyset(1)),
                QueryOptions(unit, expectedIndex)).Rows.Count > 0;
    }

    public WriteOutcome Insert(string unitId, StorageValues values, WriteOptions? options = null) =>
        Open(unitId).Insert(values, options);

    public WriteOutcome Upsert(string unitId, StorageValues values, WriteOptions? options = null) =>
        Open(unitId).Upsert(values, options);

    public WriteOutcome ConditionalUpsert(string unitId, StorageValues values, WriteOptions options) =>
        RequireConcurrency(Open(unitId), unitId).ConditionalUpsert(values, options);

    public WriteOutcome Delete(string unitId, string id, WriteOptions? options = null) =>
        Open(unitId).Delete(Key(id), options);

    public DesignUnitOfWork BeginUnitOfWork(IReadOnlyCollection<string> unitIds)
    {
        ArgumentNullException.ThrowIfNull(unitIds);
        if (unitIds.Count == 0)
            throw new ArgumentException("At least one workflow-design unit is required.", nameof(unitIds));
        var current = accessContextAccessor.Current ?? throw new InvalidOperationException(
            "Workflow-design persistence access context is missing.");
        var distinct = unitIds.Distinct(StringComparer.Ordinal).ToArray();
        var units = distinct.Select(unitId => sessions.Unit(unitId, targetName)).ToArray();
        var accesses = units
            .Select(unit => GroundworkStorageAccessMapper.Map(current, unit.Scope, "elsa-workflows-design"))
            .Distinct()
            .ToArray();
        if (accesses.Length != 1)
            throw new InvalidOperationException("A workflow-design unit of work must use one exact persistence access context.");
        return new DesignUnitOfWork(
            sessions.BeginUnitOfWork(accesses[0], BatchWriteOptions.Exact, distinct, targetName),
            units.ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal));
    }

    public IStorageSession Open(string unitId, bool acrossScopes = false)
    {
        if (acrossScopes)
            throw new InvalidOperationException(
                "Privileged workflow-design queries must use the audited public cross-scope executor.");
        var unit = sessions.Unit(unitId, targetName);
        var current = accessContextAccessor.Current ?? throw new InvalidOperationException(
            "Workflow-design persistence access context is missing.");
        var access = GroundworkStorageAccessMapper.Map(current, unit.Scope, "elsa-workflows-design");
        try
        {
            return sessions.Open(unitId, access, targetName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GroundworkQueryException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new GroundworkProviderFailureException(
                $"Provider session open for unit '{unitId}' failed.", exception);
        }
    }

    public static StorageValues Values<TEntity>(
        string unitId,
        TEntity entity,
        JsonSerializerOptions jsonOptions,
        string collection,
        IReadOnlyCollection<DesignMetadataRecord>? layout = null,
        IReadOnlyCollection<ActivityPresentationRecord>? activityPresentation = null)
        where TEntity : Entity
    {
        ArgumentNullException.ThrowIfNull(entity);
        var document = new GroundworkDesignDocument<TEntity>(collection, entity, layout, activityPresentation);
        var content = JsonSerializer.SerializeToElement(document, jsonOptions);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [WorkflowsDesignStorageManifest.IdField] = entity.Id,
            [WorkflowsDesignStorageManifest.SchemaVersionField] = WorkflowsDesignStorageManifest.SchemaVersion,
            [WorkflowsDesignStorageManifest.ContentField] = content,
            [WorkflowsDesignStorageManifest.TenantIdField] = (entity as TenantEntity)?.TenantId,
            ["createdAt"] = entity.CreatedAt,
            ["lastModifiedAt"] = entity.LastModifiedAt
        };

        switch (entity)
        {
            case WorkflowDefinition definition:
                values[WorkflowsDesignStorageManifest.DefinitionIdField] = definition.Id;
                values[WorkflowsDesignStorageManifest.DefinitionIdSearchKeyField] =
                    QuerySearchKeys.Encode(definition.Id, DefinitionIdSearchPolicy);
                values[WorkflowsDesignStorageManifest.DefinitionNameField] = definition.Name;
                values[WorkflowsDesignStorageManifest.DefinitionDescriptionField] = definition.Description;
                values[WorkflowsDesignStorageManifest.DefinitionDeletedAtField] = definition.DeletedAt;
                values[WorkflowsDesignStorageManifest.DefinitionDeletedReasonField] = definition.DeletedReason;
                values[WorkflowsDesignStorageManifest.DefinitionIsSourceOwnedField] = definition.IsSourceOwned;
                break;
            case WorkflowDefinitionVersion version:
                values[WorkflowsDesignStorageManifest.VersionIdField] = version.Id;
                values[WorkflowsDesignStorageManifest.VersionDefinitionIdField] = version.DefinitionId;
                values[WorkflowsDesignStorageManifest.VersionField] = version.Version;
                values[WorkflowsDesignStorageManifest.VersionSemVerSortKeyField] = version.SemVerSortKey;
                values[WorkflowsDesignStorageManifest.VersionSourceDraftField] = version.SourceDraftId;
                break;
            case WorkflowDefinitionDraft draft:
                values[WorkflowsDesignStorageManifest.DraftIdField] = draft.Id;
                values[WorkflowsDesignStorageManifest.DraftDefinitionIdField] = draft.WorkflowDefinitionId;
                values[WorkflowsDesignStorageManifest.DraftSourceVersionField] = draft.SourceVersionId;
                values[WorkflowsDesignStorageManifest.DraftLastModifiedAtField] = draft.LastModifiedAt;
                values[WorkflowsDesignStorageManifest.DraftCreatedAtField] = draft.CreatedAt;
                break;
            case WorkflowDefinitionVersionLayout layoutEntity:
                values[WorkflowsDesignStorageManifest.LayoutVersionIdField] = layoutEntity.WorkflowDefinitionVersionId;
                break;
            default:
                throw new ArgumentException($"Workflow-design entity type '{typeof(TEntity).Name}' is not declared.", nameof(entity));
        }

        return new StorageValues(values);
    }

    public WorkflowDefinition MapDefinition(GroundworkDesignEntry entry) =>
        Deserialize<WorkflowDefinition>(entry.Entry, GroundworkDesignJson.Options);

    public WorkflowDefinitionVersion MapVersion(GroundworkDesignEntry entry, JsonSerializerOptions jsonOptions) =>
        Deserialize<WorkflowDefinitionVersion>(entry.Entry, jsonOptions);

    public WorkflowDefinitionDraft MapDraft(GroundworkDesignEntry entry, JsonSerializerOptions jsonOptions) =>
        Deserialize<WorkflowDefinitionDraft>(entry.Entry, jsonOptions);

    public (WorkflowDefinitionDraft Draft, IReadOnlyCollection<DesignMetadataRecord> Layout,
        IReadOnlyCollection<ActivityPresentationRecord> Presentation) MapDraftFull(
        GroundworkDesignEntry entry,
        JsonSerializerOptions jsonOptions)
    {
        var document = DeserializeDocument<WorkflowDefinitionDraft>(entry.Entry, jsonOptions);
        return (document.Entity, document.Layout ?? [], document.ActivityPresentation ?? []);
    }

    public WorkflowDefinitionVersionLayout MapLayout(GroundworkDesignEntry entry, JsonSerializerOptions jsonOptions) =>
        Deserialize<WorkflowDefinitionVersionLayout>(entry.Entry, jsonOptions);

    public long? Version(GroundworkDesignEntry? entry) => entry?.Entry.Version;

    public static TEntity Deserialize<TEntity>(StoredEntry entry, JsonSerializerOptions jsonOptions)
        where TEntity : Entity
        => DeserializeDocument<TEntity>(entry, jsonOptions).Entity;

    public static GroundworkDesignDocument<TEntity> DeserializeDocument<TEntity>(StoredEntry entry, JsonSerializerOptions jsonOptions)
        where TEntity : Entity
    {
        try
        {
            var content = Required(entry.Values.Values, WorkflowsDesignStorageManifest.ContentField);
            var document = content switch
            {
                JsonElement element => element.Deserialize<GroundworkDesignDocument<TEntity>>(jsonOptions),
                JsonDocument json => json.Deserialize<GroundworkDesignDocument<TEntity>>(jsonOptions),
                string text => JsonSerializer.Deserialize<GroundworkDesignDocument<TEntity>>(text, jsonOptions),
                _ => null
            };
            return document ?? throw new InvalidDataException("Groundwork workflow-design row has no entity payload.");
        }
        catch (DesignPersistenceException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidDataException)
        {
            throw new GroundworkCorruptPayloadException("Groundwork workflow-design row could not be deserialized.", exception);
        }
    }

    public static GroundworkDesignRowIdentity Identity(GroundworkDesignEntry entry) =>
        Identity(entry.Entry.Values.Values, entry.Scope);

    private static GroundworkDesignRowIdentity Identity(IReadOnlyDictionary<string, object?> values, StorageScope? scope)
    {
        var id = Convert.ToString(values.GetValueOrDefault(WorkflowsDesignStorageManifest.IdField), System.Globalization.CultureInfo.InvariantCulture) ?? "";
        return new GroundworkDesignRowIdentity(scope?.Value, id);
    }

    private void RequireAcrossScopesIfRequested(bool acrossScopes)
    {
        if (!acrossScopes)
            return;
        var context = accessContextAccessor.Current ?? throw new InvalidOperationException(
            "Workflow-design persistence access context is missing.");
        if (!context.AcrossScopes || context.AccessPolicy != PersistenceAccessPolicy.Privileged || context.Purpose is null)
            throw new InvalidOperationException(
                "Tenant-agnostic workflow-design queries require explicit privileged across-scope access.");
    }

    private TResult ExecutePrivilegedQuery<TResult>(
        string unitId,
        string auditIdentity,
        Func<IPrivilegedCrossScopeQuerySession, TResult> operation,
        CancellationToken cancellationToken = default)
    {
        if (privilegedQueryAuditExecutor is null)
            throw new InvalidOperationException(
                "Privileged workflow-design queries require the registered public cross-scope audit executor.");

        try
        {
            return privilegedQueryAuditExecutor.Execute(
                unitId,
                auditIdentity,
                operation,
                cancellationToken,
                targetName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GroundworkQueryException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new GroundworkProviderFailureException(
                $"Provider cross-scope query for unit '{unitId}' failed.", exception);
        }
    }

    private IReadOnlyList<GroundworkDesignEntry> QueryAcrossScopesCore(
        string unitId,
        StorageUnit unit,
        TableId table,
        Predicate predicate,
        IReadOnlyList<OrderTerm> order,
        QueryRenderOptions options,
        IPrivilegedCrossScopeQuerySession session,
        CancellationToken cancellationToken)
    {
        var rows = new List<GroundworkDesignEntry>();
        var seen = new HashSet<GroundworkDesignRowIdentity>();
        string? continuation = null;
        string? previousContinuation = null;
        var page = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paging = continuation is null
                ? Paging.Keyset(ProviderPageSize)
                : Paging.Continuation(continuation, ProviderPageSize);
            var request = new QueryRequest(table, predicate, order.ToImmutableArray(), Projection.All, paging);
            var result = session.QueryAcrossScopes(request, options);
            if (result.Rows.Count == 0)
            {
                if (result.NextContinuationToken is not null)
                    throw new GroundworkQueryReadinessException(
                        $"Provider query '{options.SelectedIndex}' returned an empty page with a continuation token.");
                break;
            }

            foreach (var row in result.Rows)
            {
                var identity = Identity(row.Values, row.Scope);
                if (!seen.Add(identity))
                    throw new GroundworkQueryReadinessException(
                        $"Provider query '{options.SelectedIndex}' returned a duplicate row identity '{identity}'.");
                rows.Add(new GroundworkDesignEntry(new StoredEntry(new StorageValues(row.Values), null), row.Scope));
            }

            previousContinuation = continuation;
            continuation = result.NextContinuationToken;
            if (continuation is null)
                break;
            if (StringComparer.Ordinal.Equals(previousContinuation, continuation))
                throw new GroundworkQueryReadinessException(
                    $"Provider query '{options.SelectedIndex}' repeated its continuation token.");
            if (++page > 1_000_000)
                throw new GroundworkQueryReadinessException(
                    $"Provider query '{options.SelectedIndex}' exceeded the continuation safety bound.");
        }

        return rows;
    }

    private IReadOnlyList<GroundworkDesignEntry> ProbeRows<TValues>(
        string unitId,
        IEnumerable<(TValues Values, StorageScope? Scope)> source,
        string? continuation)
        where TValues : IReadOnlyDictionary<string, object?>
    {
        var rows = source
            .Select(row => new GroundworkDesignEntry(new StoredEntry(new StorageValues(row.Values), null), row.Scope))
            .ToArray();
        if (rows.Length == SearchTermProbeLimit || continuation is not null)
            throw new GroundworkQueryReadinessException(
                $"SearchTerm results for unit '{unitId}' exceed the supported {SearchTermMaximumMatches.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} row bound.");
        var seen = new HashSet<GroundworkDesignRowIdentity>();
        foreach (var row in rows)
        {
            if (!seen.Add(Identity(row)))
                throw new GroundworkQueryReadinessException(
                    $"SearchTerm cardinality probe for unit '{unitId}' returned a duplicate row identity.");
        }
        return rows;
    }

    private static object? Required(IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) ? value : throw new InvalidDataException($"Groundwork row is missing '{key}'.");

    private static IConcurrencyStorageSession RequireConcurrency(IStorageSession session, string unitId) =>
        session as IConcurrencyStorageSession ?? throw new NotSupportedException(
            $"Workflow-design unit '{unitId}' requires public Groundwork optimistic concurrency.");

    private static QueryRenderOptions QueryOptions(StorageUnit unit, string? expectedIndex)
    {
        if (expectedIndex is null)
            return QueryRenderOptions.Default;
        var index = unit.Indexes.SingleOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Name, expectedIndex))
                    ?? throw new GroundworkQueryReadinessException(
                        $"Query route '{expectedIndex}' has no declared index on unit '{unit.Id.Value}'.");
        return new QueryRenderOptions(
            [new QueryIndexDeclaration(index.Name, index.Columns.Select(column => column.Column))],
            selectedIndex: index.Name);
    }

    private static ColumnRef Column(StorageUnit unit, TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column => StringComparer.Ordinal.Equals(column.Name, name))
                         ?? throw new GroundworkQueryReadinessException(
                             $"Unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Guid => QueryType.Guid,
            _ => throw new GroundworkQueryReadinessException(
                $"Unit '{unit.Id.Value}' query column '{name}' uses unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(
            table,
            name,
            type,
            definition.IsNullable,
            definition.MaxLength,
            stringComparison: QueryStringComparisonPolicy.Ordinal);
    }

    private static bool IsCaseInsensitiveField(string unitId, string name) =>
        StringComparer.Ordinal.Equals(unitId, WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind) &&
        (IsDefinitionIdField(name) ||
         StringComparer.Ordinal.Equals(name, WorkflowsDesignStorageManifest.DefinitionNameField) ||
         StringComparer.Ordinal.Equals(name, WorkflowsDesignStorageManifest.DefinitionDescriptionField));

    private static bool IsDefinitionIdField(string name) =>
        StringComparer.Ordinal.Equals(name, WorkflowsDesignStorageManifest.IdField) ||
        StringComparer.Ordinal.Equals(name, WorkflowsDesignStorageManifest.DefinitionIdField);

    private ColumnRef SearchColumn(string unitId, string field)
    {
        var unit = Unit(unitId);
        var table = new TableId(unit.Name);
        if (IsDefinitionIdField(field))
            return Column(unit, table, WorkflowsDesignStorageManifest.DefinitionIdSearchKeyField);

        var source = unit.Columns.Single(column => StringComparer.Ordinal.Equals(column.Name, field));
        return new ColumnRef(
            table,
            SearchKeyProjection.ColumnName(field),
            QueryType.String,
            isNullable: source.IsNullable,
            maxLength: source.MaxLength is int maxLength ? maxLength * 7 : null,
            stringComparison: QueryStringComparisonPolicy.Ordinal);
    }

    private static QuerySearchKeyPolicy SearchPolicy(string unitId, string field) =>
        IsDefinitionIdField(field)
            ? DefinitionIdSearchPolicy
            : QuerySearchKeyPolicy.UnicodeOrdinalIgnoreCase;

    public sealed class DesignUnitOfWork(IUnitOfWork inner, IReadOnlyDictionary<string, StorageUnit> units) : IDisposable
    {
        public void Stage(string unitId, StorageValues values, WriteOptions options) =>
            inner.Stage(RowWrite.ConditionalUpsert(Require(unitId), values, options));

        public void StageDelete(string unitId, string id, WriteOptions options) =>
            inner.Stage(RowWrite.Delete(Require(unitId), Key(id), options));

        public BatchWriteReport Commit() => inner.CommitWithOutcomes();

        public void Rollback() => inner.Rollback();

        public void Dispose() => inner.Dispose();

        private StorageUnit Require(string unitId) => units.TryGetValue(unitId, out var unit)
            ? unit
            : throw new InvalidOperationException($"Unit '{unitId}' was not admitted to this unit of work.");
    }
}

public sealed record GroundworkDesignEntry(StoredEntry Entry, StorageScope? Scope);

public readonly record struct GroundworkDesignRowIdentity(string? Scope, string Id);

public sealed record GroundworkDesignDocument<TEntity>(
    string Collection,
    TEntity Entity,
    IReadOnlyCollection<DesignMetadataRecord>? Layout = null,
    IReadOnlyCollection<ActivityPresentationRecord>? ActivityPresentation = null)
    where TEntity : Entity;

public class GroundworkQueryException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

public sealed class GroundworkQueryReadinessException(string message)
    : GroundworkQueryException(message);

public sealed class GroundworkQueryTranslationException(string message)
    : GroundworkQueryException(message);

public sealed class GroundworkProviderFailureException(string message, Exception innerException)
    : GroundworkQueryException(message, innerException);

public sealed class GroundworkCorruptPayloadException(string message, Exception innerException)
    : GroundworkQueryException(message, innerException);
