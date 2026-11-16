using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore;

public sealed class Elsa3ImportSqliteDbContext(DbContextOptions<Elsa3ImportSqliteDbContext> options) : Elsa3ImportDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
}

public sealed class Elsa3ImportSqlServerDbContext(DbContextOptions<Elsa3ImportSqlServerDbContext> options) : Elsa3ImportDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
}

public sealed class Elsa3ImportPostgreSqlDbContext(DbContextOptions<Elsa3ImportPostgreSqlDbContext> options) : Elsa3ImportDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
}

public sealed class Elsa3ImportMySqlDbContext(DbContextOptions<Elsa3ImportMySqlDbContext> options) : Elsa3ImportDbContext(options)
{
    private const string CharacterSetAnnotation = "MySQL:Charset";
    public const string ExpectedProviderName = EfProviderNames.MySql;
    public const string CharacterSet = "utf8mb4";

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Model.SetAnnotation(CharacterSetAnnotation, CharacterSet);
        ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
    }
}
