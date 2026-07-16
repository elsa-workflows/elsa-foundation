using Elsa.Persistence.Groundwork.Sqlite;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowExecutionStatePagingTests
{
    private readonly DateTimeOffset _timestamp = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sqlite_query_keeps_keyset_pages_stable_when_newer_executions_are_inserted()
    {
        await using var database = new TemporarySqliteDatabase();
        await using var provider = await BuildProviderAsync(database.ConnectionString);
        var store = provider.GetRequiredService<IWorkflowExecutionStateStore>();
        foreach (var id in new[] { "wf-d", "wf-b", "wf-a", "wf-c" })
            await store.SaveAsync(State(id, _timestamp));

        var first = await store.QueryPageAsync(new WorkflowExecutionStatePageQuery(PageSize: 2));
        await store.SaveAsync(State("wf-0", _timestamp));
        await store.SaveAsync(State("wf-new", _timestamp.AddMinutes(1)));
        var second = await store.QueryPageAsync(new WorkflowExecutionStatePageQuery(PageSize: 2, Cursor: first.NextCursor));
        var previous = await store.QueryPageAsync(new WorkflowExecutionStatePageQuery(PageSize: 2, Cursor: second.PreviousCursor));

        Assert.Equal(["wf-a", "wf-b"], first.Items.Select(x => x.WorkflowExecutionId));
        Assert.Equal(["wf-c", "wf-d"], second.Items.Select(x => x.WorkflowExecutionId));
        Assert.Equal(first.Items.Select(x => x.WorkflowExecutionId), previous.Items.Select(x => x.WorkflowExecutionId));
        Assert.Equal(6, second.TotalCount);

        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name LIKE 'ix_elsa_workflow_history_%';";
        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Sqlite_startup_does_not_rewrite_current_history_document()
    {
        await using var database = new TemporarySqliteDatabase();
        await using (var provider = await BuildProviderAsync(database.ConnectionString))
        {
            var contentJson = await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "v4", "workflowExecutionState.json"));
            var store = provider.GetRequiredService<IDocumentStore>();
            var result = await store.SaveAsync(new SaveDocumentRequest(
                "workflowExecutionState",
                "wf-1",
                "4",
                contentJson));
            Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status);
        }

        await using var restartedProvider = await BuildProviderAsync(database.ConnectionString);

        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version, content_json FROM groundwork_documents WHERE document_kind = 'workflowExecutionState' AND id = 'wf-1';";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("4", reader.GetString(0));
        Assert.Contains("\"historySortTicks\"", reader.GetString(1), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sqlite_query_applies_every_public_history_filter_expression()
    {
        await using var database = new TemporarySqliteDatabase();
        await using var provider = await BuildProviderAsync(database.ConnectionString);
        var store = provider.GetRequiredService<IWorkflowExecutionStateStore>();
        await WorkflowExecutionHistoryProviderConformance.VerifyAllFiltersAsync(store, _timestamp);
    }

    [Fact]
    public async Task Sqlite_query_rejects_an_explicit_tenant_outside_the_current_scope()
    {
        await using var database = new TemporarySqliteDatabase();
        await using var provider = await BuildProviderAsync(database.ConnectionString);
        var store = provider.GetRequiredService<IWorkflowExecutionStateStore>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.QueryPageAsync(new WorkflowExecutionStatePageQuery(PageSize: 10, TenantId: "tenant-b")));

        Assert.DoesNotContain("tenant-1", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-b", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sqlite_history_query_uses_host_transformed_table_and_envelope_names()
    {
        await using var database = new TemporarySqliteDatabase();
        var services = new ServiceCollection();
        services.AddScoped<IPersistenceAccessContextAccessor>(_ => TenantAccessContextAccessor.Instance);
        services.AddSingleton(new GroundworkStorageNamingPolicyOptions(
            "history-prefix-v1",
            context => $"host_{context.FeatureDefaultLogicalName}"));
        new SqliteGroundworkRuntimePersistenceShellFeature { ConnectionString = database.ConnectionString }.ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await provider.ApplySqliteGroundworkSchemaAsync(database.ConnectionString);
        await provider.InitializeGroundworkStoreAsync();

        var store = provider.GetRequiredService<IWorkflowExecutionStateStore>();
        await store.SaveAsync(State("wf-transformed", _timestamp));
        var page = await store.QueryPageAsync(new WorkflowExecutionStatePageQuery(PageSize: 10));

        Assert.Equal("wf-transformed", Assert.Single(page.Items).WorkflowExecutionId);
        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM host_groundwork_documents WHERE host_document_kind = 'workflowExecutionState' AND host_id = 'wf-transformed' AND host_content_json IS NOT NULL;";
        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    private static async Task<ServiceProvider> BuildProviderAsync(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddScoped<IPersistenceAccessContextAccessor>(_ => TenantAccessContextAccessor.Instance);
        new SqliteGroundworkRuntimePersistenceShellFeature { ConnectionString = connectionString }.ConfigureServices(services);
        var provider = services.BuildServiceProvider();
        await provider.ApplySqliteGroundworkSchemaAsync(connectionString);
        await provider.InitializeGroundworkStoreAsync();
        return provider;
    }

    private static WorkflowExecutionState State(string id, DateTimeOffset timestamp) => new(
        id,
        new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "hash-1"),
        WorkflowExecutionStatus.Completed,
        null,
        timestamp.AddMinutes(-1),
        timestamp.AddMinutes(-1),
        timestamp,
        timestamp,
        null,
        null,
        "tenant-1",
        new Dictionary<string, string>());

    private sealed class TenantAccessContextAccessor : IPersistenceAccessContextAccessor
    {
        public static TenantAccessContextAccessor Instance { get; } = new();

        public PersistenceAccessContext Current { get; } =
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-1"));
    }

    private sealed class TemporarySqliteDatabase : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"elsa-groundwork-paging-{Guid.NewGuid():N}.db");

        public string ConnectionString => $"Data Source={_path}";

        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            return ValueTask.CompletedTask;
        }
    }
}
