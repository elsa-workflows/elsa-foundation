using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Configurations;

public sealed class PersistedTelemetryResourceConfiguration : IEntityTypeConfiguration<PersistedTelemetryResource>
{
    public void Configure(EntityTypeBuilder<PersistedTelemetryResource> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(512);
        builder.Property(x => x.ServiceName).HasMaxLength(512);
        builder.Property(x => x.ServiceInstanceId).HasMaxLength(512);
        builder.Property(x => x.TelemetrySdkLanguage).HasMaxLength(128);
        builder.Property(x => x.AttributesJson).HasMaxLength(-1);
        builder.HasIndex(x => x.ServiceName).HasDatabaseName($"IX_{nameof(PersistedTelemetryResource)}_{nameof(PersistedTelemetryResource.ServiceName)}");
        builder.HasIndex(x => x.LastSeen).HasDatabaseName($"IX_{nameof(PersistedTelemetryResource)}_{nameof(PersistedTelemetryResource.LastSeen)}");
        builder.HasIndex(x => x.Status).HasDatabaseName($"IX_{nameof(PersistedTelemetryResource)}_{nameof(PersistedTelemetryResource.Status)}");
    }
}
