using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling;

public sealed class SecretsSqlServerDesignTimeFactory : IDesignTimeDbContextFactory<SecretsSqlServerDbContext>
{
    public SecretsSqlServerDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<SecretsSqlServerDbContext>();
        builder.UseSqlServer(
            SecretsDesignTimeConnection.Resolve(
                SecretsDesignTimeConnection.SqlServerVariable,
                "Server=localhost;Database=elsa-secrets-design;Trusted_Connection=True;TrustServerCertificate=True"),
            sqlServer => sqlServer
                .MigrationsAssembly(typeof(SecretsSqlServerDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(EfMigrationsHistory.TableName(SecretsEfModule.HistoryModuleName)));
        return new SecretsSqlServerDbContext(builder.Options);
    }
}
