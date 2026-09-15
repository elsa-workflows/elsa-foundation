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
    private const string CharacterSetAnnotation = "MySQL:Charset";
    private const string CollationAnnotation = "MySQL:Collation";
    public const string CharacterSet = "utf8mb4";
    public const string Collation = "utf8mb4_0900_bin";

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        ConfigureDateTime(modelBuilder, "datetime(6)");
        ConfigureText(modelBuilder, "longtext");
        modelBuilder.Model.SetAnnotation(CharacterSetAnnotation, CharacterSet);
        modelBuilder.UseCollation(Collation);
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            entityType.SetAnnotation(CollationAnnotation, Collation);
        ConfigureOrdinalCollation(modelBuilder, Collation);
    }
}
