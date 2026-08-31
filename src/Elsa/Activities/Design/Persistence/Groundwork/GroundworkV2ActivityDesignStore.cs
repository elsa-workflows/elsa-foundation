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
    long? ExpectedVersion = null,
    DateTimeOffset? UpdatedAt = null);

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
    int? Take = null,
    string? ContinuationToken = null)
{
    public ActivityDesignQuery Select(ActivityDesignQueryResultOperation _) => this;
}

public sealed record ActivityDesignQueryResult(
    IReadOnlyList<ActivityDesignDocument> Documents,
    long TotalCount,
    string? NextContinuationToken = null);

// Cross-scope results retain the provider-owned scope until the public adapter has
// completed ambiguity checks and structural de-duplication.  Tenant fields in the
// document payload are caller-controlled content and are never used as an identity.
internal sealed record ActivityDesignScopedDocument(StorageScope? Scope, ActivityDesignDocument Document);

internal sealed record ActivityDesignQueryExecutionResult(
    IReadOnlyList<ActivityDesignScopedDocument> Rows,
    long TotalCount,
    string? NextContinuationToken);

/// <summary>
/// Public-v2-only activity-design row adapter. It owns no provider connection and obtains every session and
/// transaction from <see cref="IGroundworkStorageSessionSource"/>.
/// </summary>
public sealed class GroundworkV2ActivityDesignStore(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    string? targetName = null,
    TimeProvider? timeProvider = null,
    GroundworkPrivilegedQueryAuditExecutor? privilegedQueryAuditExecutor = null)
{
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public ActivityDesignDocument? Load(string documentKind, string id, bool acrossScopes = false)
    {
        if (acrossScopes)
        {
            // Groundwork's privileged access is query-only. Resolve a cross-scope point read
            // through the public query contract rather than attempting a privileged session read.
            var result = QueryScopedPage(new ActivityDesignQuery(
                documentKind,
                "point-read",
                [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                    ActivitiesDesignStorageManifest.IdField,
                    id))],
                [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
                Take: 2), acrossScopes: true, ensureSearchCatalogBound: false);
            if (result.Rows.Count > 1)
                throw new InvalidOperationException(
                    $"Activity-design point read for '{documentKind}/{id}' is ambiguous across storage scopes.");
            return result.Rows.FirstOrDefault()?.Document;
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
        => ToPublicResult(QueryScopedPage(query, acrossScopes, ensureSearchCatalogBound: true));

    internal ActivityDesignQueryResult QueryPage(
        ActivityDesignQuery query,
        bool acrossScopes,
        bool ensureSearchCatalogBound) =>
        ToPublicResult(QueryScopedPage(query, acrossScopes, ensureSearchCatalogBound));

    internal ActivityDesignQueryExecutionResult QueryScopedPage(
        ActivityDesignQuery query,
        bool acrossScopes,
        bool ensureSearchCatalogBound)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Identity);
        if (query.Offset < 0 || query.Take is null or <= 0 || query.Take > ActivityDesignQueryPager.PageSize)
            throw new ArgumentOutOfRangeException(nameof(query), "Activity-design queries require a positive bounded page size.");
        if (query.Offset > 0 && query.ContinuationToken is not null)
            throw new ArgumentException("An activity-design query cannot combine an offset and a continuation token.", nameof(query));
        if (acrossScopes)
        {
            if (privilegedQueryAuditExecutor is null)
            {
                throw new InvalidOperationException(
                    "Activity-design cross-scope queries require the public-v2 privileged query audit executor.");
            }

            var context = accessContextAccessor.Current
                ?? throw new InvalidOperationException("The current persistence access context is unavailable.");
            if (context.AccessPolicy != PersistenceAccessPolicy.Privileged || !context.AcrossScopes)
            {
                throw new InvalidOperationException(
                    "Activity-design cross-scope queries require an explicit privileged-across-scopes context.");
            }
        }

        var unit = sessions.Unit(query.DocumentKind, targetName);
        var selectedIndex = ResolveRouteIndex(unit, query.DocumentKind, query.Identity);
        ValidateRoutePredicate(query);
        var table = new TableId(unit.Name);
        var order = query.Order.Count == 0
            ? RouteOrder(query.Identity)
            : query.Order.Any(item => StringComparer.Ordinal.Equals(item.Field, ActivitiesDesignStorageManifest.IdField))
                ? query.Order
                : query.Order.Append(new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)).ToArray();
        var paging = query.ContinuationToken is { } continuation
            ? Paging.Continuation(continuation, query.Take.Value)
            : query.Offset == 0
                ? Paging.Keyset(query.Take.Value)
                : Paging.OffsetLimit(query.Offset, query.Take.Value);
        var request = new QueryRequest(
            table,
            BuildPredicate(query, unit, table),
            [.. order.Select(item => new OrderTerm(Column(unit, table, item.Field),
                item.Descending ? OrderDirection.Descending : OrderDirection.Ascending,
                NullOrder.Last))],
            Projection.All,
            paging,
            ResultShape.TotalCount.Instance,
            acceptedScan: query.Identity == ActivitiesDesignStorageManifest.SearchActivityDefinitionsQuery
                ? SearchScanAcceptance
                : null);
        if (acrossScopes)
        {
            var executor = privilegedQueryAuditExecutor!;
            return executor.Execute(
                query.DocumentKind,
                "elsa-activities-design",
                session => ExecuteCrossScopeQuery(
                    session,
                    query,
                    unit,
                    request,
                    selectedIndex,
                    ensureSearchCatalogBound));
        }

        var ordinarySession = Open(query.DocumentKind, acrossScopes: false);
        if (ensureSearchCatalogBound && query.Identity == ActivitiesDesignStorageManifest.SearchActivityDefinitionsQuery)
            EnsureSearchCatalogBound(ordinarySession, unit);
        var ordinaryResult = ordinarySession.Query(request, unit.CreateQueryRenderOptions(selectedIndex));
        var documents = ordinaryResult.Rows
            .Select(row => new ActivityDesignScopedDocument(ordinarySession.Access.Scope, ToDocument(query.DocumentKind, row)))
            .ToArray();
        var totalCount = ordinaryResult.TotalCount;
        var nextContinuationToken = ordinaryResult.NextContinuationToken;
        return new ActivityDesignQueryExecutionResult(documents, totalCount ?? documents.Length, nextContinuationToken);
    }

    private static ActivityDesignQueryExecutionResult ExecuteCrossScopeQuery(
        IPrivilegedCrossScopeQuerySession session,
        ActivityDesignQuery query,
        StorageUnit unit,
        QueryRequest request,
        string? selectedIndex,
        bool ensureSearchCatalogBound)
    {
        if (ensureSearchCatalogBound && query.Identity == ActivitiesDesignStorageManifest.SearchActivityDefinitionsQuery)
            EnsureSearchCatalogBound(session, unit);
        var result = session.QueryAcrossScopes(request, unit.CreateQueryRenderOptions(selectedIndex));
        var rows = result.Rows
            .Select(row => new ActivityDesignScopedDocument(row.Scope, ToDocument(query.DocumentKind, row.Values)))
            .ToArray();
        var duplicate = rows
            .GroupBy(row => (row.Scope?.Value, row.Document.Id))
            .FirstOrDefault(group => group.Skip(1).Any());
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Activity-design query '{query.Identity}' returned duplicate identity '{duplicate.Key.Id}' in storage scope '{duplicate.Key.Value ?? "<global>"}'.");
        return new ActivityDesignQueryExecutionResult(rows, result.TotalCount ?? rows.Length, result.NextContinuationToken);
    }

    private static ActivityDesignQueryResult ToPublicResult(ActivityDesignQueryExecutionResult result) =>
        new(result.Rows.Select(row => row.Document).ToArray(), result.TotalCount, result.NextContinuationToken);

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
        var result = Query(query with { Take = acrossScopes ? 2 : 1 }, acrossScopes);
        if (acrossScopes && result.Documents.Count > 1)
            throw new InvalidOperationException(
                $"Activity-design point query '{query.Identity}' is ambiguous across storage scopes.");
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
            EnsureSaveScope,
            timeProvider);
    }

    internal IStorageSession Open(string documentKind, bool acrossScopes = false)
    {
        if (acrossScopes)
        {
            throw new InvalidOperationException(
                "Activity-design cross-scope sessions must be acquired through the public-v2 privileged query audit executor.");
        }

        var context = accessContextAccessor.Current;
        return sessions.Open(documentKind, ToAccess(documentKind, context), targetName);
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
        var updatedAt = values.TryGetValue(ActivitiesDesignStorageManifest.UpdatedAtField, out var timestamp) && timestamp is DateTimeOffset date
            ? date
            : DateTimeOffset.UnixEpoch;
        return new(kind, id, schemaVersion, content, LongValue(values, ActivitiesDesignStorageManifest.RevisionField) ?? entry.Version ?? 0, updatedAt);
    }

    internal static ActivityDesignDocument ToDocument(string kind, IReadOnlyDictionary<string, object?> values)
    {
        var id = StringValue(values, ActivitiesDesignStorageManifest.IdField);
        var schemaVersion = StringValue(values, ActivitiesDesignStorageManifest.SchemaVersionField);
        return new(kind, id, schemaVersion, JsonValue(values, ActivitiesDesignStorageManifest.ContentField),
            LongValue(values, ActivitiesDesignStorageManifest.RevisionField) ?? 0,
            values.TryGetValue(ActivitiesDesignStorageManifest.UpdatedAtField, out var updatedAt) && updatedAt is DateTimeOffset timestamp
                ? timestamp
                : DateTimeOffset.UnixEpoch);
    }

    private static Predicate BuildPredicate(ActivityDesignQuery query, StorageUnit unit, TableId table)
    {
        var clauses = query.Clauses.Select(clause =>
            clause.Comparisons.Count == 1
                ? BuildComparison(clause.Comparisons[0], unit, table)
                : new Predicate.Or(clause.Comparisons.Select(comparison => BuildComparison(comparison, unit, table)))).ToArray();
        return clauses.Length == 0 ? Predicate.AlwaysTrue.Instance : new Predicate.And(clauses);
    }

    private static IReadOnlyList<ActivityDesignQueryOrder> RouteOrder(string identity) => identity switch
    {
        ActivitiesDesignStorageManifest.ListActivityDefinitionsByTypeKeyQuery =>
            ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyOrder,
        ActivitiesDesignStorageManifest.ListAllActivityDefinitionsQuery or
        ActivitiesDesignStorageManifest.SearchActivityDefinitionsQuery =>
            ActivitiesDesignStorageManifest.ActivityDefinitionDisplayNameOrder,
        ActivitiesDesignStorageManifest.ListActivityDefinitionsByCategoryQuery =>
            ActivitiesDesignStorageManifest.ActivityDefinitionCategoryOrder,
        ActivitiesDesignStorageManifest.ListActivityDefinitionsByDisplayNameQuery =>
            ActivitiesDesignStorageManifest.ActivityDefinitionDisplayNameOrder,
        ActivitiesDesignStorageManifest.ListActivityDefinitionsByDescriptionQuery =>
            ActivitiesDesignStorageManifest.ActivityDefinitionDescriptionOrder,
        ActivitiesDesignStorageManifest.ListActivityDefinitionVersionsByDefinitionQuery or
            ActivitiesDesignStorageManifest.FindActivityDefinitionVersionByDefinitionAndSortKeyQuery =>
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionOrder,
        _ => [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)]
    };

    private static string? ResolveRouteIndex(StorageUnit unit, string documentKind, string identity)
    {
        var index = (documentKind, identity) switch
        {
            (ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ListActivityDefinitionsByTypeKeyQuery) => "activity_definition_by_type_key",
            (ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ListActivityDefinitionsByCategoryQuery) => "activity_definition_by_category",
            (ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ListActivityDefinitionsByDisplayNameQuery) => "activity_definition_by_display_name",
            (ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ListActivityDefinitionsByDescriptionQuery) => "activity_definition_by_description",
            (ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.FindActivityDefinitionByIdQuery or
                ActivitiesDesignStorageManifest.ListActivityDefinitionsByIdQuery) => null,
            (ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ListAllActivityDefinitionsQuery) => "activity_definition_by_display_name",
            (ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.SearchActivityDefinitionsQuery) => null,
            (ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
                ActivitiesDesignStorageManifest.FindActivityDefinitionVersionByIdQuery) => null,
            (ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
                ActivitiesDesignStorageManifest.ListActivityDefinitionVersionsByDefinitionQuery) =>
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionByDefinitionIndex,
            (ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
                ActivitiesDesignStorageManifest.FindActivityDefinitionVersionByDefinitionAndSortKeyQuery) =>
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionByDefinitionIndex,
            (_, "list-by-definition") => ActivitiesDesignStorageManifest.ByDefinitionIndex,
            (_, "list-by-head-version") => ActivitiesDesignStorageManifest.ByHeadVersionIndex,
            (_, "list-by-draft") => ActivitiesDesignStorageManifest.ByDraftIndex,
            (_, "list-by-definition-version") => ActivitiesDesignStorageManifest.ByDefinitionVersionIndex,
            (_, "list-by-owner-version") => ActivitiesDesignStorageManifest.ByOwnerVersionIndex,
            (_, "list-by-dependency-version") => ActivitiesDesignStorageManifest.ByDependencyVersionIndex,
            (ActivitiesDesignStorageManifest.ActivityForkCandidateDocumentKind,
                ActivitiesDesignStorageManifest.ActivityForkCandidateExpiredQuery) =>
                ActivitiesDesignStorageManifest.ActivityForkCandidateRetentionIndex,
            (ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind,
                ActivitiesDesignStorageManifest.ManagementDefinitionsQuery) => "management_definitions_identity_asc",
            (ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind,
                ActivitiesDesignStorageManifest.ManagementDraftsQuery) => "management_drafts_identity_asc",
            (ActivitiesDesignStorageManifest.ActivityVersionManagementProjectionDocumentKind,
                ActivitiesDesignStorageManifest.ManagementVersionsQuery) => "management_versions_identity_asc",
            (ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind or
                ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind or
                ActivitiesDesignStorageManifest.ActivityVersionManagementProjectionDocumentKind,
                ActivitiesDesignStorageManifest.ManagementExpiredQuery) => ActivitiesDesignStorageManifest.ManagementExpiredIndex,
            (_, "point-read") or (_, "list-design-operations") or
                (_, ActivitiesDesignStorageManifest.ListAllDocumentsQuery) or
                (_, ActivitiesDesignStorageManifest.ManagementDefinitionCurrentQuery) or
                (_, ActivitiesDesignStorageManifest.ManagementDraftCurrentQuery) or
                (_, ActivitiesDesignStorageManifest.ManagementVersionCurrentQuery) => null,
            _ => throw new ArgumentException(
                $"Activity-design query identity '{identity}' is not declared for document kind '{documentKind}'.",
                nameof(identity))
        };

        if (index is not null && unit.Indexes.All(candidate => !StringComparer.Ordinal.Equals(candidate.Name, index)))
            throw new InvalidOperationException(
                $"Activity-design query route '{identity}' selects undeclared index '{index}' on unit '{documentKind}'.");
        return index;
    }

    private static void ValidateRoutePredicate(ActivityDesignQuery query)
    {
        var comparisons = query.Clauses.SelectMany(clause => clause.Comparisons).ToArray();
        if (query.Identity == ActivitiesDesignStorageManifest.SearchActivityDefinitionsQuery)
        {
            var searchFields = new HashSet<string>(StringComparer.Ordinal)
            {
                ActivitiesDesignStorageManifest.ActivityDefinitionIdField,
                ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField,
                ActivitiesDesignStorageManifest.ActivityDefinitionCategoryField,
                ActivitiesDesignStorageManifest.ActivityDefinitionDisplayNameField,
                ActivitiesDesignStorageManifest.ActivityDefinitionDescriptionField,
                ActivitiesDesignStorageManifest.ManagementSearchField
            };
            if (query.Clauses.Count != 1 || query.Clauses[0].Comparisons.Count == 0 ||
                query.Clauses[0].Comparisons.Any(comparison =>
                    comparison.Kind != ActivityDesignComparisonKind.Contains ||
                    !searchFields.Contains(comparison.Field)))
            {
                throw new ArgumentException(
                    $"Activity-design search route '{query.Identity}' requires one bounded OR clause of admitted substring predicates.",
                    nameof(query));
            }

            return;
        }

        IReadOnlyList<RoutePredicateRule> rules = query.Identity switch
        {
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByTypeKeyQuery =>
                [new RoutePredicateRule(ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField,
                    ActivityDesignComparisonKind.Equal, ActivityDesignComparisonKind.In)],
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByCategoryQuery =>
                [new RoutePredicateRule(ActivitiesDesignStorageManifest.ActivityDefinitionCategoryField,
                    ActivityDesignComparisonKind.Equal)],
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByDisplayNameQuery =>
                [new RoutePredicateRule(ActivitiesDesignStorageManifest.ActivityDefinitionDisplayNameField,
                    ActivityDesignComparisonKind.Equal)],
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByDescriptionQuery =>
                [new RoutePredicateRule(ActivitiesDesignStorageManifest.ActivityDefinitionDescriptionField,
                    ActivityDesignComparisonKind.Contains)],
            ActivitiesDesignStorageManifest.FindActivityDefinitionByIdQuery =>
                [new RoutePredicateRule(ActivitiesDesignStorageManifest.ActivityDefinitionIdField,
                    ActivityDesignComparisonKind.Equal)],
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByIdQuery =>
                [new RoutePredicateRule(ActivitiesDesignStorageManifest.ActivityDefinitionIdField,
                    ActivityDesignComparisonKind.Equal, ActivityDesignComparisonKind.In)],
            ActivitiesDesignStorageManifest.FindActivityDefinitionVersionByDefinitionAndSortKeyQuery =>
            [
                new RoutePredicateRule(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDefinitionIdField,
                    ActivityDesignComparisonKind.Equal),
                new RoutePredicateRule(ActivitiesDesignStorageManifest.ActivityDefinitionVersionSemVerSortKeyField,
                    ActivityDesignComparisonKind.Equal)
            ],
            ActivitiesDesignStorageManifest.ListActivityDefinitionVersionsByDefinitionQuery =>
                query.Clauses.Count == 0
                    ? []
                    : [new RoutePredicateRule(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDefinitionIdField,
                        ActivityDesignComparisonKind.Equal, ActivityDesignComparisonKind.In)],
            _ when StringComparer.Ordinal.Equals(query.Identity, ActivitiesDesignStorageManifest.ManagementExpiredQuery) &&
                   (query.DocumentKind is ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind or
                       ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind or
                       ActivitiesDesignStorageManifest.ActivityVersionManagementProjectionDocumentKind) =>
                [new RoutePredicateRule(ActivitiesDesignStorageManifest.ManagementValidToField,
                    ActivityDesignComparisonKind.LessThanOrEqual)],
            "point-read" =>
                [new RoutePredicateRule(ActivitiesDesignStorageManifest.IdField, ActivityDesignComparisonKind.Equal)],
            "list-by-definition" =>
                query.Clauses.Count == 0
                    ? []
                    : [new RoutePredicateRule(ActivitiesDesignStorageManifest.DefinitionIdField,
                        ActivityDesignComparisonKind.Equal, ActivityDesignComparisonKind.In)],
            "list-by-head-version" =>
                [new RoutePredicateRule(ActivitiesDesignStorageManifest.HeadVersionIdField,
                    ActivityDesignComparisonKind.Equal, ActivityDesignComparisonKind.In)],
            "list-by-draft" =>
                [new RoutePredicateRule(ActivitiesDesignStorageManifest.DraftIdField,
                    ActivityDesignComparisonKind.Equal, ActivityDesignComparisonKind.In)],
            "list-by-definition-version" =>
                [new RoutePredicateRule(ActivitiesDesignStorageManifest.DefinitionVersionIdField,
                    ActivityDesignComparisonKind.Equal, ActivityDesignComparisonKind.In)],
            "list-by-owner-version" =>
                query.Clauses.Count == 0
                    ? []
                    : [new RoutePredicateRule(ActivitiesDesignStorageManifest.OwnerVersionIdField,
                        ActivityDesignComparisonKind.Equal, ActivityDesignComparisonKind.In)],
            "list-by-dependency-version" =>
                [new RoutePredicateRule(ActivitiesDesignStorageManifest.DependencyVersionIdField,
                    ActivityDesignComparisonKind.Equal, ActivityDesignComparisonKind.In)],
            ActivitiesDesignStorageManifest.ActivityForkCandidateExpiredQuery =>
                [new RoutePredicateRule(ActivitiesDesignStorageManifest.ActivityForkCandidateRetentionField,
                    ActivityDesignComparisonKind.LessThanOrEqual)],
            _ => []
        };

        foreach (var rule in rules)
        {
            if (!comparisons.Any(comparison =>
                    StringComparer.Ordinal.Equals(comparison.Field, rule.Field) &&
                    rule.Operations.Contains(comparison.Kind)))
            {
                throw new ArgumentException(
                    $"Activity-design query route '{query.Identity}' requires an admitted {string.Join("/", rule.Operations)} predicate on '{rule.Field}'.",
                    nameof(query));
            }
        }
    }

    private sealed record RoutePredicateRule(string Field, params ActivityDesignComparisonKind[] Operations);

    private static void EnsureSearchCatalogBound(IStorageSession session, StorageUnit unit)
    {
        var table = new TableId(unit.Name);
        var id = Column(unit, table, ActivitiesDesignStorageManifest.IdField);
        var request = new QueryRequest(
            table,
            Predicate.AlwaysTrue.Instance,
            [new OrderTerm(id, OrderDirection.Ascending, NullOrder.Last)],
            Projection.ColumnsOnly([id]),
            Paging.Keyset(ActivitiesDesignStorageManifest.MaximumActivityDefinitionSearchCatalogRows + 1),
            acceptedScan: SearchScanAcceptance);
        var result = session.Query(request, unit.CreateQueryRenderOptions()).Rows.Count;
        if (result > ActivitiesDesignStorageManifest.MaximumActivityDefinitionSearchCatalogRows)
        {
            throw new InvalidOperationException(
                $"Activity-definition substring search is refused when the current scope contains more than {ActivitiesDesignStorageManifest.MaximumActivityDefinitionSearchCatalogRows} rows.");
        }
    }

    private static void EnsureSearchCatalogBound(IPrivilegedCrossScopeQuerySession session, StorageUnit unit)
    {
        var table = new TableId(unit.Name);
        var id = Column(unit, table, ActivitiesDesignStorageManifest.IdField);
        var request = new QueryRequest(
            table,
            Predicate.AlwaysTrue.Instance,
            [new OrderTerm(id, OrderDirection.Ascending, NullOrder.Last)],
            Projection.ColumnsOnly([id]),
            Paging.Keyset(ActivitiesDesignStorageManifest.MaximumActivityDefinitionSearchCatalogRows + 1),
            acceptedScan: SearchScanAcceptance);
        var result = session.QueryAcrossScopes(request, unit.CreateQueryRenderOptions()).Rows.Count;
        if (result > ActivitiesDesignStorageManifest.MaximumActivityDefinitionSearchCatalogRows)
        {
            throw new InvalidOperationException(
                $"Activity-definition substring search is refused when the current scope contains more than {ActivitiesDesignStorageManifest.MaximumActivityDefinitionSearchCatalogRows} rows.");
        }
    }

    private static readonly ScanAcceptance SearchScanAcceptance = ScanAcceptance.Allow(
        "GW-SCAN-ELSA-ACTIVITY-DESIGN-SUBSTRING",
        $"The public search contract preserves cross-field substring matching. The query first admits a bounded catalog-cardinality probe of at most {ActivitiesDesignStorageManifest.MaximumActivityDefinitionSearchCatalogRows + 1} rows and refuses larger scopes; the result page is capped at 100 rows and uses a keyset cursor.",
        "elsa-activities-design",
        new DateTimeOffset(2027, 8, 16, 0, 0, 0, TimeSpan.Zero));

    private static Predicate BuildComparison(ActivityDesignQueryComparison comparison, StorageUnit unit, TableId table)
    {
        var column = Column(unit, table, comparison.Field);
        if (comparison.Value is string scalar)
            ValidateLength(unit, comparison.Field, scalar);
        foreach (var value in comparison.Values?.OfType<string>() ?? [])
            ValidateLength(unit, comparison.Field, value);
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

    private static ColumnRef Column(StorageUnit unit, TableId table, string field)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, field))
            ?? throw new InvalidOperationException(
                $"Groundwork activity-design unit '{unit.Id.Value}' does not declare query column '{field}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork activity-design query column '{field}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, field, type, definition.IsNullable, definition.MaxLength);
    }

    private static void ValidateLength(StorageUnit unit, string field, string value)
    {
        var definition = unit.Columns.Single(column => StringComparer.Ordinal.Equals(column.Name, field));
        if (definition.MaxLength is { } maxLength && value.Length > maxLength)
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Activity-design query value for '{field}' exceeds its declared maximum length of {maxLength}.");
    }

    private StorageAccess ToAccess(
        string documentKind,
        PersistenceAccessContext context,
        bool acrossScopes = false)
    {
        if (acrossScopes && !context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Activity-design cross-scope access requires an explicit privileged-across-scopes context.");
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
    Action<ActivityDesignSaveRequest>? validateSave = null,
    TimeProvider? timeProvider = null) : IDisposable
{
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;
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
        var values = GroundworkV2ActivityDesignProjection.Values(request, currentVersion, timeProvider.GetUtcNow());
        var unit = units[request.DocumentKind];
        GroundworkProjectedText.EnsureFits(unit, values, "Activity-design");
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
    public static StorageValues Values(
        ActivityDesignSaveRequest request,
        long? currentVersion = null,
        DateTimeOffset? updatedAt = null)
    {
        var projections = ActivityDesignProjection.Project(request.ContentJson);
        var values = new Dictionary<string, object?>(projections, StringComparer.Ordinal)
        {
            [ActivitiesDesignStorageManifest.IdField] = request.Id,
            [ActivitiesDesignStorageManifest.SchemaVersionField] = request.SchemaVersion,
            [ActivitiesDesignStorageManifest.ContentField] = request.ContentJson,
            [ActivitiesDesignStorageManifest.UpdatedAtField] = request.UpdatedAt ?? updatedAt ?? DateTimeOffset.UtcNow,
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
    public const int PageSize = 100;

    public static async Task<IReadOnlyList<ActivityDesignDocument>> QueryAllAsync(
        GroundworkV2ActivityDesignStore store,
        string documentKind,
        string identity,
        IReadOnlyList<ActivityDesignQueryClause> clauses,
        IReadOnlyList<ActivityDesignQueryOrder> order,
        CancellationToken cancellationToken = default,
        bool acrossScopes = false)
    {
        var rows = await QueryAllScopedAsync(
            store,
            documentKind,
            identity,
            clauses,
            order,
            cancellationToken,
            acrossScopes);
        return rows.Select(row => row.Document).ToArray();
    }

    internal static Task<IReadOnlyList<ActivityDesignScopedDocument>> QueryAllScopedAsync(
        GroundworkV2ActivityDesignStore store,
        string documentKind,
        string identity,
        IReadOnlyList<ActivityDesignQueryClause> clauses,
        IReadOnlyList<ActivityDesignQueryOrder> order,
        CancellationToken cancellationToken = default,
        bool acrossScopes = false)
    {
        var identities = new HashSet<(string? Scope, string Id)>();
        var continuations = new HashSet<string>(StringComparer.Ordinal);
        string? continuation = null;
        var scopedResults = new List<ActivityDesignScopedDocument>();
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            var page = store.QueryScopedPage(
                new(documentKind, identity, clauses, order, Take: PageSize, ContinuationToken: continuation),
                acrossScopes,
                ensureSearchCatalogBound: continuation is null);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var row in page.Rows)
            {
                // A cross-scope result's provider scope is intentionally not
                // reconstructed from JSON tenant content. QueryPage has already
                // preserved it for the structural identity check below.
                if (!identities.Add((acrossScopes ? row.Scope?.Value : null, row.Document.Id)))
                    throw new InvalidDataException(
                        $"Activity-design query returned duplicate identity '{row.Document.Id}' in storage scope '{row.Scope?.Value ?? "<global>"}'.");
                scopedResults.Add(row);
            }
            var next = page.NextContinuationToken;
            if (next is null)
                return Task.FromResult<IReadOnlyList<ActivityDesignScopedDocument>>(scopedResults);
            if (page.Rows.Count == 0 || !continuations.Add(next))
                return Task.FromException<IReadOnlyList<ActivityDesignScopedDocument>>(
                    new InvalidDataException("Activity-design query continuation repeated or advanced an empty page."));
            continuation = next;
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
        JsonSerializer.Serialize(new GroundworkV2ActivityDesignDocument<TEntity>(collection, entity), jsonOptions),
        UpdatedAt: entity.LastModifiedAt == default ? null : entity.LastModifiedAt);

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
