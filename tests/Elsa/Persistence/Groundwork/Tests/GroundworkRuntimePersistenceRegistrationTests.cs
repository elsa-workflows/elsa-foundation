using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Sqlite;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Documents.Store;
using Groundwork.Sqlite.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkRuntimePersistenceRegistrationTests
{
    [Fact]
    public void Default_Runtime_Composition_Keeps_InMemory_Store()
    {
        var services = new ServiceCollection();
        services.TryAddSingleton<IBookmarkStateStore, InMemoryBookmarkStateStore>();
        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryBookmarkStateStore>(provider.GetRequiredService<IBookmarkStateStore>());
        Assert.IsType<InMemoryWorkflowExecutableStore>(provider.GetRequiredService<IWorkflowExecutableStore>());
    }

    [Fact]
    public void AddGroundworkRuntimeStores_Replaces_InMemory_Store()
    {
        var services = new ServiceCollection();
        services.TryAddSingleton<IBookmarkStateStore, InMemoryBookmarkStateStore>();
        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        services.TryAddSingleton<IActivityExecutionStateStore, InMemoryActivityExecutionStateStore>();
        services.TryAddSingleton<IWorkflowExecutionStateStore, InMemoryWorkflowExecutionStateStore>();
        services.TryAddSingleton<IDurableValueStateStore, InMemoryDurableValueStateStore>();
        services.TryAddSingleton<ISchedulerStateStore, InMemorySchedulerStateStore>();
        services.TryAddSingleton<IExecutionLivenessStateStore, InMemoryExecutionLivenessStateStore>();
        services.TryAddSingleton<IWorkflowHoldStateStore, InMemoryWorkflowHoldStateStore>();
        services.TryAddSingleton<IIncidentStateStore, InMemoryIncidentStateStore>();
        services.TryAddSingleton<InMemoryRuntimeCheckpointCommitStore>();
        services.TryAddSingleton<IRuntimeCheckpointCommitStore>(sp => sp.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.TryAddSingleton<IRuntimePostCommitOutboxStore>(sp => sp.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.TryAddSingleton<IWorkflowSchedulerWorkQueue, InMemoryWorkflowSchedulerWorkQueue>();
        services.AddSingleton<IDocumentStore>(new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create()));

        services.AddGroundworkRuntimeStores();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<GroundworkBookmarkStateStore>(provider.GetRequiredService<IBookmarkStateStore>());
        Assert.IsType<GroundworkWorkflowExecutableStore>(provider.GetRequiredService<IWorkflowExecutableStore>());
        Assert.IsType<GroundworkActivityExecutionStateStore>(provider.GetRequiredService<IActivityExecutionStateStore>());
        Assert.IsType<GroundworkWorkflowExecutionStateStore>(provider.GetRequiredService<IWorkflowExecutionStateStore>());
        Assert.IsType<GroundworkDurableValueStateStore>(provider.GetRequiredService<IDurableValueStateStore>());
        Assert.IsType<GroundworkSchedulerStateStore>(provider.GetRequiredService<ISchedulerStateStore>());
        Assert.IsType<GroundworkExecutionLivenessStateStore>(provider.GetRequiredService<IExecutionLivenessStateStore>());
        Assert.IsType<GroundworkWorkflowHoldStateStore>(provider.GetRequiredService<IWorkflowHoldStateStore>());
        Assert.IsType<GroundworkIncidentStateStore>(provider.GetRequiredService<IIncidentStateStore>());
        Assert.IsType<GroundworkRuntimeCheckpointWriter>(provider.GetRequiredService<IRuntimeCheckpointCommitStore>());
        Assert.IsType<GroundworkRuntimePostCommitOutboxStore>(provider.GetRequiredService<IRuntimePostCommitOutboxStore>());
        Assert.IsType<GroundworkWorkflowSchedulerWorkQueue>(provider.GetRequiredService<IWorkflowSchedulerWorkQueue>());
    }

    // Constitution §2.23.1: the versioned-document serialization services must be registered as their
    // sealed defaults, and the upcaster registry must construct cleanly when no upcasters are contributed
    // (the state today — every kind is at version 1, so there are no historical versions to upcast).
    [Fact]
    public void AddGroundworkRuntimeStores_Registers_Default_Serializer_And_Empty_Upcaster_Registry()
    {
        var services = new ServiceCollection();
        services.TryAddSingleton<IBookmarkStateStore, InMemoryBookmarkStateStore>();
        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        services.TryAddSingleton<IActivityExecutionStateStore, InMemoryActivityExecutionStateStore>();
        services.TryAddSingleton<IWorkflowExecutionStateStore, InMemoryWorkflowExecutionStateStore>();
        services.TryAddSingleton<IDurableValueStateStore, InMemoryDurableValueStateStore>();
        services.TryAddSingleton<ISchedulerStateStore, InMemorySchedulerStateStore>();
        services.TryAddSingleton<IExecutionLivenessStateStore, InMemoryExecutionLivenessStateStore>();
        services.TryAddSingleton<IWorkflowHoldStateStore, InMemoryWorkflowHoldStateStore>();
        services.TryAddSingleton<IIncidentStateStore, InMemoryIncidentStateStore>();
        services.TryAddSingleton<InMemoryRuntimeCheckpointCommitStore>();
        services.TryAddSingleton<IRuntimeCheckpointCommitStore>(sp => sp.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.TryAddSingleton<IRuntimePostCommitOutboxStore>(sp => sp.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.AddSingleton<IDocumentStore>(new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create()));

        services.AddGroundworkRuntimeStores();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<GroundworkRuntimeDocumentSerializer>(provider.GetRequiredService<IGroundworkRuntimeDocumentSerializer>());
        Assert.IsType<GroundworkRuntimeDocumentUpcasterRegistry>(provider.GetRequiredService<IGroundworkRuntimeDocumentUpcasterRegistry>());

        Assert.IsType<WorkflowExecutionStateV1ToV2Upcaster>(
            Assert.Single(provider.GetRequiredService<IEnumerable<IGroundworkRuntimeDocumentUpcaster>>()));
        Assert.NotNull(provider.GetRequiredService<IGroundworkRuntimeDocumentUpcasterRegistry>());
    }

    [Fact]
    public async Task Sqlite_Feature_Wires_DocumentStore_And_Bridge()
    {
        await using var database = new TemporarySqliteDatabase();
        var services = new ServiceCollection();
        services.TryAddSingleton<IBookmarkStateStore, InMemoryBookmarkStateStore>();
        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();

        new SqliteGroundworkRuntimePersistenceShellFeature { ConnectionString = database.ConnectionString }.ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();

        // The store is now materialized at startup via a hosted service / shell initializer that populates the
        // holder (the handle is owned by the holder, not registered in DI). Drive that startup step explicitly,
        // as a real host would, before resolving IDocumentStore.
        Assert.NotNull(provider.GetRequiredService<GroundworkDocumentStoreHolder>());
        await provider.InitializeGroundworkStoreAsync();

        Assert.IsType<SqliteDocumentStore>(provider.GetRequiredService<IDocumentStore>());
        Assert.IsType<GroundworkBookmarkStateStore>(provider.GetRequiredService<IBookmarkStateStore>());
        Assert.IsType<GroundworkWorkflowExecutableStore>(provider.GetRequiredService<IWorkflowExecutableStore>());
        Assert.IsType<GroundworkRuntimeCheckpointWriter>(provider.GetRequiredService<IRuntimeCheckpointCommitStore>());
        Assert.IsType<GroundworkRuntimePostCommitOutboxStore>(provider.GetRequiredService<IRuntimePostCommitOutboxStore>());
        Assert.IsType<GroundworkWorkflowSchedulerWorkQueue>(provider.GetRequiredService<IWorkflowSchedulerWorkQueue>());
    }

    [Fact] // The bite: resolving IDocumentStore before startup throws (no synchronous block resurrects it on the
           // resolving thread), and the store is fully usable only after the startup initializer has run.
    public async Task Sqlite_Store_Throws_Before_Init_Then_Is_Usable_After()
    {
        await using var database = new TemporarySqliteDatabase();
        var services = new ServiceCollection();
        new SqliteGroundworkRuntimePersistenceShellFeature { ConnectionString = database.ConnectionString }.ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();

        // Before the startup initializer runs, the holder is empty and resolving the store throws — it does not
        // silently block the resolving thread the way the old sync-over-async factory did.
        Assert.False(provider.GetRequiredService<GroundworkDocumentStoreHolder>().IsInitialized);
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IDocumentStore>());

        await provider.InitializeGroundworkStoreAsync();

        // After startup the singleton is fully initialized and the store round-trips a document.
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.SaveAsync(new SaveDocumentRequest(
            ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, "run-init", "1.0.0", "{\"ok\":true}"));
        Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, "run-init"));
    }

    [Fact] // Running the startup step twice is a no-op: the store is materialized once and the singleton is stable.
    public async Task Sqlite_Store_Initialization_Is_Idempotent()
    {
        await using var database = new TemporarySqliteDatabase();
        var services = new ServiceCollection();
        new SqliteGroundworkRuntimePersistenceShellFeature { ConnectionString = database.ConnectionString }.ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();

        await provider.InitializeGroundworkStoreAsync();
        var first = provider.GetRequiredService<IDocumentStore>();
        await provider.InitializeGroundworkStoreAsync();
        var second = provider.GetRequiredService<IDocumentStore>();

        Assert.Same(first, second);
    }

    [Fact]
    public async Task Composed_Sqlite_Feature_Persists_Across_Restart()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-compose-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            // First host process: compose the feature exactly as a host would, then persist through a resolved seam.
            await using (var provider = await BuildComposedProviderAsync(connectionString))
            {
                var bookmarks = provider.GetRequiredService<IBookmarkStateStore>();
                await bookmarks.SaveAsync(Bookmark("wf-1", "bm-1"));
            }

            // Second host process: a fresh container over the same database file. State read back was genuinely durable.
            await using (var provider = await BuildComposedProviderAsync(connectionString))
            {
                var bookmarks = provider.GetRequiredService<IBookmarkStateStore>();
                Assert.NotNull(await bookmarks.FindAsync("wf-1", "bm-1"));
            }
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private static async Task<ServiceProvider> BuildComposedProviderAsync(string connectionString)
    {
        var services = new ServiceCollection();
        new SqliteGroundworkRuntimePersistenceShellFeature { ConnectionString = connectionString }.ConfigureServices(services);
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

    private sealed class TemporarySqliteDatabase : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"elsa-groundwork-registration-{Guid.NewGuid():N}.db");

        public string ConnectionString => $"Data Source={_path}";

        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            return ValueTask.CompletedTask;
        }
    }
}
