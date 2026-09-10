using Elsa.Persistence.Spike.VariantA;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Elsa.Persistence.Spike.VariantA.Tooling;

public sealed class SecretsSqliteDesignTimeFactory : IDesignTimeDbContextFactory<SecretsSqliteDbContext>
{
    public SecretsSqliteDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SecretsSqliteDbContext>()
            .UseSqlite(
                "Data Source=variant-a-design-time.sqlite",
                sqlite => sqlite
                    .MigrationsAssembly(typeof(SecretsSqliteDesignTimeFactory).Assembly.GetName().Name)
                    .MigrationsHistoryTable(SchemaNames.EfHistoryTable))
            .Options;

        return new SecretsSqliteDbContext(options);
    }
}
