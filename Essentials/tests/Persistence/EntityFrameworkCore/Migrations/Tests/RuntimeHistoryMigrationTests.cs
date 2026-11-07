using System.Data;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

public sealed class RuntimeHistoryMigrationTests
{
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    [Trait("Feature", "RunHistory")]
    public void History_is_an_additive_step_on_every_retained_runtime_chain(string provider)
    {
        var type = Assert.Single(ModuleContextCatalog.Contexts(provider), type => typeof(RuntimeDbContext).IsAssignableFrom(type));
        using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection(provider));
        var chain = context.Database.GetMigrations().ToArray();
        Assert.EndsWith("_RunHistory", chain[^1]); Assert.EndsWith("_OutboxClaimDeliveredAt", chain[^2]);
        var assembly = context.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations[chain[^1]], context.Database.ProviderName!);
        Assert.Equal(new[] { "elsa_runtime_history_activity", "elsa_runtime_history_bookmark", "elsa_runtime_history_bookmark_count", "elsa_runtime_history_event", "elsa_runtime_history_head" },
            migration.UpOperations.OfType<CreateTableOperation>().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(migration.UpOperations, operation => Assert.True(operation is CreateTableOperation or CreateIndexOperation or AddColumnOperation,
            "Run history may add owned tables/indexes and defaulted timer source projections; it cannot rebuild retained Runtime tables."));
        Assert.Equal(new[] { "HistoryPending", "HistoryRetired", "HistoryRevision" }, migration.UpOperations.OfType<AddColumnOperation>().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(migration.UpOperations.OfType<AddColumnOperation>(), column => { Assert.Equal("elsa_runtime_durable_timer", column.Table); Assert.NotNull(column.DefaultValue); });
        var indexes = migration.UpOperations.OfType<CreateIndexOperation>().ToArray();
        Assert.Contains(indexes, index => index.Table == "elsa_runtime_history_event" && index.IsUnique && index.Columns.SequenceEqual(new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "Sequence" }));
        Assert.Contains(indexes, index => index.Table == "elsa_runtime_history_event" && index.Columns.Contains("NameHash") && index.Columns[^1] == "Sequence");
        Assert.Contains(indexes, index => index.Table == "elsa_runtime_history_event" && index.Columns.Contains("Category") && index.Columns[^1] == "Sequence");
        Assert.Contains(indexes, index => index.Table == "elsa_runtime_durable_timer" && index.Columns.SequenceEqual(new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "HistoryPending", "HistoryRetired", "TimerIdHash" }));
        var script = context.GetService<IMigrator>().GenerateScript(chain[^2], chain[^1]);
        Assert.Contains("elsa_runtime_history_head", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
