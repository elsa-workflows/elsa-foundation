using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;
using System.Text.Json;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2ExecutableActivityTemplateStoreTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Providers => new() { "sqlite", "postgresql", "sqlserver", "mongodb" };

    [Fact]
    public async Task Sqlite_current_template_vertical_round_trips_replays_collides_pages_and_deletes()
    {
        await using var fixture = NativeFixture.Create("sqlite", null);
        await RunCoreBehaviorAsync(fixture);
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Native_provider_current_template_vertical(string providerName)
    {
        var connectionString = providerName == "sqlite"
            ? null
            : Environment.GetEnvironmentVariable($"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING");
        RequireOrSkip(providerName != "sqlite" && string.IsNullOrWhiteSpace(connectionString),
            $"Set GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING to run the {providerName} proof.");
        await using var fixture = NativeFixture.Create(providerName, connectionString);
        RequireOrSkip(!fixture.Connection.Capabilities.Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit)),
            $"The {providerName} provider does not advertise atomic commit.");
        await RunCoreBehaviorAsync(fixture);
    }

    [Fact]
    public async Task Save_reconciles_a_concurrent_hash_winner_without_overwriting()
    {
        await using var fixture = NativeFixture.Create("sqlite", null);
        var competingStore = fixture.Store("tenant-a");
        var interceptingSource = new InterleavingSource(fixture.Source);
        IExecutableActivityTemplateStore store = new GroundworkV2ExecutableActivityTemplateStore(
            interceptingSource,
            fixture.Access("tenant-a"));
        interceptingSource.BeforeBegin = () => competingStore.SaveAsync(
            Template("template-winner", "sha256:race", "winner-node")).GetAwaiter().GetResult();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(Template("template-loser", "sha256:race", "loser-node")).AsTask());

        Assert.Contains("already bound to id 'template-winner'", exception.Message, StringComparison.Ordinal);
        Assert.Equal("template-winner", (await competingStore.FindByHashAsync("sha256:race"))!.TemplateId);
        Assert.Null(await competingStore.FindAsync("template-loser"));
        Assert.Null(fixture.ReadTemplate("template-loser"));
    }

    [Fact]
    public async Task Delete_reconciles_a_successor_claim_and_never_removes_it()
    {
        await using var fixture = NativeFixture.Create("sqlite", null);
        var competingStore = fixture.Store("tenant-a");
        await competingStore.SaveAsync(Template("template-original", "sha256:successor", "original-node"));
        var interceptingSource = new InterleavingSource(fixture.Source);
        IExecutableActivityTemplateStore store = new GroundworkV2ExecutableActivityTemplateStore(
            interceptingSource,
            fixture.Access("tenant-a"));
        interceptingSource.BeforeBegin = () =>
        {
            Assert.True(competingStore.DeleteAsync("template-original").GetAwaiter().GetResult());
            competingStore.SaveAsync(Template("template-successor", "sha256:successor", "successor-node"))
                .GetAwaiter().GetResult();
        };

        Assert.False(await store.DeleteAsync("template-original"));
        Assert.Equal("template-successor", (await competingStore.FindByHashAsync("sha256:successor"))!.TemplateId);
        Assert.NotNull(await competingStore.FindAsync("template-successor"));
        Assert.NotNull(fixture.ReadClaim("sha256:successor"));
    }

    [Fact]
    public async Task FindByHash_detects_duplicate_rows_with_a_bounded_take_two_query()
    {
        await using var fixture = NativeFixture.Create("sqlite", null);
        var first = Template("template-duplicate-a", "sha256:duplicate", "node-a");
        var second = Template("template-duplicate-b", "sha256:duplicate", "node-b");
        var session = fixture.OpenTemplate("tenant-a");
        Assert.Equal(WriteOutcomeStatus.Inserted,
            session.Insert(GroundworkV2ExecutableActivityTemplateStorageConventions.Values(first), WriteOptions.CreateOnly).Status);
        Assert.Equal(WriteOutcomeStatus.Inserted,
            session.Insert(GroundworkV2ExecutableActivityTemplateStorageConventions.Values(second), WriteOptions.CreateOnly).Status);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store("tenant-a").FindByHashAsync("sha256:duplicate").AsTask());

        Assert.Contains("more than one stored template", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, fixture.Source.LastQuery?.Paging.Limit);
    }

    [Fact]
    public async Task Current_rows_reject_schema_projection_and_physical_identity_drift()
    {
        await using var fixture = NativeFixture.Create("sqlite", null);
        var template = Template("template-validation", "sha256:validation", "node-validation");
        var values = GroundworkV2ExecutableActivityTemplateStorageConventions.Values(template).Values;
        var store = fixture.Store("tenant-a");

        var wrongSchema = new Dictionary<string, object?>(values, StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.SchemaVersionField] = "0.9.0"
        };
        Assert.Throws<InvalidDataException>(() =>
            GroundworkV2ExecutableActivityTemplateStorageConventions.Deserialize(wrongSchema));

        var wrongProjection = new Dictionary<string, object?>(values, StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.TemplateHashField] = "sha256:forged"
        };
        Assert.Throws<InvalidDataException>(() =>
            GroundworkV2ExecutableActivityTemplateStorageConventions.Deserialize(wrongProjection));

        var wrongPhysicalId = new Dictionary<string, object?>(values, StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.IdField] = "template-forged"
        };
        Assert.Throws<InvalidDataException>(() =>
            GroundworkV2ExecutableActivityTemplateStorageConventions.Deserialize(wrongPhysicalId));

        await store.SaveAsync(template);
        Assert.Equal("template-validation", (await store.FindAsync("template-validation"))!.TemplateId);
    }

    [Fact]
    public async Task Global_access_is_refused_before_provider_io_and_hash_claim_ids_are_injective()
    {
        await using var fixture = NativeFixture.Create("sqlite", null);
        var store = fixture.Store(PersistenceAccessContext.Global);
        fixture.Source.ResetOpenCount();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindAsync("template-global").AsTask());
        Assert.Equal(0, fixture.Source.OpenCount);
        Assert.NotEqual(
            GroundworkV2ExecutableActivityTemplateStorageConventions.HashClaimId("ab"),
            GroundworkV2ExecutableActivityTemplateStorageConventions.HashClaimId("a:b"));
        Assert.Equal(
            "templateHash:5:sha-1",
            GroundworkV2ExecutableActivityTemplateStorageConventions.HashClaimId("sha-1"));
    }

    private static async Task RunCoreBehaviorAsync(NativeFixture fixture)
    {
        var store = fixture.Store("tenant-a");
        var template = Template("template-1", "sha256:one", "node-1");
        await store.SaveAsync(template);

        var byId = await store.FindAsync("template-1");
        var byHash = await store.FindByHashAsync("sha256:one");
        Assert.NotNull(byId);
        Assert.Equal("template-1", byId!.TemplateId);
        Assert.Equal("node-1", byId.Root.ExecutableNodeId);
        Assert.True(byId.NodesById.ContainsKey("node-1"));
        Assert.Equal(new RuntimeRequirement("test.consumer", "1"), Assert.Single(byId.RuntimeRequirements));
        Assert.Equal("sample.external", Assert.Single(byId.StorageDriverRequirements).DriverKey);
        Assert.Equal("template-1", byHash!.TemplateId);

        var envelope = fixture.ReadTemplate("template-1") ?? throw new XunitException("The template row was not persisted.");
        Assert.Equal(ElsaRuntimeV2StorageManifest.SchemaVersion,
            envelope.Values.Values[ElsaRuntimeV2StorageManifest.SchemaVersionField]);
        var root = JsonDocument.Parse(ContentJson(envelope)).RootElement;
        Assert.Equal(ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateDocumentKind,
            root.GetProperty("collection").GetString());
        Assert.Equal("sha256:one", root.GetProperty("templateHash").GetString());
        var persistedTemplate = root.GetProperty("template");
        Assert.False(persistedTemplate.TryGetProperty("nodesById", out _));
        var persistedRoot = persistedTemplate.GetProperty("root");
        Assert.Equal("test.consumer", persistedRoot.GetProperty("descriptorType").GetString());
        Assert.Equal("1", persistedRoot.GetProperty("descriptorSchemaVersion").GetString());
        Assert.Equal("node-1", persistedRoot.GetProperty("descriptorPayload").GetProperty("id").GetString());
        Assert.False(persistedRoot.TryGetProperty("descriptor", out _));

        await store.SaveAsync(Template("template-1", "sha256:one", "node-1", CreatedAt.AddMinutes(1)));
        Assert.Equal(1, fixture.ReadTemplate("template-1")!.Version);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(Template("template-1", "sha256:other", "node-2")).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(Template("template-other", "sha256:one", "node-3")).AsTask());
        var contentCollision = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(Template("template-1", "sha256:one", "different-node")).AsTask());
        Assert.Contains("different content", contentCollision.Message, StringComparison.Ordinal);
        Assert.Equal("node-1", (await store.FindAsync("template-1"))!.Root.ExecutableNodeId);

        await store.SaveAsync(Template("template-2", "sha256:two", "node-2"));
        await store.SaveAsync(Template("template-3", "sha256:three", "node-3"));
        var firstPage = await store.ListPageAsync(new RuntimeStorePageRequest(2));
        Assert.Equal(["template-1", "template-2"], firstPage.Items.Select(item => item.TemplateId));
        Assert.NotNull(firstPage.NextContinuationToken);
        var secondPage = await store.ListPageAsync(new RuntimeStorePageRequest(2, firstPage.NextContinuationToken));
        Assert.Equal(["template-3"], secondPage.Items.Select(item => item.TemplateId));
        Assert.Null(secondPage.NextContinuationToken);

        Assert.True(await store.DeleteAsync("template-1"));
        Assert.Null(await store.FindAsync("template-1"));
        Assert.Null(await store.FindByHashAsync("sha256:one"));
        await store.SaveAsync(Template("template-reused", "sha256:one", "node-reused"));
        Assert.Equal("template-reused", (await store.FindByHashAsync("sha256:one"))!.TemplateId);
        Assert.Equal(BatchWriteOptions.Exact, fixture.Source.LastUnitOfWorkOptions);
        Assert.Equal(
            [
                ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateDocumentKind,
                ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateHashClaimDocumentKind
            ],
            fixture.Source.LastUnitOfWorkLogicalIds);
    }

    private static ExecutableActivityTemplate Template(
        string id,
        string hash,
        string nodeId,
        DateTimeOffset? createdAt = null) => new(
        id,
        hash,
        Node(nodeId),
        new Dictionary<string, WorkflowExecutableResumeTarget>(),
        [],
        [],
        [new RuntimeRequirement("test.consumer", "1")],
        "provider/1",
        new Dictionary<string, string> { ["wire"] = "stable-descriptor" },
        createdAt ?? CreatedAt,
        [new RuntimeStorageDriverRequirement("sample.external")]);

    private static string ContentJson(StoredEntry entry) => entry.Values.Values[ElsaRuntimeV2StorageManifest.ContentField] switch
    {
        string text => text,
        JsonElement element => element.GetRawText(),
        JsonDocument document => document.RootElement.GetRawText(),
        _ => throw new XunitException("The template content was not returned as JSON.")
    };

    private static ExecutableNode Node(string id) => new(
        id,
        $"authored-{id}",
        "test.activity",
        "1",
        new RuntimeActivityDescriptor(
            "test.consumer",
            "1",
            JsonSerializer.SerializeToElement(new { id })),
        new Dictionary<string, RuntimeInputBinding>(),
        new Dictionary<string, RuntimeOutputCapture>(),
        new Dictionary<string, string>());

    private static void RequireOrSkip(bool unavailable, string message)
    {
        if (!unavailable)
            return;
        if (StringComparer.Ordinal.Equals(
                Environment.GetEnvironmentVariable("GROUNDWORK_V2_REQUIRE_NATIVE_PROVIDER_MATRIX"),
                "1"))
        {
            throw new InvalidOperationException($"Required Groundwork v2 native-provider evidence is unavailable: {message}");
        }

        Skip.If(true, message);
    }

    private sealed class NativeFixture : IAsyncDisposable
    {
        private readonly string? sqlitePath;
        private readonly IStorageProviderConnection connection;

        private NativeFixture(
            string? sqlitePath,
            IStorageProviderConnection connection,
            NativeSource source)
        {
            this.sqlitePath = sqlitePath;
            this.connection = connection;
            Source = source;
        }

        public NativeSource Source { get; }

        public IStorageProviderConnection Connection => connection;

        public static NativeFixture Create(string providerName, string? connectionString)
        {
            string? sqlitePath = null;
            if (providerName == "sqlite")
            {
                sqlitePath = Path.Combine(Path.GetTempPath(), $"elsa-v2-template-{Guid.NewGuid():N}.db");
                connectionString = $"Data Source={sqlitePath}";
            }

            var connection = providerName switch
            {
                "sqlite" => new SqliteProviderFactory().Create(connectionString!),
                "postgresql" => new PostgreSqlProviderFactory().Create(connectionString!),
                "sqlserver" => new SqlServerProviderFactory().Create(connectionString!),
                "mongodb" => new MongoProviderFactory().Create(connectionString!),
                _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
            };
            var units = ElsaRuntimeV2StorageManifest.CreateUnits()
                .Where(unit => unit.Id.Value is
                    ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateDocumentKind or
                    ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateHashClaimDocumentKind)
                .ToDictionary(
                    unit => unit.Id.Value,
                    unit => providerName == "sqlite"
                        ? unit
                        : unit with
                        {
                            Id = new StorageUnitId($"{unit.Id.Value}-{Guid.NewGuid():N}"[..42]),
                            Name = $"{unit.Name}_{Guid.NewGuid():N}"[..52]
                        },
                    StringComparer.Ordinal);
            foreach (var unit in units.Values)
                connection.Schema.Apply(unit);
            return new NativeFixture(sqlitePath, connection, new NativeSource(connection, units));
        }

        public GroundworkV2ExecutableActivityTemplateStore Store(string tenant) =>
            new(Source, Access(tenant));

        public GroundworkV2ExecutableActivityTemplateStore Store(PersistenceAccessContext context) =>
            new(Source, new TestAccessContextAccessor(context));

        public StoredEntry? ReadTemplate(string templateId) =>
            OpenTemplate("tenant-a").Read(GroundworkRuntimeRowStore.Key(
                GroundworkV2ExecutableActivityTemplateStorageConventions.PhysicalId(templateId)));

        public StoredEntry? ReadClaim(string hash) =>
            Source.Open(
                    ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateHashClaimDocumentKind,
                    StorageAccess.Scoped(new StorageScope("tenant-a")))
                .Read(GroundworkRuntimeRowStore.Key(
                    GroundworkV2ExecutableActivityTemplateStorageConventions.HashClaimId(hash)));

        public IStorageSession OpenTemplate(string tenant) =>
            Source.Open(
                ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateDocumentKind,
                StorageAccess.Scoped(new StorageScope(tenant)));

        public TestAccessContextAccessor Access(string tenant) =>
            new(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));

        public ValueTask DisposeAsync()
        {
            connection.Dispose();
            if (sqlitePath is not null)
                foreach (var path in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal", $"{sqlitePath}-journal" })
                    if (File.Exists(path))
                        File.Delete(path);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NativeSource(
        IStorageProviderConnection connection,
        IReadOnlyDictionary<string, StorageUnit> units) :
        IGroundworkStorageSessionSource,
        IGroundworkStorageCapabilitySource
    {
        public int OpenCount { get; private set; }
        public QueryRequest? LastQuery { get; private set; }
        public BatchWriteOptions? LastUnitOfWorkOptions { get; private set; }
        public IReadOnlyList<string>? LastUnitOfWorkLogicalIds { get; private set; }

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => connection.Capabilities;

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            OpenCount++;
            return new RecordingSession(connection.OpenSession(Resolve(unitId), access), request => LastQuery = request);
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null)
        {
            LastUnitOfWorkOptions = options;
            LastUnitOfWorkLogicalIds = unitIds
                .Select(id => units.Single(pair => StringComparer.Ordinal.Equals(pair.Value.Id.Value, id)).Key)
                .ToArray();
            return connection.BeginUnitOfWork(access, options, unitIds.Select(Resolve).ToArray());
        }

        public StorageUnit Unit(string unitId, string? targetName = null) => units[unitId];

        public void ResetOpenCount() => OpenCount = 0;

        private StorageUnit Resolve(string unitId) =>
            units.TryGetValue(unitId, out var logical)
                ? logical
                : units.Values.Single(unit => StringComparer.Ordinal.Equals(unit.Id.Value, unitId));
    }

    private sealed class InterleavingSource(NativeSource inner) :
        IGroundworkStorageSessionSource,
        IGroundworkStorageCapabilitySource
    {
        public Action? BeforeBegin { get; set; }

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => inner.Capabilities(targetName);

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            inner.Open(unitId, access, targetName);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null)
        {
            var callback = BeforeBegin;
            BeforeBegin = null;
            callback?.Invoke();
            return inner.BeginUnitOfWork(access, options, unitIds, targetName);
        }

        public StorageUnit Unit(string unitId, string? targetName = null) => inner.Unit(unitId, targetName);
    }

    private sealed class RecordingSession(IStorageSession inner, Action<QueryRequest> recordQuery) : IStorageSession
    {
        public StorageUnit Unit => inner.Unit;
        public StorageAccess Access => inner.Access;

        public StoredEntry? Read(StorageKey key) => inner.Read(key);

        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
        {
            recordQuery(request);
            return inner.Query(request, options);
        }

        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);
        public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
    }

    private sealed class TestAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
