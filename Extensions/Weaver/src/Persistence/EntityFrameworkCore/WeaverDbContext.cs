using Elsa.Weaver.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Weaver.Persistence.EntityFrameworkCore;

/// <summary>Agent-owned session, record and audit model; installing the mappings in a consumer context is also supported.</summary>
public abstract class WeaverDbContext : DbContext
{
    protected WeaverDbContext(DbContextOptions options) : base(options) { }
    internal DbSet<WeaverSessionRow> Sessions => Set<WeaverSessionRow>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.AddWeaverModel(Database.ProviderName!);
        modelBuilder.MapSchemaFinalization(WeaverEfModule.HistoryModuleName);
        ConfigureProvider(modelBuilder);
        modelBuilder.IndexSchemaVersionStamps();
        EfOrdinalCollation.Apply(modelBuilder, Database.ProviderName!);
    }
    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}
