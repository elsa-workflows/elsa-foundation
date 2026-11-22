using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;

public sealed class ExecutionCommandTransportSqliteDbContext(DbContextOptions<ExecutionCommandTransportSqliteDbContext> options) : ExecutionCommandTransportDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExecutionCommandStreamHeadEntity>().Property(row => row.ScopeKey).HasColumnType("TEXT");
        modelBuilder.Entity<ExecutionCommandTransportItemEntity>().Property(row => row.ScopeKey).HasColumnType("TEXT");
        modelBuilder.Entity<ExecutionCommandTransportItemEntity>().Property(row => row.PayloadJson).HasColumnType("TEXT");
    }
}

public sealed class ExecutionCommandTransportSqlServerDbContext(DbContextOptions<ExecutionCommandTransportSqlServerDbContext> options) : ExecutionCommandTransportDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExecutionCommandStreamHeadEntity>().Property(row => row.ScopeKey).HasColumnType("nvarchar(max)");
        modelBuilder.Entity<ExecutionCommandTransportItemEntity>().Property(row => row.ScopeKey).HasColumnType("nvarchar(max)");
        modelBuilder.Entity<ExecutionCommandTransportItemEntity>().Property(row => row.PayloadJson).HasColumnType("nvarchar(max)");
    }
}

public sealed class ExecutionCommandTransportPostgreSqlDbContext(DbContextOptions<ExecutionCommandTransportPostgreSqlDbContext> options) : ExecutionCommandTransportDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExecutionCommandStreamHeadEntity>().Property(row => row.ScopeKey).HasColumnType("text");
        modelBuilder.Entity<ExecutionCommandTransportItemEntity>().Property(row => row.ScopeKey).HasColumnType("text");
        modelBuilder.Entity<ExecutionCommandTransportItemEntity>().Property(row => row.PayloadJson).HasColumnType("text");
    }
}

public sealed class ExecutionCommandTransportMySqlDbContext(DbContextOptions<ExecutionCommandTransportMySqlDbContext> options) : ExecutionCommandTransportDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.MySql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExecutionCommandStreamHeadEntity>().Property(row => row.ScopeKey).HasColumnType("longtext");
        modelBuilder.Entity<ExecutionCommandTransportItemEntity>().Property(row => row.ScopeKey).HasColumnType("longtext");
        modelBuilder.Entity<ExecutionCommandTransportItemEntity>().Property(row => row.PayloadJson).HasColumnType("longtext");
    }
}
