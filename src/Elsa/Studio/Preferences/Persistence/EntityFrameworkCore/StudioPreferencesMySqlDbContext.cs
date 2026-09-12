using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;

public sealed class StudioPreferencesMySqlDbContext(DbContextOptions<StudioPreferencesMySqlDbContext> options)
    : StudioPreferencesDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.MySql;

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StudioPreferenceRecord>(entity =>
        {
            // Keep the original JSON text verbatim. A native JSON column can normalize textual
            // representation (including property order), which would change the document contract.
            entity.Property(record => record.ValueJson).HasColumnType("longtext");
            entity.Property(record => record.UpdatedAt).HasColumnType("varchar(64)");
        });
    }
}
