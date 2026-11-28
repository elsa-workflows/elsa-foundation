using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

internal sealed class RuntimeKernelScheduleOccurrenceEntityConfiguration : IEntityTypeConfiguration<RuntimeKernelScheduleOccurrenceEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeKernelScheduleOccurrenceEntity> builder)
    {
        builder.ToTable(RuntimeKernelScheduleOccurrenceEfModule.TableName);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ScopeKey).IsRequired();
        builder.Property(row => row.ScopeKeyHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.TenantKey).IsRequired();
        builder.Property(row => row.TenantKeyHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ArtifactId).HasMaxLength(RuntimeArtifactEfModule.IdentityProjectionMaximumLength).IsRequired();
        builder.Property(row => row.ContentJson).IsRequired();
        builder.Property(row => row.SchemaVersion).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.ActorReference).HasMaxLength(64);

        builder.HasIndex(row => new { row.ScopeKeyHash, row.TenantKeyHash, row.ActorReference, row.AcceptedAtUtcTicks, row.Id })
            .HasDatabaseName("IX_KernelScheduleOccurrence_Pending");
        builder.HasIndex(row => new { row.ScopeKeyHash, row.RetiredAtUtcTicks, row.ArtifactId })
            .HasDatabaseName("IX_KernelScheduleOccurrence_ArtifactPin");
        builder.HasIndex(row => new { row.ScopeKeyHash, row.TenantKeyHash, row.RetiredAtUtcTicks, row.AcceptedAtUtcTicks, row.Id })
            .HasDatabaseName("IX_KernelScheduleOccurrence_Open");
    }
}
