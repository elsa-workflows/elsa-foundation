using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore;

public sealed class SpecialistsSqliteDbContext(DbContextOptions<SpecialistsSqliteDbContext> options) : SpecialistsDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SpecialistTurnRecord>().Property(x => x.Payload).HasColumnType("TEXT");
    }
}
