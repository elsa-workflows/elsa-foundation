using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.V2.Tests;

internal abstract class DelegatingStorageSession(IStorageSession inner) : SynchronousStorageSessionTestDouble, IStorageSession
{
    protected IStorageSession Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));

    public StorageUnit Unit => Inner.Unit;

    public StorageAccess Access => Inner.Access;

    public StoredEntry? Read(StorageKey key) => Inner.Read(key);

    public virtual QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) => Inner.Query(request, options);

    public AggregationResult Aggregate(AggregationQuery query) => Inner.Aggregate(query);

    public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => Inner.Insert(values, options);

    public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => Inner.Update(values, options);

    public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => Inner.Upsert(values, options);

    public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => Inner.Delete(key, options);

    public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => Inner.Append(operationId, values);
}
