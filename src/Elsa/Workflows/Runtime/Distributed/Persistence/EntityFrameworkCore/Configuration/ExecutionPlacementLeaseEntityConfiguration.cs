using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Globalization;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Configuration;

public sealed class ExecutionPlacementLeaseEntityConfiguration : IEntityTypeConfiguration<ExecutionPlacementLeaseEntity>
{
    public void Configure(EntityTypeBuilder<ExecutionPlacementLeaseEntity> builder)
    {
        builder.ToTable(ExecutionPlacementEfModule.TableName);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ScopeKey).IsRequired();
        builder.Property(row => row.ScopeKeyHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.WorkflowExecutionId).HasMaxLength(128).IsRequired();
        builder.Property(row => row.WorkflowExecutionIdOrderKey).HasMaxLength(512).IsRequired();
        builder.Property(row => row.OwnerId).HasMaxLength(128).IsRequired();
        builder.Property(row => row.OwnerIdHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.PlacementToken).IsRequired();
        builder.Property(row => row.AcquiredAt)
            .HasConversion(
                value => value.ToString("O", CultureInfo.InvariantCulture),
                value => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
            .IsRequired();
        builder.Property(row => row.ExpiresAt)
            .HasConversion(
                value => value.ToString("O", CultureInfo.InvariantCulture),
                value => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
            .IsRequired();
        builder.Property(row => row.ExpiresAtUtcTicks).IsRequired();
        builder.Property(row => row.Revision).IsRequired().IsConcurrencyToken();
        builder.HasIndex(row => new
        {
            row.ScopeKeyHash,
            row.OwnerIdHash,
            row.ExpiresAtUtcTicks,
            row.WorkflowExecutionIdOrderKey
        });
    }
}
