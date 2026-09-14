using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

public sealed class WorkflowExecutionStateEntityConfiguration : IEntityTypeConfiguration<WorkflowExecutionStateEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowExecutionStateEntity> b)
    {
        b.ToTable(RuntimeWorkflowExecutionEfModule.TableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(64).IsRequired();
        b.Property(x => x.ScopeKey).IsRequired();
        b.Property(x => x.ScopeKeyHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.WorkflowExecutionId).HasMaxLength(RuntimeWorkflowExecutionEfModule.IdentityProjectionMaximumLength).IsRequired();
        b.Property(x => x.WorkflowExecutionIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.WorkflowExecutionIdOrderKey).HasMaxLength(RuntimeWorkflowExecutionEfModule.OrderKeyMaximumLength).IsRequired();
        b.Property(x => x.TenantId).HasMaxLength(RuntimeWorkflowExecutionEfModule.IdentityProjectionMaximumLength);
        b.Property(x => x.TenantIdHash).HasMaxLength(64);
        b.Property(x => x.DefinitionId).HasMaxLength(RuntimeWorkflowExecutionEfModule.IdentityProjectionMaximumLength).IsRequired();
        b.Property(x => x.DefinitionIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.CorrelationId).HasMaxLength(RuntimeWorkflowExecutionEfModule.IdentityProjectionMaximumLength);
        b.Property(x => x.CorrelationIdHash).HasMaxLength(64);
        b.Property(x => x.ArtifactId).HasMaxLength(RuntimeWorkflowExecutionEfModule.IdentityProjectionMaximumLength).IsRequired();
        b.Property(x => x.ArtifactIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ArtifactIdOrderKey).HasMaxLength(RuntimeWorkflowExecutionEfModule.OrderKeyMaximumLength).IsRequired();
        b.Property(x => x.AuthorityPartitionKey).HasMaxLength(64);
        b.Property(x => x.ContentJson).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.Property(x => x.Revision).IsConcurrencyToken().IsRequired();
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.WorkflowExecutionId }).IsUnique();
        b.HasIndex(x => new { x.ScopeKeyHash, x.SortTimestampUtcTicks, x.WorkflowExecutionIdOrderKey, x.WorkflowExecutionId });
        b.HasIndex(x => new { x.ScopeKeyHash, x.TenantIdHash, x.AuthorityPartitionKey, x.WorkflowExecutionIdOrderKey, x.WorkflowExecutionId });
        b.HasIndex(x => new { x.ScopeKeyHash, x.ArtifactIdHash, x.ArtifactId });
        b.HasIndex(x => new { x.ScopeKeyHash, x.Status, x.SortTimestampUtcTicks, x.WorkflowExecutionIdOrderKey });
    }
}
