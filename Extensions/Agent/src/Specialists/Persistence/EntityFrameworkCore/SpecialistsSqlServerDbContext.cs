using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore;

public sealed class SpecialistsSqlServerDbContext(DbContextOptions<SpecialistsSqlServerDbContext> options) : SpecialistsDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SpecialistTurnRecord>().Property(x => x.Payload).HasColumnType("nvarchar(max)");
    }
}
