using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.Spike.VariantB.Host;

public static class VariantBContextFactory
{
    public static SecretsDbContext CreateSqlite(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SecretsDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new SecretsDbContext(options);
    }

    public static SecretsDbContext CreatePostgreSql(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SecretsDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new SecretsDbContext(options);
    }
}
