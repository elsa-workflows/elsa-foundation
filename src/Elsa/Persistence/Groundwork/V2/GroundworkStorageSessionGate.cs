using System.Runtime.ExceptionServices;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// Defers access to one admitted Groundwork v2 session until startup has applied and verified the
/// feature-owned declaration. This gate deliberately preserves optional v2 capabilities instead of
/// emulating them: callers receive the provider's exact append, inspection, and retention contracts.
/// </summary>
public sealed class GroundworkStorageSessionGate :
    IStorageSession
{
    private readonly Lock gate = new();
    private IStorageSession? session;
    private ExceptionDispatchInfo? failure;
    private bool released;

    public StorageUnit Unit => GetSession().Unit;

    public StorageAccess Access => GetSession().Access;

    /// <summary>
    /// Returns the admitted provider session without widening its runtime capability set. Optional
    /// interfaces must be queried on this value; the gate intentionally does not claim capabilities
    /// that the provider may not implement.
    /// </summary>
    public IStorageSession Current => GetSession();

    public void Publish(IStorageSession value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (gate)
        {
            if (released)
                throw new ObjectDisposedException(nameof(GroundworkStorageSessionGate));
            if (failure is not null)
                throw new InvalidOperationException("A failed Groundwork v2 session gate cannot publish a session.");
            if (session is not null)
                throw new InvalidOperationException("The Groundwork v2 session gate has already published a session.");
            session = value;
        }
    }

    public void PublishFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (gate)
        {
            if (session is not null || failure is not null)
                return;
            failure = ExceptionDispatchInfo.Capture(exception);
        }
    }

    public void Release()
    {
        lock (gate)
        {
            session = null;
            released = true;
        }
    }

    public StoredEntry? Read(StorageKey key) => GetSession().Read(key);

    public ValueTask<StoredEntry?> ReadAsync(StorageKey key, CancellationToken cancellationToken = default) =>
        GetSession().ReadAsync(key, cancellationToken);

    public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) =>
        GetSession().Query(request, options);

    public ValueTask<QueryMaterializedResult> QueryAsync(
        QueryRequest request,
        QueryRenderOptions? options = null,
        CancellationToken cancellationToken = default) =>
        GetSession().QueryAsync(request, options, cancellationToken);

    public AggregationResult Aggregate(AggregationQuery query) => GetSession().Aggregate(query);

    public ValueTask<AggregationResult> AggregateAsync(
        AggregationQuery query,
        CancellationToken cancellationToken = default) =>
        GetSession().AggregateAsync(query, cancellationToken);

    public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => GetSession().Insert(values, options);

    public ValueTask<WriteOutcome> InsertAsync(
        StorageValues values,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default) =>
        GetSession().InsertAsync(values, options, cancellationToken);

    public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => GetSession().Update(values, options);

    public ValueTask<WriteOutcome> UpdateAsync(
        StorageValues values,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default) =>
        GetSession().UpdateAsync(values, options, cancellationToken);

    public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => GetSession().Upsert(values, options);

    public ValueTask<WriteOutcome> UpsertAsync(
        StorageValues values,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default) =>
        GetSession().UpsertAsync(values, options, cancellationToken);

    public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => GetSession().Delete(key, options);

    public ValueTask<WriteOutcome> DeleteAsync(
        StorageKey key,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default) =>
        GetSession().DeleteAsync(key, options, cancellationToken);

    public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) =>
        GetSession().Append(operationId, values);

    public ValueTask<WriteOutcome> AppendAsync(
        OperationId operationId,
        IReadOnlyList<StorageValues> values,
        CancellationToken cancellationToken = default) =>
        GetSession().AppendAsync(operationId, values, cancellationToken);

    private IStorageSession GetSession()
    {
        ExceptionDispatchInfo? startupFailure;
        lock (gate)
        {
            if (session is not null)
                return session;
            startupFailure = failure;
            if (startupFailure is null && released)
                throw new ObjectDisposedException(nameof(GroundworkStorageSessionGate));
        }

        startupFailure?.Throw();
        throw new InvalidOperationException(
            "The Groundwork v2 storage session has not completed startup admission.");
    }
}
