using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2BookmarkStateStoreTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Sqlite_round_trips_pages_and_isolates_scopes()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind);
        connection.Schema.Apply(unit);

        var source = new DirectSessionSource(connection, unit);
        var scopeA = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var scopeB = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b")));
        IBookmarkStateStore storeA = new GroundworkV2BookmarkStateStore(source, scopeA);
        IBookmarkStateStore storeB = new GroundworkV2BookmarkStateStore(source, scopeB);

        await storeA.SaveAsync(Bookmark("wf-1", "bm-b", "HttpEndpoint", "h1"));
        await storeA.SaveAsync(Bookmark("wf-1", "bm-a", "HttpEndpoint", "h1"));
        await storeA.SaveAsync(Bookmark("wf-2", "bm-c", "Signal", "h2"));
        await storeB.SaveAsync(Bookmark("wf-1", "bm-b", "HttpEndpoint", "h1"));

        var found = await storeA.FindAsync("wf-1", "bm-a");
        Assert.NotNull(found);
        Assert.Equal("/orders", found!.Payload!.Value.GetProperty("url").GetString());
        Assert.Null(await storeB.FindAsync("wf-1", "bm-a"));

        var first = await storeA.ListPageAsync(new BookmarkStatePageQuery("wf-1", limit: 1));
        Assert.Equal(["bm-a"], first.Items.Select(bookmark => bookmark.BookmarkId));
        Assert.NotNull(first.NextContinuationToken);
        var second = await storeA.ListPageAsync(
            new BookmarkStatePageQuery("wf-1", limit: 1, first.NextContinuationToken));
        Assert.Equal(["bm-b"], second.Items.Select(bookmark => bookmark.BookmarkId));
        Assert.Null(second.NextContinuationToken);

        var stimulusIndex = (IBookmarkStimulusIndex)storeA;
        var stimulusFirst = await stimulusIndex.ListByStimulusPageAsync(
            new BookmarkStimulusPageQuery("HttpEndpoint", "h1", limit: 1));
        var stimulusSecond = await stimulusIndex.ListByStimulusPageAsync(
            new BookmarkStimulusPageQuery("HttpEndpoint", "h1", limit: 1, stimulusFirst.NextContinuationToken));
        Assert.Equal(["bm-a"], stimulusFirst.Items.Select(bookmark => bookmark.BookmarkId));
        Assert.Equal(["bm-b"], stimulusSecond.Items.Select(bookmark => bookmark.BookmarkId));
        Assert.Null(stimulusSecond.NextContinuationToken);

        var typeFirst = await stimulusIndex.ListByStimulusTypePageAsync(
            new BookmarkStimulusTypePageQuery("HttpEndpoint", limit: 1));
        var typeSecond = await stimulusIndex.ListByStimulusTypePageAsync(
            new BookmarkStimulusTypePageQuery("HttpEndpoint", limit: 1, typeFirst.NextContinuationToken));
        Assert.Equal(["bm-a"], typeFirst.Items.Select(bookmark => bookmark.BookmarkId));
        Assert.Equal(["bm-b"], typeSecond.Items.Select(bookmark => bookmark.BookmarkId));
        Assert.Null(typeSecond.NextContinuationToken);
    }

    [Fact]
    public async Task Save_replaces_and_delete_reports_only_the_owned_row()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind);
        connection.Schema.Apply(unit);

        var source = new DirectSessionSource(connection, unit);
        var accessor = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IBookmarkStateStore store = new GroundworkV2BookmarkStateStore(source, accessor);

        await store.SaveAsync(Bookmark("wf-1", "bm-a", "HttpEndpoint", "h1"));
        await store.SaveAsync(Bookmark("wf-1", "bm-a", "Timer", "h2"));
        Assert.Equal("Timer", (await store.FindAsync("wf-1", "bm-a"))!.StimulusType);
        Assert.False(await store.DeleteAsync("wf-2", "bm-a"));
        Assert.True(await store.DeleteAsync("wf-1", "bm-a"));
        Assert.False(await store.DeleteAsync("wf-1", "bm-a"));
        Assert.Null(await store.FindAsync("wf-1", "bm-a"));
    }

    [Fact]
    public async Task Same_bookmark_id_coexists_across_workflows_and_delete_keeps_the_other_row()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind);
        connection.Schema.Apply(unit);

        var source = new DirectSessionSource(connection, unit);
        var accessor = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IBookmarkStateStore store = new GroundworkV2BookmarkStateStore(source, accessor);

        await store.SaveAsync(Bookmark("wf-1", "bm-a", "HttpEndpoint", "h1"));
        await store.SaveAsync(Bookmark("wf-2", "bm-a", "HttpEndpoint", "h2"));

        Assert.Equal("h1", (await store.FindAsync("wf-1", "bm-a"))!.StimulusHash);
        Assert.Equal("h2", (await store.FindAsync("wf-2", "bm-a"))!.StimulusHash);
        Assert.True(await store.DeleteAsync("wf-1", "bm-a"));
        Assert.Null(await store.FindAsync("wf-1", "bm-a"));
        Assert.Equal("h2", (await store.FindAsync("wf-2", "bm-a"))!.StimulusHash);
    }

    [Fact]
    public async Task Composite_identity_at_declared_projection_boundaries_fits_the_admitted_id()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind);
        connection.Schema.Apply(unit);

        var workflowExecutionId = new string('w', ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength);
        var bookmarkId = new string('b', ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength);
        var source = new DirectSessionSource(connection, unit);
        var accessor = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IBookmarkStateStore store = new GroundworkV2BookmarkStateStore(source, accessor);

        await store.SaveAsync(Bookmark(workflowExecutionId, bookmarkId, "HttpEndpoint", "h1"));

        var physicalId = PhysicalId(workflowExecutionId, bookmarkId);
        Assert.Equal(264, physicalId.Length);
        Assert.True(physicalId.Length <= ElsaRuntimeV2StorageManifest.IdMaximumLength);
        Assert.NotNull(await store.FindAsync(workflowExecutionId, bookmarkId));
    }

    [Fact]
    public async Task Concurrent_save_and_delete_use_compare_and_swap_without_unconditional_fallback()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind);
        var createRaceSession = new InterleavingSession(unit) { FailInsert = true };
        var createRaceStore = new GroundworkV2BookmarkStateStore(
            new InterleavingSessionSource(createRaceSession, unit),
            new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var createException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            createRaceStore.SaveAsync(Bookmark("wf-create", "bm-a", "HttpEndpoint", "h0")).AsTask());

        Assert.Contains("lost a concurrent write; retry", createException.Message, StringComparison.Ordinal);
        Assert.Equal(WritePreconditionKind.CreateOnly, createRaceSession.LastInsertOptions!.Precondition.Kind);

        var session = new InterleavingSession(unit);
        var source = new InterleavingSessionSource(session, unit);
        var accessor = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var store = new GroundworkV2BookmarkStateStore(source, accessor);
        var bookmark = Bookmark("wf-1", "bm-a", "HttpEndpoint", "h1");

        await store.SaveAsync(bookmark);
        session.FailConditionalUpsert = true;
        var saveException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(Bookmark("wf-1", "bm-a", "Timer", "h2")).AsTask());

        Assert.Contains("lost a concurrent write; retry", saveException.Message, StringComparison.Ordinal);
        Assert.Equal(WritePreconditionKind.IfVersion, session.LastConditionalOptions!.Precondition.Kind);
        Assert.Equal(WritePreconditionKind.CreateOnly, session.LastInsertOptions!.Precondition.Kind);
        Assert.False(session.UnconditionalUpsertCalled);

        session.FailDelete = true;
        Assert.False(await store.DeleteAsync("wf-1", "bm-a"));
        Assert.Equal(WritePreconditionKind.IfVersion, session.LastDeleteOptions!.Precondition.Kind);
        Assert.False(session.UnconditionalDeleteCalled);
        Assert.Equal("h1", (await store.FindAsync("wf-1", "bm-a"))!.StimulusHash);
    }

    [Fact]
    public async Task Stimulus_pages_are_deterministic_and_use_the_declared_lookup_projections()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind);
        connection.Schema.Apply(unit);

        var requests = new List<QueryRequest>();
        var source = new RecordingSessionSource(connection, unit, requests);
        var accessor = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var store = new GroundworkV2BookmarkStateStore(source, accessor);

        var first = await store.ListByStimulusPageAsync(
            new BookmarkStimulusPageQuery("HttpEndpoint", "h1", limit: 7));
        Assert.Empty(first.Items);
        var query = Assert.Single(requests);
        Assert.Equal(7, query.Paging.Limit);
        Assert.Equal(
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, ElsaRuntimeV2StorageManifest.BookmarkIdField],
            query.Order.Select(term => term.Column.Name));
        var equality = Assert.IsType<Predicate.Equal>(query.Where);
        Assert.Equal(ElsaRuntimeV2StorageManifest.StimulusLookupKeyField, equality.Column.Name);
        Assert.Equal(QueryType.String, equality.Value.Type);
        Assert.Equal(64, Assert.IsType<string>(equality.Value.Value).Length);
        AssertColumnMatchesManifest(unit, query.Order[0].Column);
        AssertColumnMatchesManifest(unit, query.Order[1].Column);
        AssertColumnMatchesManifest(unit, equality.Column);

        requests.Clear();
        await store.ListByStimulusTypePageAsync(
            new BookmarkStimulusTypePageQuery("HttpEndpoint", limit: 3));
        query = Assert.Single(requests);
        Assert.Equal(3, query.Paging.Limit);
        equality = Assert.IsType<Predicate.Equal>(query.Where);
        Assert.Equal(ElsaRuntimeV2StorageManifest.StimulusTypeLookupKeyField, equality.Column.Name);
        Assert.Equal(64, Assert.IsType<string>(equality.Value.Value).Length);
        AssertColumnMatchesManifest(unit, equality.Column);
    }

    [Fact]
    public async Task Save_writes_checkpoint_compatible_canonical_content_and_projections()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind);
        connection.Schema.Apply(unit);

        var source = new DirectSessionSource(connection, unit);
        var accessor = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var bookmark = Bookmark("wf-1", "bm-a", "HttpEndpoint", "h1");
        var store = new GroundworkV2BookmarkStateStore(source, accessor);

        await store.SaveAsync(bookmark);

        var entry = source.Open(
            unit.Id.Value,
            StorageAccess.Scoped(new StorageScope("tenant-a"))).Read(
                GroundworkRuntimeRowStore.Key(
                    PhysicalId(bookmark.WorkflowExecutionId, bookmark.BookmarkId)));
        Assert.NotNull(entry);
        Assert.Equal(ElsaRuntimeV2StorageManifest.SchemaVersion, ReadString(entry!, ElsaRuntimeV2StorageManifest.SchemaVersionField));
        var content = ReadJsonText(entry, ElsaRuntimeV2StorageManifest.ContentField);
        var roundTrip = JsonSerializer.Deserialize<BookmarkState>(content, Json);
        Assert.NotNull(roundTrip);
        Assert.Equal(bookmark.BookmarkId, roundTrip!.BookmarkId);
        Assert.Equal(bookmark.WorkflowExecutionId, roundTrip.WorkflowExecutionId);
        Assert.Equal(bookmark.StimulusType, roundTrip.StimulusType);
        Assert.Equal(bookmark.StimulusHash, roundTrip.StimulusHash);
        Assert.Equal(bookmark.WorkflowExecutionId, ReadString(entry, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField));
        Assert.Equal(bookmark.BookmarkId, ReadString(entry, ElsaRuntimeV2StorageManifest.BookmarkIdField));
        Assert.Equal(64, ReadString(entry, ElsaRuntimeV2StorageManifest.StimulusLookupKeyField).Length);
        Assert.Equal(64, ReadString(entry, ElsaRuntimeV2StorageManifest.StimulusTypeLookupKeyField).Length);
    }

    private static BookmarkState Bookmark(
        string workflowExecutionId,
        string bookmarkId,
        string stimulusType,
        string stimulusHash)
    {
        return new BookmarkState(
            bookmarkId,
            workflowExecutionId,
            "activity-1",
            "node-1",
            "resume-1",
            stimulusType,
            stimulusHash,
            JsonSerializer.SerializeToElement(new { url = "/orders" }),
            new Dictionary<string, string> { ["tag"] = "v2" },
            new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero),
            null);
    }

    private static string PhysicalId(string workflowExecutionId, string bookmarkId) =>
        $"{workflowExecutionId.Length}:{workflowExecutionId}{bookmarkId.Length}:{bookmarkId}";

    private static string ReadString(StoredEntry entry, string field) =>
        entry.Values.Values[field] switch
        {
            string value => value,
            JsonElement { ValueKind: JsonValueKind.String } value => value.GetString()!,
            _ => throw new InvalidOperationException($"Field '{field}' was not a string.")
        };

    private static string ReadJsonText(StoredEntry entry, string field) =>
        entry.Values.Values[field] switch
        {
            string value => value,
            JsonElement value => value.GetRawText(),
            JsonDocument value => value.RootElement.GetRawText(),
            _ => throw new InvalidOperationException($"Field '{field}' was not JSON.")
        };

    private static void AssertColumnMatchesManifest(StorageUnit unit, ColumnRef column)
    {
        var definition = unit.Columns.Single(candidate => candidate.Name == column.Name);
        Assert.Equal(definition.IsNullable, column.IsNullable);
        Assert.Equal(definition.MaxLength, column.MaxLength);
    }

    private sealed class TestAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class DirectSessionSource(IStorageProviderConnection connection, StorageUnit unit) : IGroundworkStorageSessionSource
    {
        private readonly Dictionary<StorageAccess, IStorageSession> sessions = [];

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            if (sessions.TryGetValue(access, out var session))
                return session;

            session = connection.OpenSession(unit, access);
            sessions.Add(access, session);
            return session;
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return unit;
        }
    }

    private sealed class RecordingSessionSource(
        IStorageProviderConnection connection,
        StorageUnit unit,
        ICollection<QueryRequest> requests) : IGroundworkStorageSessionSource
    {
        private readonly Dictionary<StorageAccess, IStorageSession> sessions = [];

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            if (sessions.TryGetValue(access, out var session))
                return session;

            session = new RecordingSession(connection.OpenSession(unit, access), requests);
            sessions.Add(access, session);
            return session;
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return unit;
        }
    }

    private sealed class RecordingSession(IStorageSession inner, ICollection<QueryRequest> requests) : SynchronousStorageSessionTestDouble, IStorageSession
    {
        public StorageUnit Unit => inner.Unit;
        public StorageAccess Access => inner.Access;
        public StoredEntry? Read(StorageKey key) => inner.Read(key);

        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
        {
            requests.Add(request);
            return inner.Query(request, options);
        }

        public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);
    }

    private sealed class InterleavingSessionSource(InterleavingSession session, StorageUnit unit) : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            Assert.Equal("tenant-a", access.Scope!.Value);
            return session;
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return unit;
        }
    }

    private sealed class InterleavingSession(StorageUnit unit) : SynchronousStorageSessionTestDouble, IStorageSession, IConcurrencyStorageSession
    {
        private readonly Dictionary<string, StoredEntry> entries = new(StringComparer.Ordinal);

        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access { get; } = StorageAccess.Scoped(new StorageScope("tenant-a"));
        public bool FailConditionalUpsert { get; set; }
        public bool FailInsert { get; set; }
        public bool FailDelete { get; set; }
        public bool UnconditionalUpsertCalled { get; private set; }
        public bool UnconditionalDeleteCalled { get; private set; }
        public WriteOptions? LastInsertOptions { get; private set; }
        public WriteOptions? LastConditionalOptions { get; private set; }
        public WriteOptions? LastDeleteOptions { get; private set; }

        public StoredEntry? Read(StorageKey key) => entries.GetValueOrDefault(Id(key));

        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) =>
            throw new NotSupportedException();

        public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();

        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null)
        {
            LastInsertOptions = options;
            var id = Id(values);
            if (FailInsert)
                return new WriteOutcome(WriteOutcomeStatus.ConcurrencyConflict, 0);
            if (entries.ContainsKey(id))
                return new WriteOutcome(WriteOutcomeStatus.ConcurrencyConflict, 0);
            entries[id] = new StoredEntry(values, 1);
            return new WriteOutcome(WriteOutcomeStatus.Inserted, 1);
        }

        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) =>
            throw new NotSupportedException();

        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null)
        {
            UnconditionalUpsertCalled = options?.Precondition.Kind is WritePreconditionKind.Unconditional;
            return new WriteOutcome(WriteOutcomeStatus.Upserted, 1);
        }

        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null)
        {
            LastDeleteOptions = options;
            UnconditionalDeleteCalled = options?.Precondition.Kind is WritePreconditionKind.Unconditional;
            if (FailDelete)
                return new WriteOutcome(WriteOutcomeStatus.ConcurrencyConflict, 0);
            entries.Remove(Id(key));
            return new WriteOutcome(WriteOutcomeStatus.Deleted, 1);
        }

        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) =>
            throw new NotSupportedException();

        public WriteOutcome ConditionalUpsert(StorageValues values, WriteOptions? options = null)
        {
            LastConditionalOptions = options;
            if (FailConditionalUpsert)
                return new WriteOutcome(WriteOutcomeStatus.ConcurrencyConflict, 0);
            var id = Id(values);
            var current = entries[id];
            entries[id] = new StoredEntry(values, current.Version.GetValueOrDefault() + 1);
            return new WriteOutcome(WriteOutcomeStatus.Updated, 1);
        }

        private static string Id(StorageKey key) =>
            (string)key.Values[ElsaRuntimeV2StorageManifest.IdField]!;

        private static string Id(StorageValues values) =>
            (string)values.Values[ElsaRuntimeV2StorageManifest.IdField]!;
    }

    private sealed class NativeProviderRuntime : IAsyncDisposable
    {
        private readonly string path;

        private NativeProviderRuntime(string path) => this.path = path;

        public static NativeProviderRuntime Create() =>
            new(Path.Combine(Path.GetTempPath(), $"elsa-runtime-bookmark-{Guid.NewGuid():N}.db"));

        public IStorageProviderConnection OpenConnection() =>
            new SqliteProviderFactory().Create($"Data Source={path}");

        public ValueTask DisposeAsync()
        {
            foreach (var candidate in new[] { path, $"{path}-shm", $"{path}-wal" })
                if (File.Exists(candidate))
                    File.Delete(candidate);

            return ValueTask.CompletedTask;
        }
    }
}
