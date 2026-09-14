using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Configuration;

public sealed class ExecutionCommandTransportItemEntityConfiguration : IEntityTypeConfiguration<ExecutionCommandTransportItemEntity>
{
    public void Configure(EntityTypeBuilder<ExecutionCommandTransportItemEntity> builder)
    {
        builder.ToTable(ExecutionCommandTransportEfModule.TransportItemTableName);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ScopeKey).IsRequired();
        builder.Property(row => row.ScopeKeyHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.TransportItemId).HasMaxLength(ExecutionCommandTransportEfModule.TransportItemIdMaximumLength).IsRequired();
        builder.Property(row => row.TransportItemIdHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.WorkflowExecutionId).HasMaxLength(128).IsRequired();
        builder.Property(row => row.WorkflowExecutionIdHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Sequence).IsRequired();
        builder.Property(row => row.EnqueuedAtUtcTicks).IsRequired();
        builder.Property(row => row.EnqueuedAtOffsetMinutes).IsRequired();
        builder.Property(row => row.VisibleAtUtcTicks).IsRequired();
        builder.Property(row => row.LeaseOwnerId).HasMaxLength(128);
        builder.Property(row => row.LeaseToken).IsRequired();
        builder.Property(row => row.LeaseExpiresAtUtcTicks).IsRequired();
        builder.Property(row => row.LeaseExpiresAtOffsetMinutes).IsRequired();
        builder.Property(row => row.PayloadJson).IsRequired();
        builder.Property(row => row.Revision).IsRequired().IsConcurrencyToken();
        builder.HasIndex(row => new { row.ScopeKeyHash, row.WorkflowExecutionIdHash, row.Sequence }).IsUnique();
        builder.HasIndex(row => new { row.ScopeKeyHash, row.WorkflowExecutionIdHash, row.VisibleAtUtcTicks, row.Sequence, row.TransportItemIdHash });
    }
}
