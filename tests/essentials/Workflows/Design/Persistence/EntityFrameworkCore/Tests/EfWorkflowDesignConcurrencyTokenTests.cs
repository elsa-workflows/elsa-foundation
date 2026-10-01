using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

/// <summary>The SQLite leg of the LastModifiedAt concurrency token scenarios the native provider tests run on the other providers (#2204).</summary>
public sealed class EfWorkflowDesignConcurrencyTokenTests : IAsyncDisposable
{
    private readonly TemporarySqliteDatabase _database = new("workflows-design-token");

    [Theory]
    [InlineData(true)] // The scope that materialized the definition also updates and deletes it.
    [InlineData(false)] // Each step reads the definition back in a scope of its own.
    public Task Definition_commands_update_and_delete_a_definition(bool sameScope) =>
        ConcurrencyTokenScenarios.UpdateAndDeleteDefinitionAsync(CreateContext, sameScope);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Every_row_with_a_LastModifiedAt_token_updates_and_deletes(bool sameScope) =>
        ConcurrencyTokenScenarios.UpdateAndDeleteTokenEntitiesAsync(CreateContext, sameScope);

    private WorkflowsDesignDbContext CreateContext() =>
        new WorkflowsDesignSqliteDbContext(new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>().UseSqlite(_database.ConnectionString).Options);

    public ValueTask DisposeAsync() => _database.DisposeAsync();
}
