using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
namespace Elsa.Decisions.Persistence.EntityFrameworkCore;
internal sealed class DecisionsSqlServerDbContext(DbContextOptions<DecisionsSqlServerDbContext> options) : DecisionsDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) {  }
}
