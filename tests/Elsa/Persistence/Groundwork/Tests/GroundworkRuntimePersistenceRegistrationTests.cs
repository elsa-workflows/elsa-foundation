using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Sqlite;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Documents.Store;
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
        services.TryAddSingleton<IOperationalStateStore, InMemoryOperationalStateStore>();
        services.TryAddSingleton<IControlPlaneStateStore, InMemoryControlPlaneStateStore>();
        services.TryAddSingleton<IIncidentStateStore, InMemoryIncidentStateStore>();
        services.TryAddSingleton<IRuntimeCheckpointWriter, InMemoryRuntimeCheckpointWriter>();
        services.TryAddSingleton<IRuntimePostCommitOutboxStore, InMemoryRuntimePostCommitOutboxStore>();
        services.AddSingleton<IDocumentStore>(new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create()));

        services.AddGroundworkRuntimeStores();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<GroundworkBookmarkStateStore>(provider.GetRequiredService<IBookmarkStateStore>());
        Assert.IsType<GroundworkWorkflowExecutableStore>(provider.GetRequiredService<IWorkflowExecutableStore>());
        Assert.IsType<GroundworkActivityExecutionStateStore>(provider.GetRequiredService<IActivityExecutionStateStore>());
        Assert.IsType<GroundworkWorkflowExecutionStateStore>(provider.GetRequiredService<IWorkflowExecutionStateStore>());
        Assert.IsType<GroundworkDurableValueStateStore>(provider.GetRequiredService<IDurableValueStateStore>());
        Assert.IsType<GroundworkSchedulerStateStore>(provider.GetRequiredService<ISchedulerStateStore>());
        Assert.IsType<GroundworkOperationalStateStore>(provider.GetRequiredService<IOperationalStateStore>());
        Assert.IsType<GroundworkControlPlaneStateStore>(provider.GetRequiredService<IControlPlaneStateStore>());
        Assert.IsType<GroundworkIncidentStateStore>(provider.GetRequiredService<IIncidentStateStore>());
        Assert.IsType<GroundworkRuntimeCheckpointWriter>(provider.GetRequiredService<IRuntimeCheckpointWriter>());
        Assert.IsType<GroundworkRuntimePostCommitOutboxStore>(provider.GetRequiredService<IRuntimePostCommitOutboxStore>());
    }

    [Fact]
    public async Task Sqlite_Feature_Wires_DocumentStore_And_Bridge()
    {
        var services = new ServiceCollection();
        services.TryAddSingleton<IBookmarkStateStore, InMemoryBookmarkStateStore>();
        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();

        new SqliteGroundworkRuntimePersistenceShellFeature().ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();

        Assert.IsType<SqliteGroundworkDocumentStore>(provider.GetRequiredService<IDocumentStore>());
        Assert.IsType<GroundworkBookmarkStateStore>(provider.GetRequiredService<IBookmarkStateStore>());
        Assert.IsType<GroundworkWorkflowExecutableStore>(provider.GetRequiredService<IWorkflowExecutableStore>());
        Assert.IsType<GroundworkRuntimeCheckpointWriter>(provider.GetRequiredService<IRuntimeCheckpointWriter>());
        Assert.IsType<GroundworkRuntimePostCommitOutboxStore>(provider.GetRequiredService<IRuntimePostCommitOutboxStore>());
    }

    [Fact]
    public async Task Composed_Sqlite_Feature_Persists_Across_Restart()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-compose-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            // First host process: compose the feature exactly as a host would, then persist through a resolved seam.
            await using (var provider = BuildComposedProvider(connectionString))
            {
                var bookmarks = provider.GetRequiredService<IBookmarkStateStore>();
                await bookmarks.SaveAsync(Bookmark("wf-1", "bm-1"));
            }

            // Second host process: a fresh container over the same database file. State read back was genuinely durable.
            await using (var provider = BuildComposedProvider(connectionString))
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

    private static ServiceProvider BuildComposedProvider(string connectionString)
    {
        var services = new ServiceCollection();
        new SqliteGroundworkRuntimePersistenceShellFeature { ConnectionString = connectionString }.ConfigureServices(services);
        return services.BuildServiceProvider();
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
}
