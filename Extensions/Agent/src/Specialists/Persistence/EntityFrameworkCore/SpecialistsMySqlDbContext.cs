using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore;

public sealed class SpecialistsMySqlDbContext(DbContextOptions<SpecialistsMySqlDbContext> options) : SpecialistsDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.MySql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Model.SetAnnotation("MySQL:Charset", "utf8mb4");
        modelBuilder.Entity<SpecialistTurnRecord>().Property(x => x.Payload).HasColumnType("json");
    }
}
