using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
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
    private readonly DateTimeOffset _timestamp = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task Composed_feature_wires_a_postgresql_document_store()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");

        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
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
    public async Task PostgreSql_executes_every_workflow_history_filter_and_keyset_page()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");

        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var provider = await BuildComposedProviderAsync(connectionString);
        var store = provider.GetRequiredService<IWorkflowExecutionStateStore>();
        await WorkflowExecutionHistoryProviderConformance.VerifyAllFiltersAsync(store, _timestamp);

        var first = await store.QueryPageAsync(new WorkflowExecutionStatePageQuery(PageSize: 3));
        var second = await store.QueryPageAsync(new WorkflowExecutionStatePageQuery(PageSize: 3, Cursor: first.NextCursor));
        Assert.Equal(3, first.Items.Count);
        Assert.Equal(3, second.Items.Count);
        Assert.Empty(first.Items.Select(x => x.WorkflowExecutionId).Intersect(second.Items.Select(x => x.WorkflowExecutionId)));
    }

    [SkippableFact]
    public async Task PostgreSql_startup_upgrades_v2_history_and_creates_online_indexes_before_queries()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");

        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using (var provider = await BuildComposedProviderAsync(connectionString))
        {
            var contentJson = await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "v2", "workflowExecutionState.json"));
            var result = await provider.GetRequiredService<IDocumentStore>().SaveAsync(new SaveDocumentRequest(
                "workflowExecutionState",
                "wf-1",
                "2",
                contentJson));
            Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status);
        }

        await using var restartedProvider = await BuildComposedProviderAsync(connectionString);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT schema_version, content_json FROM groundwork_documents WHERE document_kind = 'workflowExecutionState' AND id = 'wf-1';";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(
                ElsaRuntimeDocumentVersions.Stamp(
                    ElsaRuntimeDocumentVersions.CurrentFor(ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind)),
                reader.GetString(0));
            Assert.Contains("\"historySortTicks\"", reader.GetString(1), StringComparison.Ordinal);
            Assert.Contains("\"rootVariableFrame\"", reader.GetString(1), StringComparison.Ordinal);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = current_schema() AND indexname LIKE 'ix_elsa_workflow_history_%';";
            Assert.Equal(7L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        }

        var page = await restartedProvider.GetRequiredService<IWorkflowExecutionStateStore>()
            .QueryPageAsync(new WorkflowExecutionStatePageQuery(PageSize: 10));
        Assert.Equal("wf-1", Assert.Single(page.Items).WorkflowExecutionId);
    }

    private static async Task<ServiceProvider> BuildComposedProviderAsync(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        new PostgreSqlGroundworkRuntimePersistenceShellFeature { ConnectionString = connectionString }.ConfigureServices(services);
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

}
