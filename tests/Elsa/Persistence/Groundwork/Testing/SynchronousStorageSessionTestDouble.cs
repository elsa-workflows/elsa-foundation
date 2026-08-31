using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Supplies the asynchronous <see cref="IStorageSession"/> surface for synchronous test doubles.
/// Production adapters should delegate to the provider's native asynchronous implementation instead.
/// </summary>
public abstract class SynchronousStorageSessionTestDouble
{
    public ValueTask<StoredEntry?> ReadAsync(
        StorageKey key,
        CancellationToken cancellationToken = default) =>
        Invoke(session => session.Read(key), cancellationToken);

    public ValueTask<QueryMaterializedResult> QueryAsync(
        QueryRequest request,
        QueryRenderOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Invoke(session => session.Query(request, options), cancellationToken);

    public ValueTask<AggregationResult> AggregateAsync(
        AggregationQuery query,
        CancellationToken cancellationToken = default) =>
        Invoke(session => session.Aggregate(query), cancellationToken);

    public ValueTask<WriteOutcome> InsertAsync(
        StorageValues values,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Invoke(session => session.Insert(values, options), cancellationToken);

    public ValueTask<WriteOutcome> UpdateAsync(
        StorageValues values,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Invoke(session => session.Update(values, options), cancellationToken);

    public ValueTask<WriteOutcome> UpsertAsync(
        StorageValues values,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Invoke(session => session.Upsert(values, options), cancellationToken);

    public ValueTask<WriteOutcome> DeleteAsync(
        StorageKey key,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Invoke(session => session.Delete(key, options), cancellationToken);

    public ValueTask<WriteOutcome> AppendAsync(
        OperationId operationId,
        IReadOnlyList<StorageValues> values,
        CancellationToken cancellationToken = default) =>
        Invoke(session => session.Append(operationId, values), cancellationToken);

    public ValueTask<WriteOutcome> ConditionalUpsertAsync(
        StorageValues values,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Invoke<IConcurrencyStorageSession, WriteOutcome>(
            session => session.ConditionalUpsert(values, options),
            cancellationToken);

    public ValueTask<AppendOutcomeReport> AppendWithOutcomesAsync(
        OperationId operationId,
        IReadOnlyList<StorageValues> values,
        CancellationToken cancellationToken = default) =>
        Invoke<IExactAppendStorageSession, AppendOutcomeReport>(
            session => session.AppendWithOutcomes(operationId, values),
            cancellationToken);

    public ValueTask<CrossScopeQueryResult> QueryAcrossScopesAsync(
        QueryRequest request,
        QueryRenderOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Invoke<IPrivilegedCrossScopeQuerySession, CrossScopeQueryResult>(
            session => session.QueryAcrossScopes(request, options),
            cancellationToken);

    public ValueTask<StorageInspection> InspectAsync(CancellationToken cancellationToken = default) =>
        Invoke<IStorageInspectionSession, StorageInspection>(session => session.Inspect(), cancellationToken);

    public ValueTask<WriteOutcome> CompareAndDeleteAsync(
        StorageKey key,
        IReadOnlyDictionary<string, object?> expectedValues,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Invoke<ICompareAndDeleteStorageSession, WriteOutcome>(
            session => session.CompareAndDelete(key, expectedValues, options),
            cancellationToken);

    public ValueTask<RetentionOperationResult> ApplyRetentionAsync(
        OperationId operationId,
        RetentionExecutionOptions? options = null) =>
        Invoke<IExactRetentionStorageSession, RetentionOperationResult>(
            session => session.ApplyRetention(operationId, options),
            CancellationToken.None);

    private ValueTask<T> Invoke<T>(Func<IStorageSession, T> operation, CancellationToken cancellationToken)
        => Invoke<IStorageSession, T>(operation, cancellationToken);

    private ValueTask<T> Invoke<TSession, T>(Func<TSession, T> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (this is not TSession session)
        {
            throw new NotSupportedException(
                $"Test double '{GetType().Name}' does not implement optional Groundwork capability " +
                $"'{typeof(TSession).Name}'.");
        }

        return ValueTask.FromResult(operation(session));
    }
}
