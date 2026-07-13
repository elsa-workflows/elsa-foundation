using Elsa.Persistence.Groundwork.Sqlite;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
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
        Assert.Equal(7L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Sqlite_query_applies_every_public_history_filter_expression()
    {
        await using var database = new TemporarySqliteDatabase();
        await using var provider = await BuildProviderAsync(database.ConnectionString);
        var store = provider.GetRequiredService<IWorkflowExecutionStateStore>();
        await store.SaveAsync(State("wf-match", _timestamp) with
        {
            CorrelationId = "correlation-1",
            RunKind = WorkflowRunKind.PublishedRun
        });
        await store.SaveAsync(State("wf-other", _timestamp.AddHours(-1)));

        var page = await store.QueryPageAsync(new WorkflowExecutionStatePageQuery(
            PageSize: 10,
            TenantId: "tenant-1",
            DefinitionId: "definition-1",
            Status: WorkflowExecutionStatus.Completed,
            RunKind: WorkflowRunKind.PublishedRun,
            From: _timestamp.AddSeconds(-1),
            To: _timestamp.AddSeconds(1),
            CorrelationId: "correlation-1",
            WorkflowExecutionId: "wf-match",
            ArtifactId: "artifact-1"));

        Assert.Equal("wf-match", Assert.Single(page.Items).WorkflowExecutionId);
        Assert.Equal(1, page.TotalCount);
    }

    private static async Task<ServiceProvider> BuildProviderAsync(string connectionString)
    {
        var services = new ServiceCollection();
        new SqliteGroundworkRuntimePersistenceShellFeature { ConnectionString = connectionString }.ConfigureServices(services);
        var provider = services.BuildServiceProvider();
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
