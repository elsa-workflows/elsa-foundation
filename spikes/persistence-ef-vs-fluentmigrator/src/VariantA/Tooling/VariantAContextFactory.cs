using Elsa.Persistence.Spike.VariantA;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.Spike.VariantA.Tooling;

public static class VariantAContextFactory
{
    public static SecretsSqliteDbContext CreateSqlite(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SecretsSqliteDbContext>()
            .UseSqlite(
                connectionString,
                sqlite => sqlite
                    .MigrationsAssembly(typeof(SecretsSqliteDesignTimeFactory).Assembly.GetName().Name)
                    .MigrationsHistoryTable(SchemaNames.EfHistoryTable))
            .Options;

        return new SecretsSqliteDbContext(options);
    }

    public static SecretsPostgreSqlDbContext CreatePostgreSql(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SecretsPostgreSqlDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql
                    .MigrationsAssembly(typeof(SecretsPostgreSqlDesignTimeFactory).Assembly.GetName().Name)
                    .MigrationsHistoryTable(SchemaNames.EfHistoryTable))
            .Options;

        return new SecretsPostgreSqlDbContext(options);
    }
}
