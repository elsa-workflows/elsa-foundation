using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;

public sealed class ExecutionPlacementSqliteDbContext(DbContextOptions<ExecutionPlacementSqliteDbContext> options)
    : ExecutionPlacementDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ExecutionPlacementLeaseEntity>().Property(row => row.ScopeKey).HasColumnType("TEXT");
}

public sealed class ExecutionPlacementSqlServerDbContext(DbContextOptions<ExecutionPlacementSqlServerDbContext> options)
    : ExecutionPlacementDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ExecutionPlacementLeaseEntity>().Property(row => row.ScopeKey).HasColumnType("nvarchar(max)");
}

public sealed class ExecutionPlacementPostgreSqlDbContext(DbContextOptions<ExecutionPlacementPostgreSqlDbContext> options)
    : ExecutionPlacementDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ExecutionPlacementLeaseEntity>().Property(row => row.ScopeKey).HasColumnType("text");
}

public sealed class ExecutionPlacementMySqlDbContext(DbContextOptions<ExecutionPlacementMySqlDbContext> options)
    : ExecutionPlacementDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.MySql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ExecutionPlacementLeaseEntity>().Property(row => row.ScopeKey).HasColumnType("longtext");
}
