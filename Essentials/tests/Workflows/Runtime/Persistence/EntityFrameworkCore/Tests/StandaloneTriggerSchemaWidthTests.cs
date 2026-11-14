using System.Reflection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class StandaloneTriggerSchemaWidthTests
{
    [Fact]
    public void All_provider_migrations_and_the_current_model_hold_the_actual_resource_and_association_order_keys()
    {
        var width = new[] { "a", new string('x', 128) }.Select(EfRuntimeOperationalStoreSupport.Order).Max(key => key.Length);
        using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        Assert.True(context.Model.FindEntityType(typeof(StandaloneTriggerEntity))!.FindProperty("TriggerOrderKey")!.GetMaxLength() >= width);
        Assert.True(context.Model.FindEntityType(typeof(TriggerSubscriptionEntity))!.FindProperty("SubscriptionOrderKey")!.GetMaxLength() >= width);
        var migrations = typeof(RuntimeDbContext).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<MigrationAttribute>()?.Id.EndsWith("_StandaloneTriggers", StringComparison.Ordinal) == true)
            .Select(type => (Migration)Activator.CreateInstance(type)!).ToArray();
        Assert.Equal(4, migrations.Length);
        foreach (var migration in migrations)
        {
            var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
            Assert.True(Assert.Single(tables, table => table.Name == "elsa_runtime_standalone_triggers")
                .Columns.Single(column => column.Name == "TriggerOrderKey").MaxLength >= width);
            Assert.True(Assert.Single(tables, table => table.Name == "elsa_runtime_trigger_subscriptions")
                .Columns.Single(column => column.Name == "SubscriptionOrderKey").MaxLength >= width);
        }
    }
}
