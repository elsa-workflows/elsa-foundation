using Elsa.Foundation.DataProtection.EntityFrameworkCore.Configuration;
using Elsa.Foundation.DataProtection.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.DataProtection.EntityFrameworkCore;

/// <summary>
/// The provider-neutral key ring model: one table, one row per stored element. Provider-derived contexts stay separate so
/// each engine owns its migrations without leaking an engine into this module.
/// </summary>
public abstract class DataProtectionKeysDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<DataProtectionKeyEntity> Keys => Set<DataProtectionKeyEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The host's optional schema; nothing changes when none is configured.
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.ApplyConfiguration(new DataProtectionKeyEntityConfiguration());
        // After this module's own table, so the stamp index covers both finalization tables (spec 181, FR-002).
        modelBuilder.MapSchemaFinalization(DataProtectionKeysEfModule.HistoryModuleName);
        modelBuilder.IndexSchemaVersionStamps();
    }
}
