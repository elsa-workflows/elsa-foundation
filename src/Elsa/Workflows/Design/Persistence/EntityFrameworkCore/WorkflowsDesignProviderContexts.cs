using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore;

public sealed class WorkflowsDesignSqliteDbContext(DbContextOptions<WorkflowsDesignSqliteDbContext> options) : WorkflowsDesignDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) { }
}

public sealed class WorkflowsDesignSqlServerDbContext(DbContextOptions<WorkflowsDesignSqlServerDbContext> options) : WorkflowsDesignDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) { }
}

public sealed class WorkflowsDesignPostgreSqlDbContext(DbContextOptions<WorkflowsDesignPostgreSqlDbContext> options) : WorkflowsDesignDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) { }
}

public sealed class WorkflowsDesignMySqlDbContext(DbContextOptions<WorkflowsDesignMySqlDbContext> options) : WorkflowsDesignDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.MySql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) { }
}
