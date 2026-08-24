using System.Collections.Immutable;
using System.Globalization;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork;

/// <summary>
/// Public-v2 storage access for the publishing units.
/// <para>
/// Publishing's own reads and writes never leave the lane, and nothing here reaches across scopes, so
/// there is no privileged query seam to audit. A publication is one act across three lanes and commits
/// through a transaction spanning all of their units instead.
/// </para>
/// </summary>
public sealed class GroundworkPublishingStorage(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    string? targetName = null)
{
    public const string FeatureIdentity = "elsa-workflows-publishing";
    public const int ProviderPageSize = 256;

    public StorageUnit Unit(string unitId) => sessions.Unit(unitId, targetName);

    public static StorageKey Key(string id) => new(new Dictionary<string, object?>
    {
        [PublishingGroundworkStorageManifest.IdField] = id
    });

    public ColumnRef Column(string unitId, string name)
    {
        var unit = Unit(unitId);
        return Column(unit, new TableId(unit.Name), name);
    }

    public Predicate Equal(string unitId, string field, object? value)
    {
        var column = Column(unitId, field);
        return new Predicate.Equal(column, QueryConstant.Of(column, value));
    }

    public Predicate AtOrBefore(string unitId, string field, DateTimeOffset value)
    {
        var column = Column(unitId, field);
        return new Predicate.Range(column, null, Bound.Inclusive(QueryConstant.Of(column, value)));
    }

    public OrderTerm Order(string unitId, string field, bool descending = false) =>
        new(Column(unitId, field), descending ? OrderDirection.Descending : OrderDirection.Ascending, NullOrder.Last);

    public StoredEntry? Read(string unitId, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var session = Open(unitId);
        try
        {
            return session.Read(Key(id));
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
                $"Provider point read for unit '{unitId}' failed.", exception);
        }
    }

    /// <summary>
    /// Runs a named, index-backed route to exhaustion. Every publishing route is bounded by the caller's
    /// own key — a slot's definition, a record's slot, an intent's publication, or an expiry cutoff — so
    /// following the continuation is a bounded read rather than an unbounded scan.
    /// </summary>
    public IReadOnlyList<StoredEntry> Query(
        string unitId,
        Predicate predicate,
        IReadOnlyList<OrderTerm> order,
        string expectedIndex,
        int? take = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedIndex);
        cancellationToken.ThrowIfCancellationRequested();
        if (order.Count == 0)
            throw new GroundworkQueryReadinessException($"Named query '{expectedIndex}' requires a deterministic order.");

        var unit = Unit(unitId);
        var table = new TableId(unit.Name);
        var options = QueryOptions(unit, expectedIndex);
        var session = Open(unitId);
        var rows = new List<StoredEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? continuation = null;
        var page = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = take is null ? ProviderPageSize : Math.Min(ProviderPageSize, take.Value - rows.Count);
            if (remaining <= 0)
                break;
            var paging = continuation is null
                ? Paging.Keyset(remaining)
                : Paging.Continuation(continuation, remaining);
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
                    var id = Identity(row);
                    if (!seen.Add(id))
                        throw new GroundworkQueryReadinessException(
                            $"Provider query '{expectedIndex}' returned a duplicate row identity '{id}'.");
                    rows.Add(new StoredEntry(new StorageValues(row), null));
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

            var previousContinuation = continuation;
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

    public WriteOutcome Insert(string unitId, StorageValues values, WriteOptions? options = null) =>
        Open(unitId).Insert(values, options);

    public WriteOutcome Upsert(string unitId, StorageValues values, WriteOptions? options = null) =>
        Open(unitId).Upsert(values, options);

    public WriteOutcome ConditionalUpsert(string unitId, StorageValues values, WriteOptions options) =>
        RequireConcurrency(Open(unitId), unitId).ConditionalUpsert(values, options);

    public WriteOutcome Delete(string unitId, string id, WriteOptions? options = null) =>
        Open(unitId).Delete(Key(id), options);

    /// <summary>
    /// Opens one exact transaction over the named publishing units. Publishing's own writes never leave
    /// the lane; a publication that also writes design and runtime material asks the shared factory for a
    /// transaction spanning all three instead.
    /// </summary>
    public GroundworkStorageTransaction BeginUnitOfWork(IReadOnlyCollection<string> unitIds) =>
        new GroundworkStorageTransactionFactory(sessions, accessContextAccessor)
            .Begin(FeatureIdentity, unitIds, targetName);

    public IStorageSession Open(string unitId)
    {
        var unit = Unit(unitId);
        var current = accessContextAccessor.Current ?? throw new InvalidOperationException(
            "Publishing persistence access context is missing.");
        var access = GroundworkStorageAccessMapper.Map(current, unit.Scope, FeatureIdentity);
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

    private static string Identity(IReadOnlyDictionary<string, object?> values) =>
        Convert.ToString(
            values.GetValueOrDefault(PublishingGroundworkStorageManifest.IdField),
            CultureInfo.InvariantCulture) ?? "";

    private static IConcurrencyStorageSession RequireConcurrency(IStorageSession session, string unitId) =>
        session as IConcurrencyStorageSession ?? throw new NotSupportedException(
            $"Publishing unit '{unitId}' requires public Groundwork optimistic concurrency.");

    private static QueryRenderOptions QueryOptions(StorageUnit unit, string expectedIndex)
    {
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

}
