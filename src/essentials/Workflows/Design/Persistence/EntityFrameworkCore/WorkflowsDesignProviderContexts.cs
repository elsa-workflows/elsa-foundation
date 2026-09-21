using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore;

public sealed class WorkflowsDesignSqliteDbContext(DbContextOptions<WorkflowsDesignSqliteDbContext> options) : WorkflowsDesignDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) { ConfigureDateTime(modelBuilder, "TEXT"); ConfigureText(modelBuilder, "TEXT"); ApplyOrdinalCollation(modelBuilder, ExpectedProviderName); }
}

public sealed class WorkflowsDesignSqlServerDbContext(DbContextOptions<WorkflowsDesignSqlServerDbContext> options) : WorkflowsDesignDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) { ConfigureDateTime(modelBuilder, "datetimeoffset"); ConfigureText(modelBuilder, "nvarchar(max)"); ApplyOrdinalCollation(modelBuilder, ExpectedProviderName); }
}

public sealed class WorkflowsDesignPostgreSqlDbContext(DbContextOptions<WorkflowsDesignPostgreSqlDbContext> options) : WorkflowsDesignDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) { ConfigureDateTime(modelBuilder, "timestamp with time zone"); ConfigureText(modelBuilder, "text"); ApplyOrdinalCollation(modelBuilder, ExpectedProviderName); }
}

public sealed class WorkflowsDesignMySqlDbContext(DbContextOptions<WorkflowsDesignMySqlDbContext> options) : WorkflowsDesignDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.MySql;
    private const string CharacterSetAnnotation = "MySQL:Charset";
    public const string CharacterSet = "utf8mb4";

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        ConfigureDateTime(modelBuilder, "datetime(6)");
        ConfigureText(modelBuilder, "longtext");
        modelBuilder.Model.SetAnnotation(CharacterSetAnnotation, CharacterSet);
        ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
    }
}
