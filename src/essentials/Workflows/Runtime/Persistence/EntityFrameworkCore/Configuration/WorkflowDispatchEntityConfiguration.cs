using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

public sealed class WorkflowDispatchEntityConfiguration : IEntityTypeConfiguration<WorkflowDispatchEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowDispatchEntity> b)
    {
        b.ToTable(RuntimeWorkflowDispatchEfModule.TableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(64).IsRequired();
        b.Property(x => x.ScopeKey).HasMaxLength(RuntimeWorkflowDispatchEfModule.ScopeProjectionMaximumLength).IsRequired();
        b.Property(x => x.ScopeKeyHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.DispatchId).IsRequired();
        b.Property(x => x.DispatchIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.DispatchIdOrderKey).HasMaxLength(RuntimeWorkflowDispatchEfModule.IdentityMaximumLength * 4).IsRequired();
        b.Property(x => x.ParentWorkflowExecutionId).IsRequired();
        b.Property(x => x.ParentWorkflowExecutionIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ParentWorkflowExecutionIdOrderKey).HasMaxLength(RuntimeWorkflowDispatchEfModule.OrderKeyMaximumLength).IsRequired();
        b.Property(x => x.ParentActivityExecutionId).IsRequired();
        b.Property(x => x.ParentActivityExecutionIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ParentActivityExecutionIdOrderKey).HasMaxLength(RuntimeWorkflowDispatchEfModule.OrderKeyMaximumLength).IsRequired();
        b.Property(x => x.ChildWorkflowExecutionId).IsRequired();
        b.Property(x => x.ChildWorkflowExecutionIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ChildWorkflowExecutionIdOrderKey).HasMaxLength(RuntimeWorkflowDispatchEfModule.OrderKeyMaximumLength).IsRequired();
        b.Property(x => x.ChildArtifactId).IsRequired();
        b.Property(x => x.ChildArtifactIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ChildArtifactIdOrderKey).HasMaxLength(RuntimeWorkflowDispatchEfModule.OrderKeyMaximumLength).IsRequired();
        b.Property(x => x.TestScopeId);
        b.Property(x => x.TestScopeIdHash).HasMaxLength(64);
        b.Property(x => x.TestScopeIdOrderKey).HasMaxLength(RuntimeWorkflowDispatchEfModule.OrderKeyMaximumLength);
        b.Property(x => x.TenantId);
        b.Property(x => x.TenantIdHash).HasMaxLength(64);
        b.Property(x => x.ContentJson).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.Property(x => x.Revision).IsConcurrencyToken().IsRequired();
        // Keep the uniqueness key binary-safe for MySQL, where an indexed TEXT column requires a provider-specific
        // prefix length. The full encoded identity is still validated on every read and the deterministic row ID
        // prevents ordinary duplicate inserts.
        b.HasIndex(x => new { x.ScopeKeyHash, x.DispatchIdHash }).IsUnique();
        b.HasIndex(x => new { x.ScopeKeyHash, x.ParentWorkflowExecutionIdHash, x.CreatedAtUtcTicks });
        b.HasIndex(x => new { x.ScopeKeyHash, x.ChildWorkflowExecutionIdHash, x.CreatedAtUtcTicks });
        b.HasIndex(x => new { x.ScopeKeyHash, x.Status, x.CreatedAtUtcTicks });
        b.HasIndex(x => new { x.ScopeKeyHash, x.TestScopeIdHash, x.CreatedAtUtcTicks });
    }
}
