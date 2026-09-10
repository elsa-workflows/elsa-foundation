using Elsa.Persistence.Spike.VariantA;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Elsa.Persistence.Spike.VariantA.Tooling;

public sealed class SecretsPostgreSqlDesignTimeFactory : IDesignTimeDbContextFactory<SecretsPostgreSqlDbContext>
{
    public SecretsPostgreSqlDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SecretsPostgreSqlDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=elsa_spike_design_time;Username=postgres;Password=postgres",
                npgsql => npgsql
                    .MigrationsAssembly(typeof(SecretsPostgreSqlDesignTimeFactory).Assembly.GetName().Name)
                    .MigrationsHistoryTable(SchemaNames.EfHistoryTable))
            .Options;

        return new SecretsPostgreSqlDbContext(options);
    }
}
