using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;

public sealed class StudioPreferencesPostgreSqlDbContext(DbContextOptions<StudioPreferencesPostgreSqlDbContext> options)
    : StudioPreferencesDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.PostgreSql;

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StudioPreferenceRecord>(entity =>
        {
            entity.Property(record => record.ValueJson).HasColumnType("text");
            entity.Property(record => record.UpdatedAt).HasColumnType("text");
        });
    }
}
