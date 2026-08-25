using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// The small, public Groundwork v2 seam used by Identity-owned adapters. It deliberately exposes
/// rows rather than Groundwork v1 envelopes: the id, optimistic revision, canonical JSON payload,
/// and declared lookup projections are all ordinary v2 values.
/// </summary>
public sealed class GroundworkIdentityRowStore(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    string? targetName = null)
{
    internal string AccessIdentity
    {
        get
        {
            var current = accessContextAccessor.Current ?? throw new InvalidOperationException("Identity persistence access context is missing.");
            return current.IsGlobal ? "global" : $"scope:{current.Scope?.Value}";
        }
    }

    public GroundworkIdentityRow? Read(
        string unitId,
        string id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = Resolve(unitId);
        ValidateIdentity(id, nameof(id));
        return Map(plan.Session.Read(Key(id)), unitId, plan.Unit);
    }

    public IReadOnlyList<GroundworkIdentityRow> Query(
        string unitId,
        GroundworkIdentityRowQuery query,
        CancellationToken cancellationToken = default)
        => QueryCore(unitId, query, includeTotalCount: false, cancellationToken).Rows;

    public GroundworkIdentityRowQueryResult QueryWithTotalCount(
        string unitId,
        GroundworkIdentityRowQuery query,
        CancellationToken cancellationToken = default)
    {
        var result = QueryCore(unitId, query, includeTotalCount: true, cancellationToken);
        return new(
            result.Rows,
            result.TotalCount ?? throw new InvalidDataException(
                $"Identity unit '{unitId}' did not return the requested filtered total count."));
    }

    private QueryCoreResult QueryCore(
        string unitId,
        GroundworkIdentityRowQuery query,
        bool includeTotalCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var plan = Resolve(unitId);
        var table = new TableId(plan.Unit.Name);
        var filterColumn = Column(plan.Unit, table, query.FilterColumn);
        Predicate where = query.Comparison switch
        {
            GroundworkIdentityRowComparison.Equal =>
                (Predicate)new Predicate.Equal(filterColumn, QueryConstant.Of(filterColumn, query.Value)),
            GroundworkIdentityRowComparison.GreaterThan =>
                new Predicate.Range(filterColumn, Bound.Exclusive(QueryConstant.Of(filterColumn, query.Value)), null),
            GroundworkIdentityRowComparison.GreaterThanOrEqual =>
                new Predicate.Range(filterColumn, Bound.Inclusive(QueryConstant.Of(filterColumn, query.Value)), null),
            GroundworkIdentityRowComparison.LessThan =>
                new Predicate.Range(filterColumn, null, Bound.Exclusive(QueryConstant.Of(filterColumn, query.Value))),
            GroundworkIdentityRowComparison.LessThanOrEqual =>
                new Predicate.Range(filterColumn, null, Bound.Inclusive(QueryConstant.Of(filterColumn, query.Value))),
            _ => throw new ArgumentOutOfRangeException(nameof(query.Comparison))
        };

        var orderColumn = Column(plan.Unit, table, query.OrderColumn);
        var order = new List<OrderTerm>
        {
            new(orderColumn, query.Descending ? OrderDirection.Descending : OrderDirection.Ascending, NullOrder.Last)
        };
        if (!StringComparer.Ordinal.Equals(query.OrderColumn, IdentityV2StorageManifest.IdField))
        {
            var idColumn = Column(plan.Unit, table, IdentityV2StorageManifest.IdField);
            order.Add(new OrderTerm(idColumn, OrderDirection.Ascending, NullOrder.Last));
        }

        var paging = query.Skip == 0
            ? Paging.Keyset(query.Take)
            : Paging.OffsetLimit(query.Skip, query.Take);
        var request = includeTotalCount
            ? new QueryRequest(table, where, [.. order], Projection.All, paging, ResultShape.TotalCount.Instance)
            : new QueryRequest(table, where, [.. order], Projection.All, paging);
        var result = plan.Session.Query(request, plan.Unit.CreateQueryRenderOptions(query.ExpectedIndex));

        var rows = new List<GroundworkIdentityRow>(result.Rows.Count);
        foreach (var row in result.Rows)
        {
            var id = RequiredString(row, IdentityV2StorageManifest.IdField);
            var mapped = Map(row, unitId, plan.Unit);
            if (mapped is not null)
            {
                if (!query.IncludeVersions)
                {
                    rows.Add(mapped);
                    continue;
                }

                // Query rows intentionally have no provider-specific revision column. Authority
                // lookups opt into a point re-read when their caller needs a concurrency stamp;
                // relationship lists keep their single bounded provider query.
                var authoritative = plan.Session.Read(Key(id));
                rows.Add(authoritative is null ? mapped : Map(authoritative, unitId, plan.Unit)!);
            }
        }

        return new(rows, result.TotalCount);
    }

    public GroundworkIdentityWriteResult Save(
        GroundworkIdentityRowWrite write,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        cancellationToken.ThrowIfCancellationRequested();
        var plan = Resolve(write.UnitId);
        ValidateIdentity(write.Id, nameof(write.Id));
        var session = plan.Session;
        var values = Values(write.Id, write.CanonicalJson, write.ProjectedValues);
        var outcome = write.Condition.Kind switch
        {
            GroundworkIdentityRowWriteConditionKind.CreateOnly => session.Insert(values, WriteOptions.CreateOnly),
            GroundworkIdentityRowWriteConditionKind.Unconditional => session.Upsert(values, WriteOptions.Unconditional),
            GroundworkIdentityRowWriteConditionKind.ExpectedVersion => RequireConcurrency(session, write.UnitId)
                .ConditionalUpsert(values, WriteOptions.IfVersion(write.Condition.ExpectedVersion!.Value)),
            _ => throw new ArgumentOutOfRangeException(nameof(write.Condition.Kind))
        };

        // A positive revision is an update contract, not an insert contract. Providers commonly
        // report a missing conditional-upsert target as a generic concurrency conflict; normalize
        // that provider detail only after a follow-up read, so a concurrent insert cannot be
        // mistaken for NotFound.
        if (write.Condition.Kind == GroundworkIdentityRowWriteConditionKind.ExpectedVersion &&
            write.Condition.ExpectedVersion is > 0 &&
            outcome.Status == WriteOutcomeStatus.ConcurrencyConflict &&
            session.Read(Key(write.Id)) is null)
        {
            return GroundworkIdentityWriteResult.NotFound();
        }

        return GroundworkIdentityWriteResult.From(outcome);
    }

    public GroundworkIdentityWriteResult Delete(
        GroundworkIdentityRowDelete delete,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delete);
        cancellationToken.ThrowIfCancellationRequested();
        var plan = Resolve(delete.UnitId);
        ValidateIdentity(delete.Id, nameof(delete.Id));
        var options = delete.Condition.Kind switch
        {
            GroundworkIdentityRowWriteConditionKind.Unconditional => WriteOptions.Unconditional,
            GroundworkIdentityRowWriteConditionKind.ExpectedVersion => WriteOptions.IfVersion(delete.Condition.ExpectedVersion!.Value),
            GroundworkIdentityRowWriteConditionKind.CreateOnly =>
                throw new ArgumentException("A delete cannot use the create-only condition.", nameof(delete.Condition)),
            _ => throw new ArgumentOutOfRangeException(nameof(delete.Condition.Kind))
        };
        if (delete.Condition.Kind == GroundworkIdentityRowWriteConditionKind.ExpectedVersion)
            _ = RequireConcurrency(plan.Session, delete.UnitId);
        return GroundworkIdentityWriteResult.From(plan.Session.Delete(Key(delete.Id), options));
    }

    /// <summary>
    /// Stages every mutation against one exact public v2 unit of work. All rows are pre-read before
    /// the UOW is opened, conditional mutations are checked against those reads, and the shadow map
    /// carries staged state when a batch touches the same identity more than once.
    /// </summary>
    public BatchWriteReport WriteBatch(
        IReadOnlyList<GroundworkIdentityRowMutation> mutations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutations);
        if (mutations.Count == 0)
            throw new ArgumentException("At least one Identity row mutation is required.", nameof(mutations));
        cancellationToken.ThrowIfCancellationRequested();

        var plans = mutations
            .Select(mutation => (Mutation: mutation, Plan: Resolve(mutation.UnitId)))
            .ToArray();
        var accesses = plans.Select(item => item.Plan.Access).Distinct().ToArray();
        if (accesses.Length != 1)
            throw new InvalidOperationException("Identity row batches must use one explicit persistence access context.");

        var units = plans.Select(item => item.Plan.Unit).DistinctBy(unit => unit.Id.Value, StringComparer.Ordinal).ToArray();
        var preRead = new Dictionary<(string UnitId, string Id), GroundworkIdentityRow?>();
        foreach (var item in plans)
        {
            ValidateIdentity(item.Mutation.Id, nameof(item.Mutation.Id));
            var key = (item.Mutation.UnitId, item.Mutation.Id);
            if (!preRead.ContainsKey(key))
                preRead[key] = Map(item.Plan.Session.Read(Key(item.Mutation.Id)), item.Mutation.UnitId, item.Plan.Unit);
        }

        using var unitOfWork = sessions.BeginUnitOfWork(
            accesses[0],
            BatchWriteOptions.Exact,
            units.Select(unit => unit.Id.Value).ToArray(),
            targetName);
        var shadow = new Dictionary<(string UnitId, string Id), GroundworkIdentityRow?>(preRead);
        foreach (var item in plans)
        {
            var mutation = item.Mutation;
            var key = (mutation.UnitId, mutation.Id);
            var current = shadow[key];
            var write = mutation.Kind switch
            {
                GroundworkIdentityRowMutationKind.Save => StageSave(item.Plan.Unit, mutation),
                GroundworkIdentityRowMutationKind.Delete => StageDelete(item.Plan.Unit, mutation),
                _ => throw new ArgumentOutOfRangeException(nameof(mutation.Kind))
            };
            unitOfWork.Stage(write);
            shadow[key] = mutation.Kind == GroundworkIdentityRowMutationKind.Delete
                ? null
                : Shadow(mutation, current);
        }

        return unitOfWork.CommitWithOutcomes();
    }

    private AccessPlan Resolve(string unitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
        var unit = sessions.Unit(unitId, targetName);
        var current = accessContextAccessor.Current ?? throw new InvalidOperationException("Identity persistence access context is missing.");
        if (unit.Scope == ScopePolicy.Scoped)
        {
            if (current.Scope is null || current.AcrossScopes)
            {
                throw new InvalidOperationException(
                    $"Identity unit '{unitId}' is scoped and requires one explicit persistence scope before provider access.");
            }

            var access = StorageAccess.Scoped(new StorageScope(current.Scope.Value));
            return new AccessPlan(unit, access, sessions.Open(unitId, access, targetName));
        }

        if (!current.IsGlobal)
        {
            throw new InvalidOperationException(
                $"Identity unit '{unitId}' is global and requires explicit global persistence access before provider access.");
        }

        return new AccessPlan(unit, StorageAccess.Global, sessions.Open(unitId, StorageAccess.Global, targetName));
    }

    private static RowWrite StageSave(StorageUnit unit, GroundworkIdentityRowMutation mutation)
    {
        var options = Options(mutation.Condition);
        return RowWrite.ConditionalUpsert(unit, Values(mutation.Id, mutation.CanonicalJson!, mutation.ProjectedValues), options);
    }

    private static RowWrite StageDelete(StorageUnit unit, GroundworkIdentityRowMutation mutation)
    {
        var options = Options(mutation.Condition);
        return RowWrite.Delete(unit, Key(mutation.Id), options);
    }

    private static WriteOptions Options(GroundworkIdentityRowWriteCondition condition)
    {
        return condition.Kind switch
        {
            GroundworkIdentityRowWriteConditionKind.CreateOnly => WriteOptions.CreateOnly,
            GroundworkIdentityRowWriteConditionKind.Unconditional => WriteOptions.Unconditional,
            GroundworkIdentityRowWriteConditionKind.ExpectedVersion => WriteOptions.IfVersion(condition.ExpectedVersion!.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(condition.Kind))
        };
    }

    private static GroundworkIdentityRow Shadow(GroundworkIdentityRowMutation mutation, GroundworkIdentityRow? current) =>
        new(
            mutation.UnitId,
            mutation.Id,
            IdentityStorageManifest.SchemaVersion,
            checked((current?.Version ?? 0) + 1),
            mutation.CanonicalJson!,
            mutation.ProjectedValues);

    private static IConcurrencyStorageSession RequireConcurrency(IStorageSession session, string unitId) =>
        session as IConcurrencyStorageSession ?? throw new NotSupportedException(
            $"Identity unit '{unitId}' requires Groundwork optimistic concurrency, but the selected provider did not expose IConcurrencyStorageSession.");

    private static StorageValues Values(
        string id,
        string canonicalJson,
        IReadOnlyDictionary<string, object?> projectedValues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalJson);
        var values = new Dictionary<string, object?>(projectedValues, StringComparer.Ordinal)
        {
            [IdentityV2StorageManifest.IdField] = id,
            [IdentityV2StorageManifest.SchemaVersionField] = IdentityStorageManifest.SchemaVersion,
            [IdentityV2StorageManifest.ContentField] = canonicalJson
        };
        return new StorageValues(values);
    }

    private static StorageKey Key(string id) => new(new Dictionary<string, object?>
    {
        [IdentityV2StorageManifest.IdField] = id
    });

    private static GroundworkIdentityRow? Map(
        StoredEntry? entry,
        string unitId,
        StorageUnit unit) => entry is null
            ? null
            : Map(entry.Values.Values, unitId, unit, entry.Version ?? throw new InvalidDataException($"Identity unit '{unitId}' returned a row without an optimistic version."));

    private static GroundworkIdentityRow Map(
        IReadOnlyDictionary<string, object?> values,
        string unitId,
        StorageUnit unit,
        long version = 0)
    {
        var schemaVersion = RequiredString(values, IdentityV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, IdentityStorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Identity unit '{unitId}' returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{IdentityStorageManifest.SchemaVersion}'.");
        }

        return new GroundworkIdentityRow(
            unitId,
            RequiredString(values, IdentityV2StorageManifest.IdField),
            schemaVersion,
            version,
            CanonicalJson(values, unitId),
            ProjectedValues(values));
    }

    private static string RequiredString(IReadOnlyDictionary<string, object?> values, string field) => values.TryGetValue(field, out var value) && value is string text
        ? text
        : throw new InvalidDataException($"Identity Groundwork row is missing required string field '{field}'.");

    private static string CanonicalJson(IReadOnlyDictionary<string, object?> values, string unitId) => values.TryGetValue(IdentityV2StorageManifest.ContentField, out var value)
        ? value switch
        {
            string text => text,
            System.Text.Json.JsonElement element => element.GetRawText(),
            System.Text.Json.JsonDocument document => document.RootElement.GetRawText(),
            _ => throw new InvalidDataException($"Identity unit '{unitId}' returned a non-JSON content value.")
        }
        : throw new InvalidDataException($"Identity unit '{unitId}' returned a row without its canonical JSON content.");

    private static IReadOnlyDictionary<string, object?> ProjectedValues(IReadOnlyDictionary<string, object?> values) =>
        values
            .Where(pair => pair.Key is not IdentityV2StorageManifest.IdField and
                           not IdentityV2StorageManifest.SchemaVersionField and
                           not IdentityV2StorageManifest.ContentField)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static ColumnRef Column(StorageUnit unit, TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column => StringComparer.Ordinal.Equals(column.Name, name))
                         ?? throw new ArgumentException($"Identity unit '{unit.Id.Value}' does not declare query column '{name}'.", nameof(name));
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            _ => throw new ArgumentException($"Identity query column '{name}' has unsupported type '{definition.Type}'.", nameof(name))
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private static void ValidateIdentity(string value, string parameterName) => ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

    private sealed record AccessPlan(StorageUnit Unit, StorageAccess Access, IStorageSession Session);

    private sealed record QueryCoreResult(
        IReadOnlyList<GroundworkIdentityRow> Rows,
        long? TotalCount);
}

public sealed record GroundworkIdentityRow(
    string UnitId,
    string Id,
    string SchemaVersion,
    long Version,
    string CanonicalJson,
    IReadOnlyDictionary<string, object?> ProjectedValues);

public enum GroundworkIdentityRowComparison
{
    Equal,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}

public sealed record GroundworkIdentityRowQuery(
    string FilterColumn,
    GroundworkIdentityRowComparison Comparison,
    object Value,
    string OrderColumn,
    bool Descending = false,
    int Take = 100,
    bool IncludeVersions = false,
    int Skip = 0,
    string? ExpectedIndex = null);

public sealed record GroundworkIdentityRowQueryResult(
    IReadOnlyList<GroundworkIdentityRow> Rows,
    long TotalCount);

public enum GroundworkIdentityRowWriteConditionKind
{
    CreateOnly,
    ExpectedVersion,
    Unconditional
}

public sealed record GroundworkIdentityRowWriteCondition(
    GroundworkIdentityRowWriteConditionKind Kind,
    long? ExpectedVersion = null)
{
    public static GroundworkIdentityRowWriteCondition CreateOnly { get; } = new(GroundworkIdentityRowWriteConditionKind.CreateOnly);
    public static GroundworkIdentityRowWriteCondition Unconditional { get; } = new(GroundworkIdentityRowWriteConditionKind.Unconditional);
    public static GroundworkIdentityRowWriteCondition IfVersion(long version) => new(GroundworkIdentityRowWriteConditionKind.ExpectedVersion, version);
}

public sealed record GroundworkIdentityRowWrite(
    string UnitId,
    string Id,
    string CanonicalJson,
    IReadOnlyDictionary<string, object?> ProjectedValues,
    GroundworkIdentityRowWriteCondition Condition);

public sealed record GroundworkIdentityRowDelete(
    string UnitId,
    string Id,
    GroundworkIdentityRowWriteCondition Condition);

public enum GroundworkIdentityRowMutationKind
{
    Save,
    Delete
}

public sealed record GroundworkIdentityRowMutation(
    GroundworkIdentityRowMutationKind Kind,
    string UnitId,
    string Id,
    string? CanonicalJson,
    IReadOnlyDictionary<string, object?> ProjectedValues,
    GroundworkIdentityRowWriteCondition Condition)
{
    public static GroundworkIdentityRowMutation Save(GroundworkIdentityRowWrite write) =>
        new(GroundworkIdentityRowMutationKind.Save, write.UnitId, write.Id, write.CanonicalJson, write.ProjectedValues, write.Condition);

    public static GroundworkIdentityRowMutation Delete(GroundworkIdentityRowDelete delete) =>
        new(GroundworkIdentityRowMutationKind.Delete, delete.UnitId, delete.Id, null, new Dictionary<string, object?>(), delete.Condition);
}

public sealed record GroundworkIdentityWriteResult(
    WriteOutcomeStatus Status,
    long? Version,
    string Message,
    GroundworkIdentityRow? Row = null,
    string? AuthoritativeId = null,
    string? FailedUnitId = null)
{
    public bool Succeeded => Status is WriteOutcomeStatus.Inserted or WriteOutcomeStatus.Updated or WriteOutcomeStatus.Upserted or WriteOutcomeStatus.Deleted or WriteOutcomeStatus.Replayed;

    public static GroundworkIdentityWriteResult From(WriteOutcome outcome) =>
        new(outcome.Status, outcome.Version, outcome.Detail?.Message ?? outcome.Status.ToString());

    public static GroundworkIdentityWriteResult Saved(GroundworkIdentityRow row) =>
        new(row.Version == 1 ? WriteOutcomeStatus.Inserted : WriteOutcomeStatus.Updated, row.Version, "Identity row saved.", row, row.Id);

    public static GroundworkIdentityWriteResult NotFound(string? authoritativeId = null) =>
        new(WriteOutcomeStatus.NotFound, null, "Identity row was not found.", AuthoritativeId: authoritativeId);

    public static GroundworkIdentityWriteResult ConcurrencyConflict(string? authoritativeId = null) =>
        new(WriteOutcomeStatus.ConcurrencyConflict, null, "Identity row version did not match.", AuthoritativeId: authoritativeId);
}
