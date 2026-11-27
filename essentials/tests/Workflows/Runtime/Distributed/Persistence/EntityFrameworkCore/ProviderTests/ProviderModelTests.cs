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
        Assert.False(entity.FindProperty(nameof(Entities.ExecutionPlacementLeaseEntity.IsReleased))!.IsNullable);
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
            nameof(Entities.ExecutionPlacementLeaseEntity.IsReleased),
            nameof(Entities.ExecutionPlacementLeaseEntity.ExpiresAtUtcTicks),
            nameof(Entities.ExecutionPlacementLeaseEntity.WorkflowExecutionIdOrderKey)]));
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    public void Every_supported_provider_exposes_the_same_command_transport_model(string provider)
    {
        using var context = CreateCommandTransport(provider);
        var head = context.Model.FindEntityType(typeof(Entities.ExecutionCommandStreamHeadEntity));
        var item = context.Model.FindEntityType(typeof(Entities.ExecutionCommandTransportItemEntity));

        Assert.NotNull(head);
        Assert.NotNull(item);
        Assert.Equal(ExecutionCommandTransportEfModule.StreamHeadTableName, head!.GetTableName());
        Assert.Equal(ExecutionCommandTransportEfModule.TransportItemTableName, item!.GetTableName());
        Assert.Equal(
            [nameof(Entities.ExecutionCommandStreamHeadEntity.Id)],
            head.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(
            [nameof(Entities.ExecutionCommandTransportItemEntity.Id)],
            item.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(ExpectedScopeColumnType(provider), head.FindProperty(nameof(Entities.ExecutionCommandStreamHeadEntity.ScopeKey))!.GetColumnType());
        Assert.Equal(ExpectedScopeColumnType(provider), item.FindProperty(nameof(Entities.ExecutionCommandTransportItemEntity.ScopeKey))!.GetColumnType());
        Assert.Equal(ExpectedPayloadColumnType(provider), item.FindProperty(nameof(Entities.ExecutionCommandTransportItemEntity.PayloadJson))!.GetColumnType());
        Assert.Equal(typeof(byte[]), head.FindProperty(nameof(Entities.ExecutionCommandStreamHeadEntity.WorkflowExecutionIdOrderKey))!.ClrType);
        Assert.Equal(ExecutionCommandTransportEfModule.WorkflowExecutionIdOrderKeyWidth, head.FindProperty(nameof(Entities.ExecutionCommandStreamHeadEntity.WorkflowExecutionIdOrderKey))!.GetMaxLength());
        Assert.True(head.FindProperty(nameof(Entities.ExecutionCommandStreamHeadEntity.Revision))!.IsConcurrencyToken);
        Assert.True(item.FindProperty(nameof(Entities.ExecutionCommandTransportItemEntity.Revision))!.IsConcurrencyToken);
        Assert.Contains(ExpectedProviderFragment(provider), context.Database.ProviderName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(head.GetIndexes(), index => index.Properties.Select(property => property.Name).SequenceEqual([
            nameof(Entities.ExecutionCommandStreamHeadEntity.ScopeKeyHash),
            nameof(Entities.ExecutionCommandStreamHeadEntity.WorkflowExecutionIdHash)]));
        Assert.Contains(head.GetIndexes(), index => index.Properties.Select(property => property.Name).SequenceEqual([
            nameof(Entities.ExecutionCommandStreamHeadEntity.ScopeKeyHash),
            nameof(Entities.ExecutionCommandStreamHeadEntity.PendingVisibleAtUtcTicks),
            nameof(Entities.ExecutionCommandStreamHeadEntity.WorkflowExecutionIdOrderKey),
            nameof(Entities.ExecutionCommandStreamHeadEntity.Id)]));
        Assert.Contains(item.GetIndexes(), index => index.Properties.Select(property => property.Name).SequenceEqual([
            nameof(Entities.ExecutionCommandTransportItemEntity.ScopeKeyHash),
            nameof(Entities.ExecutionCommandTransportItemEntity.WorkflowExecutionIdHash),
            nameof(Entities.ExecutionCommandTransportItemEntity.Sequence)]));
        Assert.Contains(item.GetIndexes(), index => index.Properties.Select(property => property.Name).SequenceEqual([
            nameof(Entities.ExecutionCommandTransportItemEntity.ScopeKeyHash),
            nameof(Entities.ExecutionCommandTransportItemEntity.WorkflowExecutionIdHash),
            nameof(Entities.ExecutionCommandTransportItemEntity.VisibleAtUtcTicks),
            nameof(Entities.ExecutionCommandTransportItemEntity.Sequence),
            nameof(Entities.ExecutionCommandTransportItemEntity.TransportItemIdHash)]));
    }

    private static ExecutionPlacementDbContext Create(string provider) => provider switch
    {
        "Sqlite" => new ExecutionPlacementSqliteDbContext(new DbContextOptionsBuilder<ExecutionPlacementSqliteDbContext>().UseSqlite("Data Source=:memory:").Options),
        "SqlServer" => new ExecutionPlacementSqlServerDbContext(new DbContextOptionsBuilder<ExecutionPlacementSqlServerDbContext>().UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=unused").Options),
        "PostgreSql" => new ExecutionPlacementPostgreSqlDbContext(new DbContextOptionsBuilder<ExecutionPlacementPostgreSqlDbContext>().UseNpgsql("Host=localhost;Database=unused").Options),
        "MySql" => new ExecutionPlacementMySqlDbContext(new DbContextOptionsBuilder<ExecutionPlacementMySqlDbContext>().UseMySQL("Server=localhost;Database=unused").Options),
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    private static ExecutionCommandTransportDbContext CreateCommandTransport(string provider) => provider switch
    {
        "Sqlite" => new ExecutionCommandTransportSqliteDbContext(new DbContextOptionsBuilder<ExecutionCommandTransportSqliteDbContext>().UseSqlite("Data Source=:memory:").Options),
        "SqlServer" => new ExecutionCommandTransportSqlServerDbContext(new DbContextOptionsBuilder<ExecutionCommandTransportSqlServerDbContext>().UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=unused").Options),
        "PostgreSql" => new ExecutionCommandTransportPostgreSqlDbContext(new DbContextOptionsBuilder<ExecutionCommandTransportPostgreSqlDbContext>().UseNpgsql("Host=localhost;Database=unused").Options),
        "MySql" => new ExecutionCommandTransportMySqlDbContext(new DbContextOptionsBuilder<ExecutionCommandTransportMySqlDbContext>().UseMySQL("Server=localhost;Database=unused").Options),
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

    private static string ExpectedPayloadColumnType(string provider) => provider switch
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
