using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Cluster.EntityFrameworkCore;

public sealed class ClusterMembershipSqliteDbContext(DbContextOptions<ClusterMembershipSqliteDbContext> options)
    : ClusterMembershipDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
}

public sealed class ClusterMembershipSqlServerDbContext(DbContextOptions<ClusterMembershipSqlServerDbContext> options)
    : ClusterMembershipDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
}

public sealed class ClusterMembershipPostgreSqlDbContext(DbContextOptions<ClusterMembershipPostgreSqlDbContext> options)
    : ClusterMembershipDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
}

public sealed class ClusterMembershipMySqlDbContext(DbContextOptions<ClusterMembershipMySqlDbContext> options)
    : ClusterMembershipDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.MySql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
}
