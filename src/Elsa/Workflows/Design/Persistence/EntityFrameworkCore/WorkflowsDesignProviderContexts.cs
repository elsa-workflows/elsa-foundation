using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore;

public sealed class WorkflowsDesignSqliteDbContext(DbContextOptions<WorkflowsDesignSqliteDbContext> options) : WorkflowsDesignDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) { ConfigureDateTime(modelBuilder, "TEXT"); ConfigureText(modelBuilder, "TEXT"); ConfigureOrdinalCollation(modelBuilder, "BINARY"); }
}

public sealed class WorkflowsDesignSqlServerDbContext(DbContextOptions<WorkflowsDesignSqlServerDbContext> options) : WorkflowsDesignDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) { ConfigureDateTime(modelBuilder, "datetimeoffset"); ConfigureText(modelBuilder, "nvarchar(max)"); ConfigureOrdinalCollation(modelBuilder, "Latin1_General_100_BIN2"); }
}

public sealed class WorkflowsDesignPostgreSqlDbContext(DbContextOptions<WorkflowsDesignPostgreSqlDbContext> options) : WorkflowsDesignDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) { ConfigureDateTime(modelBuilder, "timestamp with time zone"); ConfigureText(modelBuilder, "text"); ConfigureOrdinalCollation(modelBuilder, "C"); }
}

public sealed class WorkflowsDesignMySqlDbContext(DbContextOptions<WorkflowsDesignMySqlDbContext> options) : WorkflowsDesignDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.MySql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) { ConfigureDateTime(modelBuilder, "datetime(6)"); ConfigureText(modelBuilder, "longtext"); ConfigureOrdinalCollation(modelBuilder, "utf8mb4_bin"); }
}
