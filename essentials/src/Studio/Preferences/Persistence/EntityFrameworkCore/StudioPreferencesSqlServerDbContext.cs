using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;

public sealed class StudioPreferencesSqlServerDbContext(DbContextOptions<StudioPreferencesSqlServerDbContext> options)
    : StudioPreferencesDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.SqlServer;

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StudioPreferenceRecord>(entity =>
        {
            entity.Property(record => record.ValueJson).HasColumnType("nvarchar(max)");
            entity.Property(record => record.UpdatedAt).HasColumnType("nvarchar(64)");
        });
    }
}
