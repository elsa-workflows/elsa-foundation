using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Workflows.Dashboard.Persistence.Groundwork.V2;

/// <summary>
/// Serves the workflow-portfolio tile from the Groundwork v2 lanes.
/// </summary>
/// <remarks>
/// <para>
/// The v1 source read one shared document table, so it could express the tile as a single statement with
/// CTEs and a join. Every v2 unit owns its own physical table and the query model has no joins, so the tile
/// is three ordered walks — definitions and drafts on the design lane, live published source references on
/// the runtime lane — correlated here on definition id. That is the split-target path the v1 source only
/// took when a host separated the lanes; in v2 it is the only path, and it is also what makes a split host
/// work without any extra wiring: each unit resolves through its own target.
/// </para>
/// <para>
/// Every projected fact the tile needs is a first-class column, so nothing is deserialized to obtain a
/// count. Only <see cref="StreamCurrentDraftsAsync"/> materializes payloads, and only for the drafts it
/// actually yields.
/// </para>
/// </remarks>
public sealed class GroundworkV2WorkflowPortfolioDataSource : IWorkflowPortfolioDataSource
{
    /// <summary>
    /// Upper bound on live published source references read from the runtime lane. The set is at most one
    /// row per published definition version, so this is the catalog's order of magnitude rather than the
    /// execution history's. Exceeding it is reported rather than silently truncating a count.
    /// </summary>
    public const int PublishedReferenceLimit = 100_000;

    private const int PageSize = 256;

    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly JsonSerializerOptions jsonOptions;
    private readonly string? designTargetName;
    private readonly string? runtimeTargetName;

    public GroundworkV2WorkflowPortfolioDataSource(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        IPayloadSerializer payloadSerializer,
        string? designTargetName = null,
        string? runtimeTargetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        ArgumentNullException.ThrowIfNull(payloadSerializer);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.designTargetName = designTargetName;
        this.runtimeTargetName = runtimeTargetName;
        jsonOptions = GroundworkDesignDocumentSerialization.Create(payloadSerializer);
    }

    public bool IsAvailable => true;

    public ValueTask<WorkflowPortfolioBaseCounts> QueryBaseCountsAsync(
        string tenantId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        var access = RequireScopedAccess(tenantId);
        var activeDefinitionIds = ActiveDefinitionIds(access, cancellationToken);
        if (activeDefinitionIds.Count == 0)
        {
            // No definitions means no publications and no current drafts, so neither lane is worth a query.
            return ValueTask.FromResult(new WorkflowPortfolioBaseCounts(0, 0, 0));
        }

        var publishedCount = PublishedDefinitionIds(access, asOf, cancellationToken)
            .Count(activeDefinitionIds.Contains);
        var draftCount = CurrentDraftDefinitionIds(access, activeDefinitionIds, cancellationToken).Count;
        return ValueTask.FromResult(new WorkflowPortfolioBaseCounts(
            activeDefinitionIds.Count,
            publishedCount,
            draftCount));
    }

    public async IAsyncEnumerable<WorkflowDefinitionDraft> StreamCurrentDraftsAsync(
        string tenantId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The v2 provider surface is synchronous; the yields below are this iterator's only suspension points.
        await Task.CompletedTask;
        var access = RequireScopedAccess(tenantId);
        var activeDefinitionIds = ActiveDefinitionIds(access, cancellationToken);
        if (activeDefinitionIds.Count == 0)
            yield break;

        foreach (var row in CurrentDraftRows(access, activeDefinitionIds, cancellationToken))
        {
            var document = GroundworkDesignStorage.DeserializeDocument<WorkflowDefinitionDraft>(
                new StoredEntry(new StorageValues(row), null),
                jsonOptions);
            yield return document.Entity;
        }
    }

    /// <summary>
    /// The tenant's definitions that have not been soft-deleted. Both facts are projected columns, so the
    /// walk never touches a payload. Ordering by definition id is the design lane's list route.
    /// </summary>
    private HashSet<string> ActiveDefinitionIds(StorageAccess access, CancellationToken cancellationToken)
    {
        var unit = Unit(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, designTargetName);
        var table = new TableId(unit.Name);
        var definitionId = Column(unit, table, WorkflowsDesignStorageManifest.DefinitionIdField);
        var deletedAt = Column(unit, table, WorkflowsDesignStorageManifest.DefinitionDeletedAtField);
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Walk(
                     unit,
                     designTargetName,
                     access,
                     Predicate.AlwaysTrue.Instance,
                     [Ascending(definitionId)],
                     Projection.ColumnsOnly(definitionId, deletedAt),
                     limit: null,
                     "workflow-definition",
                     cancellationToken))
        {
            if (row.TryGetValue(WorkflowsDesignStorageManifest.DefinitionDeletedAtField, out var deleted) &&
                deleted is not null)
            {
                continue;
            }

            active.Add(RequiredString(row, WorkflowsDesignStorageManifest.DefinitionIdField, "workflow-definition"));
        }

        return active;
    }

    /// <summary>
    /// The definitions with a source reference that is published, not retired, and not yet expired at
    /// <paramref name="asOf"/>. A missing expiry is stored as <see cref="DateTimeOffset.MaxValue"/> rather
    /// than null, so "still live" is one range predicate instead of a null test.
    /// </summary>
    private HashSet<string> PublishedDefinitionIds(
        StorageAccess access,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var unit = Unit(
            ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
            runtimeTargetName);
        var table = new TableId(unit.Name);
        var scope = Column(unit, table, ElsaRuntimeV2StorageManifest.ScopeField);
        var retired = Column(unit, table, ElsaRuntimeV2StorageManifest.IsRetiredField);
        var expiresAt = Column(unit, table, ElsaRuntimeV2StorageManifest.ExpiresAtField);
        var definitionId = Column(
            unit,
            table,
            ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDefinitionIdField);
        var sourceReferenceId = Column(
            unit,
            table,
            ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceIdField);
        var predicate = new Predicate.And([
            new Predicate.Equal(
                scope,
                QueryConstant.Of(scope, WorkflowExecutableReferenceScope.Published.ToString())),
            new Predicate.Equal(retired, QueryConstant.Of(retired, false)),
            new Predicate.Range(expiresAt, Bound.Exclusive(QueryConstant.Of(expiresAt, asOf)), null)
        ]);

        var published = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Walk(
                     unit,
                     runtimeTargetName,
                     access,
                     predicate,
                     [Ascending(sourceReferenceId)],
                     Projection.ColumnsOnly(sourceReferenceId, definitionId),
                     PublishedReferenceLimit,
                     "workflow-executable-source-reference",
                     cancellationToken))
        {
            published.Add(RequiredString(
                row,
                ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDefinitionIdField,
                "workflow-executable-source-reference"));
        }

        return published;
    }

    private HashSet<string> CurrentDraftDefinitionIds(
        StorageAccess access,
        IReadOnlySet<string> activeDefinitionIds,
        CancellationToken cancellationToken)
    {
        var definitions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in CurrentDraftRows(access, activeDefinitionIds, cancellationToken))
        {
            definitions.Add(RequiredString(
                row,
                WorkflowsDesignStorageManifest.DraftDefinitionIdField,
                "workflow-definition-draft"));
        }

        return definitions;
    }

    /// <summary>
    /// One row per active definition that has a draft: its current one.
    /// </summary>
    /// <remarks>
    /// The draft unit's by-definition index is declared in exactly the order that makes the current draft
    /// first — definition id ascending, then last-modified, created and draft id descending — so walking it
    /// and keeping the first row per definition is the same selection the design lane's own current-draft
    /// read performs, without a window function the query model does not have.
    /// </remarks>
    private IEnumerable<IReadOnlyDictionary<string, object?>> CurrentDraftRows(
        StorageAccess access,
        IReadOnlySet<string> activeDefinitionIds,
        CancellationToken cancellationToken)
    {
        var unit = Unit(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, designTargetName);
        var table = new TableId(unit.Name);
        var definitionId = Column(unit, table, WorkflowsDesignStorageManifest.DraftDefinitionIdField);
        var lastModifiedAt = Column(unit, table, WorkflowsDesignStorageManifest.DraftLastModifiedAtField);
        var createdAt = Column(unit, table, WorkflowsDesignStorageManifest.DraftCreatedAtField);
        var draftId = Column(unit, table, WorkflowsDesignStorageManifest.DraftIdField);
        string? previousDefinitionId = null;
        foreach (var row in Walk(
                     unit,
                     designTargetName,
                     access,
                     Predicate.AlwaysTrue.Instance,
                     [
                         Ascending(definitionId),
                         Descending(lastModifiedAt),
                         Descending(createdAt),
                         Descending(draftId)
                     ],
                     Projection.All,
                     limit: null,
                     "workflow-definition-draft",
                     cancellationToken))
        {
            var current = RequiredString(
                row,
                WorkflowsDesignStorageManifest.DraftDefinitionIdField,
                "workflow-definition-draft");
            if (StringComparer.Ordinal.Equals(previousDefinitionId, current))
                continue;

            previousDefinitionId = current;
            if (activeDefinitionIds.Contains(current))
                yield return row;
        }
    }

    /// <summary>Pages one ordered query to exhaustion, refusing a provider that cycles its continuation.</summary>
    private IEnumerable<IReadOnlyDictionary<string, object?>> Walk(
        StorageUnit unit,
        string? targetName,
        StorageAccess access,
        Predicate predicate,
        IReadOnlyList<OrderTerm> order,
        Projection projection,
        int? limit,
        string subject,
        CancellationToken cancellationToken)
    {
        var session = sessions.Open(unit.Id.Value, access, targetName);
        var table = new TableId(unit.Name);
        var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
        var seen = 0;
        string? cursor = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = session.Query(new QueryRequest(
                table,
                predicate,
                [.. order],
                projection,
                cursor is null ? Paging.Keyset(PageSize) : Paging.Continuation(cursor, PageSize)));
            foreach (var row in result.Rows)
            {
                if (limit is { } maximum && ++seen > maximum)
                {
                    throw new InvalidOperationException(
                        $"The workflow portfolio read more than {maximum} '{subject}' rows from Groundwork. " +
                        "Correlating the lanes in memory is only viable at catalog scale.");
                }

                yield return row;
            }

            if (result.NextContinuationToken is { } next && !seenContinuations.Add(next))
            {
                throw new InvalidDataException(
                    $"Groundwork '{subject}' portfolio continuation repeated or cycled.");
            }

            cursor = result.NextContinuationToken;
        } while (cursor is not null);
    }

    private StorageUnit Unit(string unitId, string? targetName) => sessions.Unit(unitId, targetName);

    private StorageAccess RequireScopedAccess(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var context = accessContextAccessor.Current ??
                      throw new InvalidOperationException("Workflow portfolio persistence access context is missing.");
        if (context.Scope is null || context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Groundwork workflow portfolio queries require one explicit persistence scope; global and across-scope access are refused.");
        }

        context.EnsureTenantScope(tenantId);
        return StorageAccess.Scoped(new StorageScope(context.Scope.Value));
    }

    private static OrderTerm Ascending(ColumnRef column) =>
        new(column, OrderDirection.Ascending, NullOrder.Last);

    private static OrderTerm Descending(ColumnRef column) =>
        new(column, OrderDirection.Descending, NullOrder.Last);

    private static string RequiredString(
        IReadOnlyDictionary<string, object?> row,
        string field,
        string subject)
    {
        if (row.TryGetValue(field, out var value))
        {
            switch (value)
            {
                case string text when !string.IsNullOrWhiteSpace(text):
                    return text;
                case JsonElement { ValueKind: JsonValueKind.String } element
                    when !string.IsNullOrWhiteSpace(element.GetString()):
                    return element.GetString()!;
            }
        }

        throw new InvalidDataException(
            $"Groundwork '{subject}' portfolio row is missing required string column '{field}'.");
    }

    private static ColumnRef Column(StorageUnit unit, TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
                             StringComparer.Ordinal.Equals(column.Name, name))
                         ?? throw new InvalidOperationException(
                             $"Groundwork workflow portfolio unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.Boolean => QueryType.Boolean,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            _ => throw new InvalidOperationException(
                $"Groundwork workflow portfolio query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }
}
