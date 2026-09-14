using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Globalization;

namespace Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Configuration;

public sealed class StudioPreferenceRecordConfiguration : IEntityTypeConfiguration<StudioPreferenceRecord>
{
    public void Configure(EntityTypeBuilder<StudioPreferenceRecord> builder)
    {
        builder.ToTable(StudioPreferencesEfModule.TableName);
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasMaxLength(64).IsRequired();
        builder.Property(record => record.SubjectId).IsRequired();
        builder.Property(record => record.TenantId).IsRequired();
        builder.Property(record => record.StudioHostId).HasMaxLength(128).IsRequired();
        builder.Property(record => record.Namespace).IsRequired();
        builder.Property(record => record.SchemaVersion).IsRequired();
        builder.Property(record => record.ValueJson).IsRequired();
        builder.Property(record => record.UpdatedAt)
            .HasConversion(
                value => value.ToString("O", CultureInfo.InvariantCulture),
                value => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
            .IsRequired();
        builder.Property(record => record.Revision).IsRequired().IsConcurrencyToken();
    }
}
