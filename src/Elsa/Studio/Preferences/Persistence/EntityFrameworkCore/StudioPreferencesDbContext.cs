using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;

/// <summary>
/// Shared Studio Preferences model. Provider-derived contexts remain separate so each provider can
/// own its model annotations and migration artifact without leaking an engine into this module.
/// </summary>
public abstract class StudioPreferencesDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<StudioPreferenceRecord> Preferences => Set<StudioPreferenceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new StudioPreferenceRecordConfiguration());
        ConfigureProvider(modelBuilder);
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}
