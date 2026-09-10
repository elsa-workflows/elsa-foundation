using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling;

public sealed class SecretsPostgreSqlDesignTimeFactory : IDesignTimeDbContextFactory<SecretsPostgreSqlDbContext>
{
    public SecretsPostgreSqlDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<SecretsPostgreSqlDbContext>();
        builder.UseNpgsql(
            "Host=localhost;Database=elsa_secrets_design;Username=postgres;Password=postgres",
            npgsql => npgsql
                .MigrationsAssembly(typeof(SecretsPostgreSqlDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(EfMigrationsHistory.TableName(SecretsEfModule.HistoryModuleName)));
        return new SecretsPostgreSqlDbContext(builder.Options);
    }
}
