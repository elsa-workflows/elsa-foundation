using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

public sealed class IdentityIamSqliteDbContext(DbContextOptions<IdentityIamSqliteDbContext> options)
    : IdentityIamDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.Sqlite;
    protected override string ExpectedProviderNameValue => ExpectedProviderName;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
    }
}

public sealed class IdentityIamSqlServerDbContext(DbContextOptions<IdentityIamSqlServerDbContext> options)
    : IdentityIamDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.SqlServer;
    protected override string ExpectedProviderNameValue => ExpectedProviderName;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
    }
}

public sealed class IdentityIamPostgreSqlDbContext(DbContextOptions<IdentityIamPostgreSqlDbContext> options)
    : IdentityIamDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.PostgreSql;
    protected override string ExpectedProviderNameValue => ExpectedProviderName;

    protected override void ConfigureProvider(ModelBuilder modelBuilder) =>
        modelBuilder.Model.RemoveAnnotation("Npgsql:ValueGenerationStrategy");
}

public sealed class IdentityIamMySqlDbContext(DbContextOptions<IdentityIamMySqlDbContext> options)
    : IdentityIamDbContext(options)
{
    private const string CharacterSetAnnotation = "MySQL:Charset";
    private const string CollationAnnotation = "MySQL:Collation";

    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.MySql;
    public const string CharacterSet = "utf8mb4";
    public const string Collation = "utf8mb4_0900_bin";
    protected override string ExpectedProviderNameValue => ExpectedProviderName;

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Model.SetAnnotation(CharacterSetAnnotation, CharacterSet);
        modelBuilder.UseCollation(Collation);
        modelBuilder.Entity<Entities.ApplicationEntity>().Metadata.SetAnnotation(CollationAnnotation, Collation);
        modelBuilder.Entity<Entities.CredentialEntity>().Metadata.SetAnnotation(CollationAnnotation, Collation);
    }
}
