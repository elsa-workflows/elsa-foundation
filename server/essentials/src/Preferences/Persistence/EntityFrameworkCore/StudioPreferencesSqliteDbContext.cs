using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;

public sealed class StudioPreferencesSqliteDbContext(DbContextOptions<StudioPreferencesSqliteDbContext> options)
    : StudioPreferencesDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.Sqlite;

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StudioPreferenceRecord>(entity =>
        {
            entity.Property(record => record.ValueJson).HasColumnType("TEXT");
            entity.Property(record => record.UpdatedAt).HasColumnType("TEXT");
        });
    }
}
