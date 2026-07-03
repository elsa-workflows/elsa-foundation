using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using global::Groundwork.Documents.Store;
using global::Groundwork.PostgreSql.Documents;
using Microsoft.Extensions.DependencyInjection;
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

        Assert.IsType<PostgreSqlDocumentStoreHandle>(provider.GetRequiredService<PostgreSqlDocumentStoreHandle>());
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
        await using (var provider = BuildComposedProvider(connectionString))
        {
            var bookmarks = provider.GetRequiredService<IBookmarkStateStore>();
            await bookmarks.SaveAsync(Bookmark("wf-1", "bm-1"));
        }

        // Second host process: a fresh container over the same database. State read back was genuinely durable.
        await using (var provider = BuildComposedProvider(connectionString))
        {
            var bookmarks = provider.GetRequiredService<IBookmarkStateStore>();
            Assert.NotNull(await bookmarks.FindAsync("wf-1", "bm-1"));
        }
    }

    private static ServiceProvider BuildComposedProvider(string connectionString)
    {
        var services = new ServiceCollection();
        new PostgreSqlGroundworkRuntimePersistenceShellFeature { ConnectionString = connectionString }.ConfigureServices(services);
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
