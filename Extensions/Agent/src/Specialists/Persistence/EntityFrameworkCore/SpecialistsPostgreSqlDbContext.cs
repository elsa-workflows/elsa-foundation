using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore;

public sealed class SpecialistsPostgreSqlDbContext(DbContextOptions<SpecialistsPostgreSqlDbContext> options) : SpecialistsDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Model.RemoveAnnotation("Npgsql:ValueGenerationStrategy");
        modelBuilder.Entity<SpecialistTurnRecord>().Property(x => x.Payload).HasColumnType("jsonb");
    }
}
