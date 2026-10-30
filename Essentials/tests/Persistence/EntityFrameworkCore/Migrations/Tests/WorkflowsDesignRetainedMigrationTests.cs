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
    [InlineData("Sqlite", "20260928040714_Initial", "20261030020434_Initial", null)]
    [InlineData("SqlServer", "20260928040712_Initial", "20261030020433_Initial", null)]
    [InlineData("PostgreSql", "20260928040711_Initial", "20261030020432_Initial", null)]
    [InlineData("MySql", "20260928040709_Initial", "20261030020430_Initial", null)]
    [InlineData("SqlServer", "20260928040712_Initial", "20261030020433_Initial", "elsa_retained")]
    [InlineData("PostgreSql", "20260928040711_Initial", "20261030020432_Initial", "elsa_retained")]
    public void Retained_baseline_and_additive_templates_script_without_opening_a_database(string provider, string baseline, string templates, string? schema)
    {
        var type = Assert.Single(ModuleContextCatalog.Contexts(provider), type => typeof(WorkflowsDesignDbContext).IsAssignableFrom(type));
        using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection(provider), schema: schema);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Equal(new[] { baseline, templates }, context.Database.GetMigrations());
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
        if (provider != "Sqlite")
        {
            var history = context.GetService<IHistoryRepository>();
            var guard = history.GetBeginIfNotExistsScript(baseline);
            Assert.Contains("IN (", guard, StringComparison.Ordinal);
            Assert.Contains(baseline, guard, StringComparison.Ordinal);
            Assert.Contains(templates, guard, StringComparison.Ordinal);
            var script = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
            Assert.Contains(guard.Trim(), script, StringComparison.Ordinal);
            if (schema is not null) Assert.Contains(schema, script, StringComparison.Ordinal);
        }
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }
}
