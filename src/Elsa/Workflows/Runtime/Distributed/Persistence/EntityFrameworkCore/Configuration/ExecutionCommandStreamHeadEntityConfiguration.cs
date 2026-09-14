using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Configuration;

public sealed class ExecutionCommandStreamHeadEntityConfiguration : IEntityTypeConfiguration<ExecutionCommandStreamHeadEntity>
{
    public void Configure(EntityTypeBuilder<ExecutionCommandStreamHeadEntity> builder)
    {
        builder.ToTable(ExecutionCommandTransportEfModule.StreamHeadTableName);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ScopeKey).IsRequired();
        builder.Property(row => row.ScopeKeyHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.WorkflowExecutionId).HasMaxLength(128).IsRequired();
        builder.Property(row => row.WorkflowExecutionIdHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.WorkflowExecutionIdOrderKey)
            .HasMaxLength(ExecutionCommandTransportEfModule.WorkflowExecutionIdOrderKeyWidth)
            .IsRequired();
        builder.Property(row => row.LastSequence).IsRequired();
        builder.Property(row => row.PendingCount).IsRequired();
        builder.Property(row => row.PendingVisibleAtUtcTicks).IsRequired();
        builder.Property(row => row.PendingSequence).IsRequired();
        builder.Property(row => row.Revision).IsRequired().IsConcurrencyToken();
        builder.HasIndex(row => new { row.ScopeKeyHash, row.WorkflowExecutionIdHash }).IsUnique();
        builder.HasIndex(row => new { row.ScopeKeyHash, row.PendingVisibleAtUtcTicks, row.WorkflowExecutionIdOrderKey, row.Id });
        builder.HasIndex(row => new { row.ScopeKeyHash, row.WorkflowExecutionIdHash, row.PendingCount });
    }
}
