using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
namespace Elsa.Decisions.Persistence.EntityFrameworkCore;
internal sealed class DecisionsSqliteDbContext(DbContextOptions<DecisionsSqliteDbContext> options) : DecisionsDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) {  }
}
