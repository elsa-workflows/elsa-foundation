using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Weaver.Persistence.EntityFrameworkCore;

public sealed class WeaverPostgreSqlDbContext(DbContextOptions<WeaverPostgreSqlDbContext> options) : WeaverDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Model.RemoveAnnotation("Npgsql:ValueGenerationStrategy");
    }
}
