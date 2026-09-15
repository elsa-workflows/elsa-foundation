using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

public sealed class WorkflowSchedulerPoisonEntityConfiguration : IEntityTypeConfiguration<WorkflowSchedulerPoisonEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowSchedulerPoisonEntity> b)
    {
        b.ToTable(RuntimeSchedulerPoisonEfModule.TableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(64).IsRequired();
        b.Property(x => x.ScopeKey).HasMaxLength(RuntimeSchedulerPoisonEfModule.ScopeProjectionMaximumLength).IsRequired();
        b.Property(x => x.ScopeKeyHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.WorkflowExecutionId).HasMaxLength(RuntimeSchedulerPoisonEfModule.IdentityMaximumLength).IsRequired();
        b.Property(x => x.WorkflowExecutionIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.WorkflowExecutionIdOrderKey).HasMaxLength(RuntimeSchedulerPoisonEfModule.OrderKeyMaximumLength).IsRequired();
        b.Property(x => x.WorkItemId).HasMaxLength(RuntimeSchedulerPoisonEfModule.IdentityMaximumLength).IsRequired();
        b.Property(x => x.WorkItemIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.WorkItemIdOrderKey).HasMaxLength(RuntimeSchedulerPoisonEfModule.OrderKeyMaximumLength).IsRequired();
        b.Property(x => x.FirstFailedAtUtcTicks).IsRequired();
        b.Property(x => x.LastFailedAtUtcTicks).IsRequired();
        b.Property(x => x.ContentJson).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.Property(x => x.Revision).IsConcurrencyToken().IsRequired();
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.WorkItemIdHash }).IsUnique();
        b.HasIndex(x => new
        {
            x.ScopeKeyHash,
            x.WorkflowExecutionIdHash,
            x.FirstFailedAtUtcTicks,
            x.LastFailedAtUtcTicks,
            x.WorkItemIdOrderKey,
            x.WorkItemIdHash
        });
    }
}
