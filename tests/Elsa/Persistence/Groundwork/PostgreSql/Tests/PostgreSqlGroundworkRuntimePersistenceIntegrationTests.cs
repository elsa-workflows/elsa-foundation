using System.Text.Json;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Dashboard.Persistence.Groundwork;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.PostgreSql.Unified;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using global::Groundwork.Documents.Store;
using global::Groundwork.PostgreSql.Documents;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Elsa.Persistence.Groundwork.PostgreSql.Tests;

/// <summary>
/// End-to-end proof that <see cref="PostgreSqlGroundworkRuntimePersistenceShellFeature"/> backs the runtime
/// persistence seams with a real PostgreSQL database. Composed exactly as a host would, it materializes the
/// runtime manifest into the container's database and persists runtime state durably across a fresh host
/// process (a new service provider over the same database). Skips gracefully when Docker is unavailable.
/// </summary>
[Collection(PostgresContainerCollection.Name)]
public sealed class PostgreSqlGroundworkRuntimePersistenceIntegrationTests(PostgresContainerFixture fixture)
{
    [SkippableFact]
    public async Task Composed_feature_wires_a_postgresql_document_store()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");

        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        var services = new ServiceCollection();
        new PostgreSqlGroundworkRuntimePersistenceShellFeature { ConnectionString = connectionString }.ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();
        // Drive the startup initializer, as a host would, so the holder is populated and IDocumentStore resolves.
        await provider.InitializeGroundworkStoreAsync();

        Assert.IsType<PostgreSqlDocumentStore>(provider.GetRequiredService<IDocumentStore>());
        Assert.IsType<GroundworkBookmarkStateStore>(provider.GetRequiredService<IBookmarkStateStore>());
        Assert.IsType<GroundworkRuntimeCheckpointWriter>(provider.GetRequiredService<IRuntimeCheckpointCommitStore>());
        Assert.IsType<GroundworkRuntimePostCommitOutboxStore>(provider.GetRequiredService<IRuntimePostCommitOutboxStore>());
        Assert.IsType<GroundworkWorkflowSchedulerWorkQueue>(provider.GetRequiredService<IWorkflowSchedulerWorkQueue>());
    }

    [SkippableFact]
    public async Task Composed_feature_persists_runtime_state_across_a_restart()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");

        // One isolated database, reused by two independent host processes below.
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();

        // First host process: compose the feature exactly as a host would, then persist through a resolved seam.
        await using (var provider = await BuildComposedProviderAsync(connectionString))
        {
            var bookmarks = provider.GetRequiredService<IBookmarkStateStore>();
            await bookmarks.SaveAsync(Bookmark("wf-1", "bm-1"));
        }

        // Second host process: a fresh container over the same database. State read back was genuinely durable.
        await using (var provider = await BuildComposedProviderAsync(connectionString))
        {
            var bookmarks = provider.GetRequiredService<IBookmarkStateStore>();
            Assert.NotNull(await bookmarks.FindAsync("wf-1", "bm-1"));
        }
    }

    [SkippableFact]
    public async Task Dashboard_adapter_pages_exactly_through_postgresql()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");

        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var provider = await BuildComposedProviderAsync(connectionString);
        var executionStore = provider.GetRequiredService<IWorkflowExecutionStateStore>();
        for (var index = 0; index < 125; index++)
            await executionStore.SaveAsync(Execution(index));

        var source = new GroundworkWorkflowRunHealthDataSource(
            () => new NpgsqlConnection(connectionString),
            GroundworkRunHealthDialect.PostgreSql);
        var snapshot = await new WorkflowRunHealthService(source).QueryAsync(new(
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddDays(1),
            "Etc/UTC",
            WorkflowRunHealthBucketSize.Day,
            "tenant-a"));

        Assert.Equal(125, snapshot.StartedCount);
    }

    [SkippableFact]
    public async Task Dashboard_portfolio_aggregates_exactly_through_postgresql()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var provider = await BuildUnifiedProviderAsync(connectionString);
        var store = provider.GetRequiredService<IDocumentStore>();
        for (var index = 0; index < 105; index++)
            await SaveDefinitionAsync(store, PortfolioDefinition(index));
        for (var index = 0; index < 30; index++)
            await SaveDraftAsync(store, PortfolioDraft(index));
        var references = provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
        for (var index = 0; index < 50; index++)
            await references.SaveAsync(PortfolioReference(index));
        var source = new GroundworkWorkflowPortfolioDataSource(
            () => new NpgsqlConnection(connectionString),
            GroundworkRunHealthDialect.PostgreSql,
            new FakePayloadSerializer());

        var counts = await source.QueryBaseCountsAsync("tenant-a", DateTimeOffset.UtcNow);
        var draftCount = 0;
        await foreach (var _ in source.StreamCurrentDraftsAsync("tenant-a"))
            draftCount++;

        Assert.Equal(new WorkflowPortfolioBaseCounts(105, 50, 30), counts);
        Assert.Equal(30, draftCount);
    }

    private static async Task<ServiceProvider> BuildComposedProviderAsync(string connectionString)
    {
        var services = new ServiceCollection();
        new PostgreSqlGroundworkRuntimePersistenceShellFeature { ConnectionString = connectionString }.ConfigureServices(services);
        var provider = services.BuildServiceProvider();
        await provider.InitializeGroundworkStoreAsync();
        return provider;
    }

    private static async Task<ServiceProvider> BuildUnifiedProviderAsync(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPayloadSerializer, FakePayloadSerializer>();
        new PostgreSqlGroundworkUnifiedPersistenceShellFeature { ConnectionString = connectionString }.ConfigureServices(services);
        var provider = services.BuildServiceProvider();
        await provider.InitializeGroundworkStoreAsync();
        return provider;
    }

    private static BookmarkState Bookmark(string workflowExecutionId, string bookmarkId) => new(
        BookmarkId: bookmarkId,
        WorkflowExecutionId: workflowExecutionId,
        ActivityExecutionId: "ae-1",
        ExecutableNodeId: "node-1",
        ResumeTargetId: "resume-1",
        StimulusType: "delivery-status",
        StimulusHash: "sha256:stimulus",
        Payload: null,
        Metadata: new Dictionary<string, string>(),
        CreatedAt: DateTimeOffset.UnixEpoch,
        ExpiresAt: null);

    private static WorkflowExecutionState Execution(int index)
    {
        var startedAt = DateTimeOffset.UnixEpoch.AddMinutes(index);
        return new(
            $"dashboard-run-{index}",
            new WorkflowExecutableIdentity($"artifact-{index}", "definition", $"version-{index}", "1", "hash"),
            WorkflowExecutionStatus.Completed,
            null,
            startedAt,
            startedAt,
            startedAt.AddMinutes(1),
            startedAt.AddMinutes(1),
            null,
            null,
            "tenant-a",
            new Dictionary<string, string>());
    }

    private static WorkflowDefinition PortfolioDefinition(int index) => new()
    {
        Id = $"portfolio-definition-{index}", TenantId = "tenant-a", Name = $"Definition {index}",
        CreatedAt = DateTimeOffset.UnixEpoch, LastModifiedAt = DateTimeOffset.UnixEpoch
    };

    private static WorkflowDefinitionDraft PortfolioDraft(int index) => new()
    {
        Id = $"portfolio-draft-{index}", WorkflowDefinitionId = $"portfolio-definition-{index}", TenantId = "tenant-a",
        State = WorkflowDefinitionState.Empty, CreatedAt = DateTimeOffset.UnixEpoch, LastModifiedAt = DateTimeOffset.UnixEpoch
    };

    private static WorkflowExecutableSourceReference PortfolioReference(int index) => new(
        $"portfolio-reference-{index}", $"portfolio-artifact-{index}", "WorkflowDefinitionVersion", $"version-{index}", "1",
        $"portfolio-definition-{index}", $"version-{index}", "1", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        WorkflowExecutableReferenceScope.Published);

    private static async Task SaveDefinitionAsync(IDocumentStore store, WorkflowDefinition definition) => await store.SaveAsync(new(
        WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, definition.Id, WorkflowsDesignStorageManifest.SchemaVersion,
        JsonSerializer.Serialize(new DefinitionDocument(WorkflowsDesignStorageManifest.WorkflowDefinitionCollection, definition), GroundworkDesignJson.Options)));

    private static async Task SaveDraftAsync(IDocumentStore store, WorkflowDefinitionDraft draft)
    {
        var serializer = new FakePayloadSerializer();
        await store.SaveAsync(new(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, draft.Id, WorkflowsDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(new DraftDocument(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection, draft, []),
                GroundworkDesignDocumentSerialization.Create(serializer))));
    }

    private sealed record DefinitionDocument(string Collection, WorkflowDefinition Entity);
    private sealed record DraftDocument(string Collection, WorkflowDefinitionDraft Entity, IReadOnlyCollection<DesignMetadataRecord> Layout);

    private sealed class FakePayloadSerializer : IPayloadSerializer
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public string Serialize(object payload) => JsonSerializer.Serialize(payload, Options);
        public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, Options);
        public object Deserialize(string serializedData) => JsonSerializer.Deserialize<object>(serializedData, Options)!;
        public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type, Options)!;
        public object Deserialize(JsonElement serializedData) => serializedData.Deserialize<object>(Options)!;
        public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData, Options)!;
        public T Deserialize<T>(JsonElement serializedData) => serializedData.Deserialize<T>(Options)!;
        public JsonSerializerOptions GetOptions() => Options;
    }
}
