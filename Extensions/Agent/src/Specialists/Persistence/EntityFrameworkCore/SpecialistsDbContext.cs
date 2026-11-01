using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore;

/// <summary>Specialist-owned journal model; installing mappings in a consumer context is also supported.</summary>
public abstract class SpecialistsDbContext : DbContext
{
    protected SpecialistsDbContext(DbContextOptions options) : base(options) { }
    internal DbSet<SpecialistTurnRecord> Turns => Set<SpecialistTurnRecord>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.AddSpecialistsModel(Database.ProviderName!);
        modelBuilder.MapSchemaFinalization(SpecialistsEfModule.HistoryModuleName);
        ConfigureProvider(modelBuilder);
        modelBuilder.IndexSchemaVersionStamps();
        EfOrdinalCollation.Apply(modelBuilder, Database.ProviderName!);
    }
    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}
