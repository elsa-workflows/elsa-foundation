using Elsa.Persistence.Groundwork.Runtime;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Tests;

public sealed class GroundworkRuntimeRowStoreTests
{
    [Fact]
    public void Adapter_round_trips_envelope_and_declared_projections()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind);
        var session = new MemorySession(unit);
        var rows = new GroundworkRuntimeRowStore(session);

        var insert = rows.Insert(
            "bookmark-1",
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            "{\"bookmark\":true}",
            new Dictionary<string, object?>
            {
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = "workflow-1",
                [ElsaRuntimeV2StorageManifest.StimulusHashField] = "stimulus-1"
            });

        Assert.True(insert.Succeeded);
        var stored = rows.Read("bookmark-1");
        Assert.NotNull(stored);
        Assert.Equal("bookmark-1", stored.Values.Values[ElsaRuntimeV2StorageManifest.IdField]);
        Assert.Equal(ElsaRuntimeV2StorageManifest.SchemaVersion, stored.Values.Values[ElsaRuntimeV2StorageManifest.SchemaVersionField]);
        Assert.Equal("{\"bookmark\":true}", stored.Values.Values[ElsaRuntimeV2StorageManifest.ContentField]);
        Assert.Equal("workflow-1", stored.Values.Values[ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField]);
        Assert.Equal("stimulus-1", stored.Values.Values[ElsaRuntimeV2StorageManifest.StimulusHashField]);
    }

    [Fact]
    public void Adapter_does_not_allow_projection_overwriting_the_envelope()
    {
        Assert.Throws<ArgumentException>(() => GroundworkRuntimeRowStore.Values(
            "bookmark-1",
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            "{}",
            new Dictionary<string, object?>
            {
                [ElsaRuntimeV2StorageManifest.ContentField] = "tampered"
            }));
    }

    [Fact]
    public void Adapter_rejects_sessions_with_a_non_runtime_key()
    {
        var unit = StorageUnit.Declare("other", "other")
            .String("otherId")
            .Key("otherId")
            .Build();
        var session = new MemorySession(unit);

        Assert.Throws<ArgumentException>(() => new GroundworkRuntimeRowStore(session));
    }

    [Fact]
    public void Adapter_forwards_query_and_aggregate_requests_without_reinterpreting_them()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind);
        var session = new MemorySession(unit)
        {
            QueryResult = new QueryMaterializedResult(
                [new Dictionary<string, object?> { [ElsaRuntimeV2StorageManifest.IdField] = "bookmark-1" }],
                1,
                "next"),
            AggregationResult = new AggregationResult(
                [new AggregationRow(new Dictionary<string, object?> { ["count"] = 1L })])
        };
        var rows = new GroundworkRuntimeRowStore(session);
        var query = new QueryRequest(
            new TableId(unit.Name),
            new Predicate.AlwaysTrue(),
            [],
            Projection.All,
            Paging.None);

        var queryResult = rows.Query(query);
        var aggregateResult = rows.Aggregate(AggregationQuery.For("runtime-bookmark-count"));

        Assert.Same(session.QueryResult, queryResult);
        Assert.Same(session.AggregationResult, aggregateResult);
    }

    [Fact]
    public void Adapter_forwards_conditional_upsert_with_version_precondition_to_capable_session()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind);
        var session = new ConcurrencyMemorySession(unit);
        var rows = new GroundworkRuntimeRowStore(session);

        var outcome = rows.ConditionalUpsert("bookmark-1", "1.0.0", "{}", 41);

        Assert.True(outcome.Succeeded);
        Assert.Equal(WritePreconditionKind.IfVersion, session.ConditionalOptions!.Precondition.Kind);
        Assert.Equal(41, session.ConditionalOptions.Precondition.Version);
        Assert.Equal("bookmark-1", session.ConditionalValues!.Values[ElsaRuntimeV2StorageManifest.IdField]);
    }

    [Fact]
    public void Adapter_refuses_conditional_upsert_when_provider_lacks_concurrency_capability()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind);
        var rows = new GroundworkRuntimeRowStore(new MemorySession(unit));

        Assert.Throws<NotSupportedException>(() => rows.ConditionalUpsert("bookmark-1", "1.0.0", "{}", 41));
    }

    private class MemorySession(StorageUnit unit) : IStorageSession
    {
        private readonly Dictionary<string, StoredEntry> entries = new(StringComparer.Ordinal);

        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access { get; } = StorageAccess.Scoped(new StorageScope("tenant-a"));
        public QueryMaterializedResult? QueryResult { get; init; }
        public AggregationResult? AggregationResult { get; init; }

        public StoredEntry? Read(StorageKey key)
        {
            var id = (string)key.Values[ElsaRuntimeV2StorageManifest.IdField]!;
            return entries.GetValueOrDefault(id);
        }

        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) =>
            QueryResult ?? throw new NotSupportedException();

        public AggregationResult Aggregate(AggregationQuery query) => AggregationResult ?? throw new NotSupportedException();

        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => Write(values, WriteOutcomeStatus.Inserted);

        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => Write(values, WriteOutcomeStatus.Updated);

        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => Write(values, WriteOutcomeStatus.Upserted);

        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null)
        {
            var id = (string)key.Values[ElsaRuntimeV2StorageManifest.IdField]!;
            entries.Remove(id);
            return new WriteOutcome(WriteOutcomeStatus.Deleted, 1);
        }

        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) =>
            throw new NotSupportedException();

        protected WriteOutcome Write(StorageValues values, WriteOutcomeStatus status)
        {
            var id = (string)values.Values[ElsaRuntimeV2StorageManifest.IdField]!;
            entries[id] = new StoredEntry(values, 1);
            return new WriteOutcome(status, 1);
        }
    }

    private sealed class ConcurrencyMemorySession(StorageUnit unit) : MemorySession(unit), IConcurrencyStorageSession
    {
        public StorageValues? ConditionalValues { get; private set; }
        public WriteOptions? ConditionalOptions { get; private set; }

        public WriteOutcome ConditionalUpsert(StorageValues values, WriteOptions? options = null)
        {
            ConditionalValues = values;
            ConditionalOptions = options;
            return Write(values, WriteOutcomeStatus.Upserted);
        }
    }
}
