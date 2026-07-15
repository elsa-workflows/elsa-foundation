using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using global::Groundwork.Documents.Store;
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
        var services = CreateServices(connectionString);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        // Drive the startup initializer, as a host would, so the session source is published before stores resolve.
        await provider.ApplyPostgreSqlGroundworkSchemaAsync(connectionString);
        await provider.InitializeGroundworkStoreAsync();

        await using var scope = provider.CreateAsyncScope();
        Assert.IsType<GroundworkScopedDocumentStore>(scope.ServiceProvider.GetRequiredService<IDocumentStore>());
        Assert.IsType<GroundworkBookmarkStateStore>(scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>());
        Assert.IsType<GroundworkRuntimeCheckpointWriter>(scope.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>());
        Assert.IsType<GroundworkRuntimePostCommitOutboxStore>(scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxStore>());
        Assert.IsType<GroundworkWorkflowSchedulerWorkQueue>(scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>());
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
            await using var scope = provider.CreateAsyncScope();
            var bookmarks = scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>();
            await bookmarks.SaveAsync(Bookmark("wf-1", "bm-1"));
        }

        // Second host process: a fresh container over the same database. State read back was genuinely durable.
        await using (var provider = await BuildComposedProviderAsync(connectionString))
        {
            await using var scope = provider.CreateAsyncScope();
            var bookmarks = scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>();
            Assert.NotNull(await bookmarks.FindAsync("wf-1", "bm-1"));
        }
    }

    [SkippableFact]
    public async Task PostgreSql_executes_every_workflow_history_filter_and_keyset_page()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");

        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var provider = await BuildComposedProviderAsync(connectionString);
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>();
        await WorkflowExecutionHistoryProviderConformance.VerifyAllFiltersAsync(store, _timestamp);

        var first = await store.QueryPageAsync(new WorkflowExecutionStatePageQuery(PageSize: 3));
        var second = await store.QueryPageAsync(new WorkflowExecutionStatePageQuery(PageSize: 3, Cursor: first.NextCursor));
        Assert.Equal(3, first.Items.Count);
        Assert.Equal(3, second.Items.Count);
        Assert.Empty(first.Items.Select(x => x.WorkflowExecutionId).Intersect(second.Items.Select(x => x.WorkflowExecutionId)));
    }

    [SkippableFact]
    public async Task PostgreSql_startup_does_not_rewrite_v2_history_or_create_legacy_indexes()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");

        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using (var provider = await BuildComposedProviderAsync(connectionString))
        {
            await using var scope = provider.CreateAsyncScope();
            var contentJson = await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "v2", "workflowExecutionState.json"));
            var result = await scope.ServiceProvider.GetRequiredService<IDocumentStore>().SaveAsync(new SaveDocumentRequest(
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
            Assert.Equal("2", reader.GetString(0));
            Assert.DoesNotContain("\"historySortTicks\"", reader.GetString(1), StringComparison.Ordinal);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = current_schema() AND indexname LIKE 'ix_elsa_workflow_history_%';";
            Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        }

        await using var restartedScope = restartedProvider.CreateAsyncScope();
        var page = await restartedScope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>()
            .QueryPageAsync(new WorkflowExecutionStatePageQuery(PageSize: 10));
        Assert.Equal("wf-1", Assert.Single(page.Items).WorkflowExecutionId);
    }

    private static async Task<ServiceProvider> BuildComposedProviderAsync(string connectionString)
    {
        var provider = CreateServices(connectionString)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await provider.ApplyPostgreSqlGroundworkSchemaAsync(connectionString);
        await provider.InitializeGroundworkStoreAsync();
        return provider;
    }

    private static ServiceCollection CreateServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddScoped<IPersistenceAccessContextAccessor>(_ => TenantAccessContextAccessor.Instance);
        services.AddWorkflowRuntime();
        new PostgreSqlGroundworkRuntimePersistenceShellFeature { ConnectionString = connectionString }
            .ConfigureServices(services);
        return services;
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

    private sealed class TenantAccessContextAccessor : IPersistenceAccessContextAccessor
    {
        public static TenantAccessContextAccessor Instance { get; } = new();

        public PersistenceAccessContext Current { get; } =
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-1"));
    }

}
