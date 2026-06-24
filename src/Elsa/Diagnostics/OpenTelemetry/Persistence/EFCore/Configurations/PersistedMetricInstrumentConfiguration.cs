using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Configurations;

public sealed class PersistedMetricInstrumentConfiguration : IEntityTypeConfiguration<PersistedMetricInstrument>
{
    public void Configure(EntityTypeBuilder<PersistedMetricInstrument> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(512);
        builder.Property(x => x.ResourceId).HasMaxLength(512);
        builder.Property(x => x.Name).HasMaxLength(512);
        builder.Property(x => x.Unit).HasMaxLength(128);
        builder.Property(x => x.Description).HasMaxLength(-1);
        builder.Property(x => x.AttributesJson).HasMaxLength(-1);
        builder.HasIndex(x => x.ResourceId).HasDatabaseName($"IX_{nameof(PersistedMetricInstrument)}_{nameof(PersistedMetricInstrument.ResourceId)}");
        builder.HasIndex(x => x.Name).HasDatabaseName($"IX_{nameof(PersistedMetricInstrument)}_{nameof(PersistedMetricInstrument.Name)}");
    }
}
