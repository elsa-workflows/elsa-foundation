using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Delegating <see cref="IStorageSession"/> that records what a store asked of the provider session it wraps.
/// Every query lands in <see cref="Queries"/> (a caller-supplied collection may be shared across sessions);
/// the optional sinks capture render options, selected index hints and conditional-write options, and
/// <see cref="BeforeFencedDelete"/> runs once before the first delete so a test can interleave a competing write.
/// The optional Groundwork capabilities are forwarded to the inner session, which throws when it lacks them.
/// </summary>
public sealed class RecordingSession(IStorageSession inner, ICollection<QueryRequest>? queries = null)
    : SynchronousStorageSessionTestDouble,
        IStorageSession,
        IConcurrencyStorageSession,
        ICompareAndDeleteStorageSession,
        IPrivilegedCrossScopeQuerySession
{
    private Action? _beforeFencedDelete;

    public ICollection<QueryRequest> Queries { get; } = queries ?? new List<QueryRequest>();
    public ICollection<(QueryRequest Request, QueryRenderOptions? Options)>? RenderedQueries { get; init; }
    public ICollection<string>? IndexHints { get; init; }
    public ICollection<WriteOptions?>? ConditionalWrites { get; init; }
    public Action? BeforeFencedDelete { init => _beforeFencedDelete = value; }

    public StorageUnit Unit => inner.Unit;
    public StorageAccess Access => inner.Access;
    public StoredEntry? Read(StorageKey key) => inner.Read(key);

    public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
    {
        Record(request, options);
        return inner.Query(request, options);
    }

    public CrossScopeQueryResult QueryAcrossScopes(QueryRequest request, QueryRenderOptions? options = null)
    {
        Record(request, options);
        return Require<IPrivilegedCrossScopeQuerySession>().QueryAcrossScopes(request, options);
    }

    public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
    public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
    public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
    public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
    public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);

    public WriteOutcome ConditionalUpsert(StorageValues values, WriteOptions? options = null)
    {
        ConditionalWrites?.Add(options);
        return Require<IConcurrencyStorageSession>().ConditionalUpsert(values, options);
    }

    public WriteOutcome Delete(StorageKey key, WriteOptions? options = null)
    {
        Interlocked.Exchange(ref _beforeFencedDelete, null)?.Invoke();
        return inner.Delete(key, options);
    }

    public WriteOutcome CompareAndDelete(StorageKey key, IReadOnlyDictionary<string, object?> expectedValues, WriteOptions? options = null)
    {
        Interlocked.Exchange(ref _beforeFencedDelete, null)?.Invoke();
        return Require<ICompareAndDeleteStorageSession>().CompareAndDelete(key, expectedValues, options);
    }

    private void Record(QueryRequest request, QueryRenderOptions? options)
    {
        Queries.Add(request);
        RenderedQueries?.Add((request, options));
        if (options?.SelectedIndex is { } selectedIndex)
            IndexHints?.Add(selectedIndex);
    }

    private TSession Require<TSession>() where TSession : class =>
        inner as TSession ?? throw new NotSupportedException(
            $"The recorded session '{inner.GetType().Name}' does not implement '{typeof(TSession).Name}'.");
}
