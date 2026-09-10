using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling;

public sealed class SecretsSqliteDesignTimeFactory : IDesignTimeDbContextFactory<SecretsSqliteDbContext>
{
    public SecretsSqliteDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<SecretsSqliteDbContext>();
        builder.UseSqlite(
            "Data Source=elsa-secrets-design.db",
            sqlite => sqlite
                .MigrationsAssembly(typeof(SecretsSqliteDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(EfMigrationsHistory.TableName(SecretsEfModule.HistoryModuleName)));
        return new SecretsSqliteDbContext(builder.Options);
    }
}
