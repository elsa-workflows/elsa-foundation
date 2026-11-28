using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore;

public sealed class ExternalAuthenticationSqliteDbContext(DbContextOptions<ExternalAuthenticationSqliteDbContext> options)
    : ExternalAuthenticationDbContext(options)
{
    protected override string ExpectedProvider => EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder builder)
    {
        Collation(builder, ExpectedProvider);
    }
}

public sealed class ExternalAuthenticationSqlServerDbContext(DbContextOptions<ExternalAuthenticationSqlServerDbContext> options)
    : ExternalAuthenticationDbContext(options)
{
    protected override string ExpectedProvider => EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder builder)
    {
        Collation(builder, ExpectedProvider);
    }
}

public sealed class ExternalAuthenticationPostgreSqlDbContext(DbContextOptions<ExternalAuthenticationPostgreSqlDbContext> options)
    : ExternalAuthenticationDbContext(options)
{
    protected override string ExpectedProvider => EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder builder)
    {
        builder.Model.RemoveAnnotation("Npgsql:ValueGenerationStrategy");
        Collation(builder, ExpectedProvider);
    }
}

public sealed class ExternalAuthenticationMySqlDbContext(DbContextOptions<ExternalAuthenticationMySqlDbContext> options)
    : ExternalAuthenticationDbContext(options)
{
    protected override string ExpectedProvider => EfProviderNames.MySql;
    protected override void ConfigureProvider(ModelBuilder builder)
    {
        builder.Model.SetAnnotation("MySQL:Charset", "utf8mb4");
        Collation(builder, ExpectedProvider);
    }
}
