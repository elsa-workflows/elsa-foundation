using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.ProviderTests;

public sealed class ProviderModelTests
{
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    public void Every_supported_provider_exposes_the_same_placement_model(string provider)
    {
        using var context = Create(provider);
        var entity = context.Model.FindEntityType(typeof(Entities.ExecutionPlacementLeaseEntity));
        Assert.NotNull(entity);
        Assert.Equal(ExecutionPlacementEfModule.TableName, entity!.GetTableName());
        Assert.NotNull(entity.FindPrimaryKey());
        Assert.Equal(ExpectedScopeColumnType(provider), entity.FindProperty(nameof(Entities.ExecutionPlacementLeaseEntity.ScopeKey))!.GetColumnType());
        Assert.NotNull(entity.FindProperty(nameof(Entities.ExecutionPlacementLeaseEntity.ExpiresAtOffsetMinutes)));
        Assert.Null(entity.FindProperty("ExpiresAt"));
        var orderKey = entity.FindProperty(nameof(Entities.ExecutionPlacementLeaseEntity.WorkflowExecutionIdOrderKey));
        Assert.NotNull(orderKey);
        Assert.Equal(typeof(byte[]), orderKey!.ClrType);
        Assert.Equal(ExecutionPlacementEfModule.WorkflowExecutionIdOrderKeyWidth, orderKey.GetMaxLength());
        Assert.False(orderKey.IsNullable);
        Assert.Null(orderKey.GetValueConverter());
        Assert.Contains(ExpectedProviderFragment(provider), context.Database.ProviderName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(entity.GetIndexes(), index => index.Properties.Select(property => property.Name).SequenceEqual([
            nameof(Entities.ExecutionPlacementLeaseEntity.ScopeKeyHash),
            nameof(Entities.ExecutionPlacementLeaseEntity.OwnerIdHash),
            nameof(Entities.ExecutionPlacementLeaseEntity.ExpiresAtUtcTicks),
            nameof(Entities.ExecutionPlacementLeaseEntity.WorkflowExecutionIdOrderKey)]));
    }

    private static ExecutionPlacementDbContext Create(string provider) => provider switch
    {
        "Sqlite" => new ExecutionPlacementSqliteDbContext(new DbContextOptionsBuilder<ExecutionPlacementSqliteDbContext>().UseSqlite("Data Source=:memory:").Options),
        "SqlServer" => new ExecutionPlacementSqlServerDbContext(new DbContextOptionsBuilder<ExecutionPlacementSqlServerDbContext>().UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=unused").Options),
        "PostgreSql" => new ExecutionPlacementPostgreSqlDbContext(new DbContextOptionsBuilder<ExecutionPlacementPostgreSqlDbContext>().UseNpgsql("Host=localhost;Database=unused").Options),
        "MySql" => new ExecutionPlacementMySqlDbContext(new DbContextOptionsBuilder<ExecutionPlacementMySqlDbContext>().UseMySQL("Server=localhost;Database=unused").Options),
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    private static string ExpectedScopeColumnType(string provider) => provider switch
    {
        "Sqlite" => "TEXT",
        "SqlServer" => "nvarchar(max)",
        "PostgreSql" => "text",
        "MySql" => "longtext",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    private static string ExpectedProviderFragment(string provider) => provider switch
    {
        "Sqlite" => "Sqlite",
        "SqlServer" => "SqlServer",
        "PostgreSql" => "Npgsql",
        "MySql" => "MySQL",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
}
