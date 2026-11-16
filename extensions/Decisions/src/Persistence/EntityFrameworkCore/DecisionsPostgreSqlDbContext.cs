using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
namespace Elsa.Decisions.Persistence.EntityFrameworkCore;
internal sealed class DecisionsPostgreSqlDbContext(DbContextOptions<DecisionsPostgreSqlDbContext> options) : DecisionsDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) { modelBuilder.Model.RemoveAnnotation("Npgsql:ValueGenerationStrategy"); }
}
