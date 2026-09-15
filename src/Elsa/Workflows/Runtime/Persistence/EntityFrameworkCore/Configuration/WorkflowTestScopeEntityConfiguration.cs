using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

public sealed class WorkflowTestScopeEntityConfiguration : IEntityTypeConfiguration<WorkflowTestScopeEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowTestScopeEntity> b)
    {
        b.ToTable(RuntimeWorkflowTestScopeEfModule.TableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(RuntimeWorkflowTestScopeEfModule.IdentityProjectionMaximumLength).IsRequired();
        b.Property(x => x.AccessScopeKey).HasMaxLength(RuntimeWorkflowTestScopeEfModule.TenantProjectionMaximumLength).IsRequired();
        b.Property(x => x.AccessScopeKeyHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ScopeId).HasMaxLength(RuntimeWorkflowTestScopeEfModule.IdentityProjectionMaximumLength).IsRequired();
        b.Property(x => x.ScopeIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ScopeIdOrderKey).HasMaxLength(RuntimeWorkflowTestScopeEfModule.OrderKeyMaximumLength).IsRequired();
        b.Property(x => x.TenantId).HasMaxLength(RuntimeWorkflowTestScopeEfModule.TenantProjectionMaximumLength);
        b.Property(x => x.TenantIdHash).HasMaxLength(64);
        b.Property(x => x.Partition).HasMaxLength(RuntimeWorkflowTestScopeEfModule.TenantProjectionMaximumLength).IsRequired();
        b.Property(x => x.PartitionHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.PartitionOrderKey).HasMaxLength(RuntimeWorkflowTestScopeEfModule.OrderKeyMaximumLength).IsRequired();
        b.Property(x => x.ContentJson).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(16).IsRequired();
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasIndex(x => new { x.AccessScopeKeyHash, x.ScopeIdHash, x.ScopeId }).IsUnique();
        b.HasIndex(x => new { x.AccessScopeKeyHash, x.State, x.ExpiresAtUtcTicks, x.ScopeIdOrderKey });
    }
}
