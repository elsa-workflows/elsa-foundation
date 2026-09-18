using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore;

public sealed class Elsa3ImportSqliteDbContext(DbContextOptions<Elsa3ImportSqliteDbContext> options) : Elsa3ImportDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => ConfigureOrdinalCollation(modelBuilder, "BINARY");
}

public sealed class Elsa3ImportSqlServerDbContext(DbContextOptions<Elsa3ImportSqlServerDbContext> options) : Elsa3ImportDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => ConfigureOrdinalCollation(modelBuilder, "Latin1_General_100_BIN2");
}

public sealed class Elsa3ImportPostgreSqlDbContext(DbContextOptions<Elsa3ImportPostgreSqlDbContext> options) : Elsa3ImportDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => ConfigureOrdinalCollation(modelBuilder, "C");
}

public sealed class Elsa3ImportMySqlDbContext(DbContextOptions<Elsa3ImportMySqlDbContext> options) : Elsa3ImportDbContext(options)
{
    private const string CharacterSetAnnotation = "MySQL:Charset";
    private const string CollationAnnotation = "MySQL:Collation";
    public const string ExpectedProviderName = EfProviderNames.MySql;
    public const string CharacterSet = "utf8mb4";
    public const string Collation = "utf8mb4_0900_bin";

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Model.SetAnnotation(CharacterSetAnnotation, CharacterSet);
        modelBuilder.UseCollation(Collation);
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            entityType.SetAnnotation(CollationAnnotation, Collation);
        ConfigureOrdinalCollation(modelBuilder, Collation);
    }
}
