using System.Data;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

public sealed class WorkflowsDesignRetainedMigrationTests
{
    [Theory]
    [InlineData("Sqlite", "20260928040714_Initial", "20261030020434_Initial", "20261031002424_Initial", null)]
    [InlineData("SqlServer", "20260928040712_Initial", "20261030020433_Initial", "20261031002423_Initial", null)]
    [InlineData("PostgreSql", "20260928040711_Initial", "20261030020432_Initial", "20261031002421_Initial", null)]
    [InlineData("MySql", "20260928040709_Initial", "20261030020430_Initial", "20261031002420_Initial", null)]
    [InlineData("SqlServer", "20260928040712_Initial", "20261030020433_Initial", "20261031002423_Initial", "elsa_alt")]
    [InlineData("PostgreSql", "20260928040711_Initial", "20261030020432_Initial", "20261031002421_Initial", "elsa_alt")]
    public void Retained_baseline_and_additive_templates_script_without_opening_a_database(string provider, string baseline, string templates, string library, string? schema)
    {
        var type = Assert.Single(ModuleContextCatalog.Contexts(provider), type => typeof(WorkflowsDesignDbContext).IsAssignableFrom(type));
        using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection(provider), schema: schema);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Equal(new[] { baseline, templates, library }, context.Database.GetMigrations());
        var assembly = context.GetService<IMigrationsAssembly>();
        var initial = assembly.CreateMigration(assembly.Migrations[baseline], context.Database.ProviderName!);
        Assert.Contains(initial.UpOperations, operation => operation is CreateTableOperation { Name: "elsa_workflow_definitions_v2" });
        Assert.DoesNotContain(initial.UpOperations, operation => operation is CreateTableOperation { Name: "elsa_workflow_templates" });
        var addition = assembly.CreateMigration(assembly.Migrations[templates], context.Database.ProviderName!);
        Assert.Collection(addition.UpOperations,
            operation => Assert.Equal("elsa_workflow_templates", Assert.IsType<CreateTableOperation>(operation).Name),
            operation => Assert.Equal("elsa_workflow_templates", Assert.IsType<CreateIndexOperation>(operation).Table));
        var migrator = context.GetService<IMigrator>();
        var incremental = migrator.GenerateScript(baseline, templates);
        Assert.Contains("elsa_workflow_templates", incremental, StringComparison.Ordinal);
        Assert.DoesNotContain("elsa_workflow_definitions_v2", incremental, StringComparison.Ordinal);
        var libraryAddition = assembly.CreateMigration(assembly.Migrations[library], context.Database.ProviderName!);
        Assert.Equal(8, libraryAddition.UpOperations.OfType<CreateTableOperation>().Count());
        Assert.All(libraryAddition.UpOperations, operation =>
        {
            if (operation is AlterColumnOperation alteration)
            {
                Assert.Equal("elsa_workflow_templates", alteration.Table);
                Assert.Equal("Revision", alteration.Name);
                Assert.Equal(alteration.OldColumn.ColumnType, alteration.ColumnType);
                Assert.Equal(alteration.OldColumn.MaxLength, alteration.MaxLength);
                Assert.Equal(alteration.OldColumn.IsNullable, alteration.IsNullable);
            }
            else Assert.True(operation is CreateTableOperation or CreateIndexOperation or AddColumnOperation);
        });
        var libraryScript = migrator.GenerateScript(templates, library);
        Assert.DoesNotContain("CREATE TABLE \"elsa_workflow_templates\"", libraryScript, StringComparison.Ordinal);
        if (provider != "Sqlite")
        {
            var history = context.GetService<IHistoryRepository>();
            var guard = history.GetBeginIfNotExistsScript(baseline);
            Assert.Contains("IN (", guard, StringComparison.Ordinal);
            Assert.Contains(baseline, guard, StringComparison.Ordinal);
            Assert.Contains(templates, guard, StringComparison.Ordinal);
            Assert.Contains(library, guard, StringComparison.Ordinal);
            var script = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
            Assert.Contains(guard.Trim(), script, StringComparison.Ordinal);
            if (schema is not null) Assert.Contains(schema, script, StringComparison.Ordinal);
        }
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public void A_cumulative_library_history_recognizes_both_preceding_schemas_without_rewriting_history()
    {
        const string baseline = "20260928040714_Initial";
        const string templates = "20261030020434_Initial";
        const string library = "20261031002424_Initial";
        var type = Assert.Single(ModuleContextCatalog.Contexts("Sqlite"), type => typeof(WorkflowsDesignDbContext).IsAssignableFrom(type));
        using var context = ModuleContextCatalog.Create(type, "Data Source=:memory:");
        context.Database.OpenConnection();
        var assembly = context.GetService<IMigrationsAssembly>();
        var history = context.GetService<IHistoryRepository>();
        var script = context.GetService<IMigrator>().GenerateScript();
        // Create the cumulative writer's schema/history in a new synthetic provider control.
        // No saved input/history is edited, and this is separate from actual-host upgrade evidence.
        foreach (var prior in new[] { baseline, templates })
        {
            var migration = assembly.CreateMigration(assembly.Migrations[prior], context.Database.ProviderName!);
            var insert = history.GetInsertScript(new HistoryRow(prior, migration.TargetModel.GetProductVersion()!));
            Assert.Contains(insert, script, StringComparison.Ordinal);
            script = script.Replace(insert, "", StringComparison.Ordinal);
        }
        context.Database.ExecuteSqlRaw(script);
        Assert.Equal(new[] { baseline, templates, library }, context.Database.GetAppliedMigrations());
        Assert.Empty(context.Database.GetPendingMigrations());
        context.Database.Migrate();
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory_ElsaWorkflowsDesign";
        using var rows = command.ExecuteReader();
        Assert.True(rows.Read());
        Assert.Equal(library, rows.GetString(0));
        Assert.False(rows.Read());
    }
}
