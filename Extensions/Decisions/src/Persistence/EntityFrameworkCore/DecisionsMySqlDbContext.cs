using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
namespace Elsa.Decisions.Persistence.EntityFrameworkCore;
internal sealed class DecisionsMySqlDbContext(DbContextOptions<DecisionsMySqlDbContext> options) : DecisionsDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.MySql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) { modelBuilder.Model.SetAnnotation("MySQL:Charset", "utf8mb4"); }
}
