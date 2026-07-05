using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Handlers;
using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Hosting.Services;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Persistence.EFCore.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// Pins that the WorkflowsDesign Sqlite migration chain applies cleanly, end to end, on a fresh
/// database — Initial → AddWorkflowDefinitionSoftDelete → DropWorkflowDefinitionDraftValidations —
/// and that the <c>WorkflowDefinitionDraftValidations</c> table (dropped by the last migration,
/// since validation errors became derived state) is absent afterwards.
/// </summary>
/// <remarks>
/// Deliberately a standalone fixture: <c>WorkflowsDesignTestHost</c> uses <c>EnsureCreated</c> (which
/// bypasses migrations), and that default is intentional. This test applies real migrations via
/// <c>Database.Migrate()</c> instead, using its own connection + context options.
/// </remarks>
public sealed class MigrationSmokeTests
{
    [Fact]
    public void Migration_chain_applies_cleanly_and_drops_the_validations_table()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        // Minimal provider so OnModelCreating resolves the Sqlite model-creating handler (production
        // model shape) — no command wiring needed, migrations carry the DDL.
        var services = new ServiceCollection();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>();
        using var provider = services.BuildServiceProvider();

        var options = new DbContextOptionsBuilder<WorkflowsDesignDbContext>()
            .UseSqlite(connection, b => b.MigrationsAssembly(typeof(SqliteWorkflowsDesignPersistenceShellFeature).Assembly.GetName().Name))
            .Options;

        using var ctx = new WorkflowsDesignDbContext(options, provider);

        // Applies all three migrations, in order, on the fresh in-memory database.
        var applied = ctx.Database.GetMigrations().ToArray();
        var expected = new[]
        {
            "20260529114555_Initial",
            "20260618012500_AddWorkflowDefinitionSoftDelete",
            "20260705185806_DropWorkflowDefinitionDraftValidations",
        };
        Assert.Equal(expected, applied);

        ctx.Database.Migrate();

        // The dropped table must be absent from sqlite_master after the chain applies.
        Assert.False(TableExists(connection, "WorkflowDefinitionDraftValidations"));
        // Sanity: a surviving table is present, so the assertion above is meaningful.
        Assert.True(TableExists(connection, "WorkflowDefinitionDrafts"));
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }
}
