using System.Globalization;
using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Primitives.Entities;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Activities.Design.Persistence.Groundwork;

/// <summary>Stored v2 activity-design row with the provider revision returned by Groundwork.</summary>
public sealed record ActivityDesignDocument(
    string DocumentKind,
    string Id,
    string SchemaVersion,
    string ContentJson,
    long Version,
    DateTimeOffset UpdatedAt);

public sealed record ActivityDesignSaveRequest(
    string DocumentKind,
    string Id,
    string SchemaVersion,
    string ContentJson,
    long? ExpectedVersion = null);

public sealed record ActivityDesignDeleteRequest(string DocumentKind, string Id, long? ExpectedVersion = null);

public sealed class ActivityDesignWriteConflictException(string message) : InvalidOperationException(message);

public sealed record ActivityDesignCommitScope(IReadOnlyCollection<string> DocumentKinds)
{
    public static ActivityDesignCommitScope Of(params string[] documentKinds) => new(documentKinds);
}

public abstract record ActivityDesignWriteOperation
{
    public sealed record SaveOperation(ActivityDesignSaveRequest Request) : ActivityDesignWriteOperation;
    public sealed record DeleteOperation(ActivityDesignDeleteRequest Request) : ActivityDesignWriteOperation;

    public static ActivityDesignWriteOperation Save(ActivityDesignSaveRequest request) => new SaveOperation(request);
    public static ActivityDesignWriteOperation Delete(ActivityDesignDeleteRequest request) => new DeleteOperation(request);
}

public enum ActivityDesignQueryResultOperation
{
    Documents,
    Count,
    First,
    Any
}

public sealed record ActivityDesignQueryComparison(
    ActivityDesignComparisonKind Kind,
    string Field,
    object? Value = null,
    IReadOnlyList<object?>? Values = null)
{
    public static ActivityDesignQueryComparison Equal(string field, object? value) => new(ActivityDesignComparisonKind.Equal, field, value);
    public static ActivityDesignQueryComparison In(string field, IEnumerable<object?> values) => new(ActivityDesignComparisonKind.In, field, Values: values.ToArray());
    public static ActivityDesignQueryComparison In(string field, IEnumerable<string> values) => In(field, values.Cast<object?>());
    public static ActivityDesignQueryComparison LessThanOrEqual(string field, object? value) => new(ActivityDesignComparisonKind.LessThanOrEqual, field, value);
    public static ActivityDesignQueryComparison GreaterThan(string field, object? value) => new(ActivityDesignComparisonKind.GreaterThan, field, value);
    public static ActivityDesignQueryComparison Contains(string field, object? value) => new(ActivityDesignComparisonKind.Contains, field, value);
}

public enum ActivityDesignComparisonKind
{
    Equal,
    In,
    LessThanOrEqual,
    GreaterThan,
    Contains
}

public sealed record ActivityDesignQueryClause(IReadOnlyList<ActivityDesignQueryComparison> Comparisons)
{
    public static ActivityDesignQueryClause Of(ActivityDesignQueryComparison comparison) => new([comparison]);
    public static ActivityDesignQueryClause AnyOf(params ActivityDesignQueryComparison[] comparisons) => new(comparisons);
}

public sealed record ActivityDesignQueryOrder(string Field, bool Descending = false);

public sealed record ActivityDesignQuery(
    string DocumentKind,
    string Identity,
    IReadOnlyList<ActivityDesignQueryClause> Clauses,
    IReadOnlyList<ActivityDesignQueryOrder> Order,
    int Offset = 0,
    int? Take = null)
{
    public ActivityDesignQuery Select(ActivityDesignQueryResultOperation _) => this;
}

public sealed record ActivityDesignQueryResult(
    IReadOnlyList<ActivityDesignDocument> Documents,
    long TotalCount);

/// <summary>
/// Public-v2-only activity-design row adapter. It owns no provider connection and obtains every session and
/// transaction from <see cref="IGroundworkStorageSessionSource"/>.
/// </summary>
public sealed class GroundworkV2ActivityDesignStore(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    string? targetName = null)
{
    public ActivityDesignDocument? Load(string documentKind, string id, bool acrossScopes = false)
    {
        if (acrossScopes)
        {
            // Groundwork's privileged access is query-only. Resolve a cross-scope point read
            // through the public query contract rather than attempting a privileged session read.
            return Query(new ActivityDesignQuery(
                documentKind,
                "point-read",
                [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                    ActivitiesDesignStorageManifest.IdField,
                    id))],
                [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
                Take: 1), acrossScopes: true).Documents.FirstOrDefault();
        }

        var entry = Open(documentKind, acrossScopes).Read(Key(id));
        return entry is null ? null : ToDocument(documentKind, entry);
    }

    public Task<ActivityDesignDocument?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Load(documentKind, id));
    }

    public async Task SaveAsync(ActivityDesignSaveRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var unitOfWork = Begin(ActivityDesignCommitScope.Of(request.DocumentKind));
            unitOfWork.StageSave(request);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DesignPersistenceException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new DesignPersistenceException(
                DesignPersistenceDomain.Activity,
                DesignPersistenceFailureKind.Serialization,
                "save",
                request.DocumentKind,
                exception);
        }
    }

    public async Task SaveAllAsync(
        ActivityDesignCommitScope scope,
        IReadOnlyList<ActivityDesignWriteOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(operations);
        cancellationToken.ThrowIfCancellationRequested();
        using var unitOfWork = Begin(scope);
        foreach (var operation in operations)
        {
            switch (operation)
            {
                case ActivityDesignWriteOperation.SaveOperation save:
                    unitOfWork.StageSave(save.Request);
                    break;
                case ActivityDesignWriteOperation.DeleteOperation delete:
                    unitOfWork.StageDelete(delete.Request);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operations));
            }
        }

        await unitOfWork.CommitAsync(cancellationToken);
    }

    public Task SaveAllAsync(
        ActivityDesignCommitScope scope,
        IReadOnlyList<ActivityDesignSaveRequest> requests,
        CancellationToken cancellationToken = default) =>
        SaveAllAsync(scope, requests.Select(ActivityDesignWriteOperation.Save).ToArray(), cancellationToken);

    public Task WriteAllAsync(
        ActivityDesignCommitScope scope,
        IReadOnlyList<ActivityDesignWriteOperation> operations,
        CancellationToken cancellationToken = default) => SaveAllAsync(scope, operations, cancellationToken);

    public ActivityDesignQueryResult Query(ActivityDesignQuery query, bool acrossScopes = false)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Offset < 0 || query.Take is null or <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "Activity-design queries require a positive bounded page size.");
        var unit = sessions.Unit(query.DocumentKind, targetName);
        var table = new TableId(unit.Name);
        var order = query.Order.Count == 0
            ? [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)]
            : query.Order.Any(item => StringComparer.Ordinal.Equals(item.Field, ActivitiesDesignStorageManifest.IdField))
                ? query.Order
                : query.Order.Append(new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)).ToArray();
        var request = new QueryRequest(
            table,
            BuildPredicate(query, table),
            [.. order.Select(item => new OrderTerm(Column(table, item.Field),
                item.Descending ? OrderDirection.Descending : OrderDirection.Ascending,
                NullOrder.Last))],
            Projection.All,
            Paging.OffsetLimit(query.Offset, query.Take.Value),
            ResultShape.TotalCount.Instance);
        var session = Open(query.DocumentKind, acrossScopes);
        IReadOnlyList<ActivityDesignDocument> documents;
        long? totalCount;
        if (acrossScopes)
        {
            var result = session.QueryAcrossScopes(request);
            documents = result.Rows.Select(row => ToDocument(query.DocumentKind, row.Values)).ToArray();
            totalCount = result.TotalCount;
        }
        else
        {
            var result = session.Query(request);
            documents = result.Rows.Select(row => ToDocument(query.DocumentKind, row)).ToArray();
            totalCount = result.TotalCount;
        }
        return new ActivityDesignQueryResult(
            documents,
            totalCount ?? documents.Count);
    }

    public Task<ActivityDesignQueryResult> QueryAsync(
        ActivityDesignQuery query,
        CancellationToken cancellationToken = default,
        bool acrossScopes = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Query(query, acrossScopes));
    }

    public Task<ActivityDesignDocument?> FirstOrDefaultAsync(
        ActivityDesignQuery query,
        CancellationToken cancellationToken = default,
        bool acrossScopes = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = Query(query with { Take = 1 }, acrossScopes);
        return Task.FromResult(result.Documents.FirstOrDefault());
    }

    public ActivityDesignUnitOfWork Begin(ActivityDesignCommitScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var unitIds = scope.DocumentKinds.Distinct(StringComparer.Ordinal).ToArray();
        if (unitIds.Length == 0)
            throw new ArgumentException("At least one activity-design storage unit is required.", nameof(scope));
        var context = accessContextAccessor.Current;
        return new ActivityDesignUnitOfWork(
            sessions.BeginUnitOfWork(ToAccess(unitIds[0], context), BatchWriteOptions.Exact, unitIds, targetName),
            unitIds.ToDictionary(unitId => unitId, unitId => sessions.Unit(unitId, targetName), StringComparer.Ordinal),
            EnsureSaveScope);
    }

    internal IStorageSession Open(string documentKind, bool acrossScopes = false)
    {
        var context = accessContextAccessor.Current;
        return sessions.Open(documentKind, ToAccess(documentKind, context, acrossScopes), targetName);
    }

    private void EnsureSaveScope(ActivityDesignSaveRequest request)
    {
        var projections = ActivityDesignProjection.Project(request.ContentJson);
        var tenantId = projections.GetValueOrDefault(ActivitiesDesignStorageManifest.TenantIdField);
        if (tenantId is not null and not string)
            throw new InvalidDataException("The activity-design tenant projection is not a string.");
        accessContextAccessor.Current.EnsureTenantScope(tenantId as string);
    }

    private static StorageKey Key(string id) => new(new Dictionary<string, object?>
    {
        [ActivitiesDesignStorageManifest.IdField] = id
    });

    internal static ActivityDesignDocument ToDocument(string kind, StoredEntry entry)
    {
        var values = entry.Values.Values;
        var id = StringValue(values, ActivitiesDesignStorageManifest.IdField);
        var schemaVersion = StringValue(values, ActivitiesDesignStorageManifest.SchemaVersionField);
        var content = JsonValue(values, ActivitiesDesignStorageManifest.ContentField);
        var updatedAt = values.TryGetValue("updatedAt", out var timestamp) && timestamp is DateTimeOffset date
            ? date
            : DateTimeOffset.UnixEpoch;
        return new(kind, id, schemaVersion, content, LongValue(values, ActivitiesDesignStorageManifest.RevisionField) ?? entry.Version ?? 0, updatedAt);
    }

    internal static ActivityDesignDocument ToDocument(string kind, IReadOnlyDictionary<string, object?> values)
    {
        var id = StringValue(values, ActivitiesDesignStorageManifest.IdField);
        var schemaVersion = StringValue(values, ActivitiesDesignStorageManifest.SchemaVersionField);
        return new(kind, id, schemaVersion, JsonValue(values, ActivitiesDesignStorageManifest.ContentField),
            LongValue(values, ActivitiesDesignStorageManifest.RevisionField) ?? 0, DateTimeOffset.UnixEpoch);
    }

    private static Predicate BuildPredicate(ActivityDesignQuery query, TableId table)
    {
        var clauses = query.Clauses.Select(clause =>
            clause.Comparisons.Count == 1
                ? BuildComparison(clause.Comparisons[0], table)
                : new Predicate.Or(clause.Comparisons.Select(comparison => BuildComparison(comparison, table)))).ToArray();
        return clauses.Length == 0 ? Predicate.AlwaysTrue.Instance : new Predicate.And(clauses);
    }

    private static Predicate BuildComparison(ActivityDesignQueryComparison comparison, TableId table)
    {
        var column = Column(table, comparison.Field);
        return comparison.Kind switch
        {
            ActivityDesignComparisonKind.Equal => new Predicate.Equal(column, QueryConstant.Of(column, comparison.Value)),
            ActivityDesignComparisonKind.In => new Predicate.In(column, (comparison.Values ?? []).Select(value => QueryConstant.Of(column, value))),
            ActivityDesignComparisonKind.LessThanOrEqual => new Predicate.Range(column, null, Bound.Inclusive(QueryConstant.Of(column, comparison.Value))),
            ActivityDesignComparisonKind.GreaterThan => new Predicate.Range(column, Bound.Exclusive(QueryConstant.Of(column, comparison.Value)), null),
            ActivityDesignComparisonKind.Contains => new Predicate.Substring(column, Convert.ToString(comparison.Value, CultureInfo.InvariantCulture) ?? string.Empty, Anchor.Contains),
            _ => throw new ArgumentOutOfRangeException(nameof(comparison))
        };
    }

    private static ColumnRef Column(TableId table, string field) =>
        new(table, field, QueryType.String, isNullable: true, maxLength: ActivitiesDesignStorageManifest.MaximumProjectionLength);

    private StorageAccess ToAccess(
        string documentKind,
        PersistenceAccessContext context,
        bool acrossScopes = false)
    {
        if (acrossScopes && !context.AcrossScopes)
        {
            if (context.AccessPolicy != PersistenceAccessPolicy.Privileged || context.Scope is not null || context.Purpose is null)
            {
                throw new InvalidOperationException(
                    "Activity-design cross-scope access requires an explicit privileged global context.");
            }

            context = PersistenceAccessContext.PrivilegedAcrossScopes(context.Purpose);
        }

        return GroundworkStorageAccessMapper.Map(
            context,
            sessions.Unit(documentKind, targetName).Scope,
            "elsa-activities-design");
    }

    private static string StringValue(IReadOnlyDictionary<string, object?> values, string field) =>
        values.TryGetValue(field, out var value) switch
        {
            true when value is string text => text,
            true when value is JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
            _ => throw new InvalidDataException($"Groundwork activity-design row is missing string field '{field}'.")
        };

    private static long? LongValue(IReadOnlyDictionary<string, object?> values, string field) => values.TryGetValue(field, out var value) && value is long number ? number : null;

    private static string JsonValue(IReadOnlyDictionary<string, object?> values, string field) => values.TryGetValue(field, out var value) switch
    {
        true when value is string text => text,
        true when value is JsonElement element => element.GetRawText(),
        true when value is JsonDocument document => document.RootElement.GetRawText(),
        _ => throw new InvalidDataException($"Groundwork activity-design row is missing JSON field '{field}'.")
    };
}

public sealed class ActivityDesignUnitOfWork(
    IUnitOfWork inner,
    IReadOnlyDictionary<string, StorageUnit> units,
    Action<ActivityDesignSaveRequest>? validateSave = null) : IDisposable
{
    private readonly Dictionary<(string DocumentKind, string Id), ActivityDesignDocument?> staged = [];

    public ActivityDesignDocument? Load(string documentKind, string id)
    {
        if (staged.TryGetValue((documentKind, id), out var stagedDocument))
            return stagedDocument;

        var entry = inner.OpenSession(units[documentKind]).Read(new StorageKey(new Dictionary<string, object?>
        {
            [ActivitiesDesignStorageManifest.IdField] = id
        }));
        return entry is null ? null : GroundworkV2ActivityDesignStore.ToDocument(documentKind, entry);
    }

    public void StageSave(ActivityDesignSaveRequest request)
    {
        validateSave?.Invoke(request);
        var currentVersion = request.ExpectedVersion is null
            ? Load(request.DocumentKind, request.Id)?.Version
            : null;
        var values = GroundworkV2ActivityDesignProjection.Values(request, currentVersion);
        var unit = units[request.DocumentKind];
        var options = request.ExpectedVersion switch
        {
            null => WriteOptions.Unconditional,
            0 => WriteOptions.CreateOnly,
            var version => WriteOptions.IfVersion(version.Value)
        };
        inner.Stage(RowWrite.Upsert(unit, values, options));
        staged[(request.DocumentKind, request.Id)] = GroundworkV2ActivityDesignStore.ToDocument(
            request.DocumentKind,
            values.Values);
    }

    public void StageDelete(ActivityDesignDeleteRequest request)
    {
        var unit = units[request.DocumentKind];
        var options = request.ExpectedVersion is { } version && version > 0
            ? WriteOptions.IfVersion(version)
            : WriteOptions.Unconditional;
        inner.Stage(RowWrite.Delete(unit, new StorageKey(new Dictionary<string, object?>
        {
            [ActivitiesDesignStorageManifest.IdField] = request.Id
        }), options));
        staged[(request.DocumentKind, request.Id)] = null;
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        BatchWriteReport report;
        try
        {
            report = await inner.CommitWithOutcomesAsync(cancellationToken);
        }
        catch (BatchWriteException exception)
        {
            try
            {
                inner.Rollback();
            }
            catch
            {
                // Preserve the provider's attributed failure.
            }

            throw new ActivityDesignWriteConflictException(exception.Message);
        }
        catch
        {
            try
            {
                inner.Rollback();
            }
            catch
            {
                // Preserve the provider's original exception.
            }

            throw;
        }

        if (!report.IsSuccessful)
        {
            try
            {
                inner.Rollback();
            }
            catch
            {
                // Preserve the provider's attributed failure.
            }

            throw new ActivityDesignWriteConflictException(
                $"Groundwork rejected activity-design publication with {report.Failed} failed row outcomes.");
        }
    }

    public void Rollback() => inner.Rollback();
    public void Dispose() => inner.Dispose();
}

internal static class GroundworkV2ActivityDesignProjection
{
    public static StorageValues Values(ActivityDesignSaveRequest request, long? currentVersion = null)
    {
        var projections = ActivityDesignProjection.Project(request.ContentJson);
        var values = new Dictionary<string, object?>(projections, StringComparer.Ordinal)
        {
            [ActivitiesDesignStorageManifest.IdField] = request.Id,
            [ActivitiesDesignStorageManifest.SchemaVersionField] = request.SchemaVersion,
            [ActivitiesDesignStorageManifest.ContentField] = request.ContentJson,
            [ActivitiesDesignStorageManifest.RevisionField] = request.ExpectedVersion is { } expectedVersion
                ? checked(expectedVersion + 1)
                : checked(currentVersion.GetValueOrDefault() + 1),
            [ActivitiesDesignStorageManifest.ScopeField] = projections.GetValueOrDefault(ActivitiesDesignStorageManifest.TenantIdField),
            [ActivitiesDesignStorageManifest.TenantIdField] = projections.GetValueOrDefault(ActivitiesDesignStorageManifest.TenantIdField)
        };
        return new StorageValues(values);
    }
}

internal static class ActivityDesignProjection
{
    public static IReadOnlyDictionary<string, object?> Project(string contentJson)
    {
        using var document = JsonDocument.Parse(contentJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Activity-design content must be a JSON object.");
        if (root.TryGetProperty("entity", out var entity))
            root = entity;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Activity-design entity content must be a JSON object.");
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in Fields)
        {
            var path = field.Path.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            foreach (var segment in path)
            {
                if (!current.TryGetProperty(segment, out current))
                {
                    current = default;
                    break;
                }
            }
            if (current.ValueKind == JsonValueKind.Undefined)
                continue;
            values[field.Name] = Canonical(current);
        }
        if (!values.ContainsKey(ActivitiesDesignStorageManifest.TenantIdField))
        {
            foreach (var path in new[] { "plan.tenantId", "receipt.tenantId", "settings.tenantId" })
            {
                if (TryGet(root, path, out var tenant))
                {
                    values[ActivitiesDesignStorageManifest.TenantIdField] = Canonical(tenant);
                    break;
                }
            }
        }
        if (!values.TryGetValue(ActivitiesDesignStorageManifest.ManagementSearchField, out var explicitSearch) ||
            explicitSearch is not string)
        {
            values[ActivitiesDesignStorageManifest.ManagementSearchField] = new[]
            {
                values.GetValueOrDefault(ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField),
                values.GetValueOrDefault(ActivitiesDesignStorageManifest.ActivityDefinitionCategoryField),
                values.GetValueOrDefault(ActivitiesDesignStorageManifest.ActivityDefinitionDisplayNameField),
                values.GetValueOrDefault(ActivitiesDesignStorageManifest.ActivityDefinitionDescriptionField)
            }.OfType<string>().Aggregate(string.Empty, (current, value) => current + " " + value).Trim().ToUpperInvariant();
        }
        return values;
    }

    private static bool TryGet(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!value.TryGetProperty(segment, out value))
                return false;
        }

        return value.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null;
    }

    private static object? Canonical(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => null,
        _ => element.GetRawText()
    };

    private static readonly (string Name, string Path)[] Fields =
    [
        (ActivitiesDesignStorageManifest.TenantIdField, "tenantId"),
        (ActivitiesDesignStorageManifest.DefinitionIdField, "definitionId"),
        (ActivitiesDesignStorageManifest.HeadVersionIdField, "headVersionId"),
        (ActivitiesDesignStorageManifest.DraftIdField, "draftId"),
        (ActivitiesDesignStorageManifest.DefinitionVersionIdField, "definitionVersionId"),
        (ActivitiesDesignStorageManifest.OwnerVersionIdField, "ownerVersionId"),
        (ActivitiesDesignStorageManifest.DependencyVersionIdField, "dependencyVersionId"),
        (ActivitiesDesignStorageManifest.ManagementResourceIdField, "resourceId"),
        (ActivitiesDesignStorageManifest.ManagementValidFromField, "validFromKey"),
        (ActivitiesDesignStorageManifest.ManagementValidToField, "validToKey"),
        (ActivitiesDesignStorageManifest.ManagementVisibilityField, "visibilityKey"),
        (ActivitiesDesignStorageManifest.ManagementSortField, "sortKey"),
        (ActivitiesDesignStorageManifest.ManagementSearchField, "searchText"),
        (ActivitiesDesignStorageManifest.ManagementAuthorityField, "contentAuthority.kind"),
        (ActivitiesDesignStorageManifest.ManagementProviderField, "providerKey"),
        (ActivitiesDesignStorageManifest.ManagementHeadProviderField, "headProviderKey"),
        (ActivitiesDesignStorageManifest.ManagementRecommendationProviderField, "recommendationProviderKey"),
        (ActivitiesDesignStorageManifest.ManagementDraftStatusField, "status"),
        (ActivitiesDesignStorageManifest.ManagementVersionLifecycleField, "lifecycle"),
        (ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField, "activityTypeKey"),
        (ActivitiesDesignStorageManifest.ActivityDefinitionCategoryField, "category"),
        (ActivitiesDesignStorageManifest.ActivityDefinitionDisplayNameField, "displayName"),
        (ActivitiesDesignStorageManifest.ActivityDefinitionDescriptionField, "description"),
        (ActivitiesDesignStorageManifest.ActivityDefinitionVersionSemVerSortKeyField, "semVerSortKey"),
        (ActivitiesDesignStorageManifest.ActivityForkCandidateRetentionField, "retentionKey")
    ];
}

public static class ActivityDesignQueryPager
{
    public static async Task<IReadOnlyList<ActivityDesignDocument>> QueryAllOffsetAsync(
        GroundworkV2ActivityDesignStore store,
        string documentKind,
        string identity,
        IReadOnlyList<ActivityDesignQueryClause> clauses,
        IReadOnlyList<ActivityDesignQueryOrder> order,
        CancellationToken cancellationToken = default,
        bool acrossScopes = false)
    {
        var results = new List<ActivityDesignDocument>();
        var offset = 0;
        while (true)
        {
            var page = await store.QueryAsync(
                new(documentKind, identity, clauses, order, offset, 100),
                cancellationToken,
                acrossScopes);
            results.AddRange(page.Documents);
            if (page.Documents.Count == 0 || offset + page.Documents.Count >= page.TotalCount)
                return results;
            offset += page.Documents.Count;
        }
    }
}

public static class GroundworkV2ActivityDesignDocumentWriter
{
    public static ActivityDesignSaveRequest ToSaveRequest<TEntity>(
        string documentKind,
        string collection,
        string schemaVersion,
        TEntity entity,
        JsonSerializerOptions jsonOptions)
        where TEntity : Entity => new(
        documentKind,
        entity.Id,
        schemaVersion,
        JsonSerializer.Serialize(new GroundworkV2ActivityDesignDocument<TEntity>(collection, entity), jsonOptions));

    public static ActivityDesignSaveRequest ToTenantScopedSaveRequest<TEntity>(
        string documentKind,
        string collection,
        string schemaVersion,
        TEntity entity,
        JsonSerializerOptions jsonOptions,
        PersistenceAccessContext accessContext,
        object? persistenceDomain = null,
        string? failureContext = null)
        where TEntity : TenantEntity
    {
        accessContext.EnsureTenantScope(entity.TenantId);
        return ToSaveRequest(documentKind, collection, schemaVersion, entity, jsonOptions);
    }

    public static ActivityDesignDeleteRequest ToDeleteRequest(string documentKind, string id) => new(documentKind, id);
}

public sealed record GroundworkV2ActivityDesignDocument<TEntity>(string Collection, TEntity Entity) where TEntity : Entity;

public static class GroundworkMembershipBatches
{
    public static IReadOnlyList<string[]> Create(IEnumerable<string> values, int maximumBatchSize = 100)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (maximumBatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBatchSize));
        var distinct = values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return distinct.Length == 0 ? [] : distinct.Chunk(maximumBatchSize).ToArray();
    }
}
