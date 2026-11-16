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
        b.Property(x => x.ScopeKey).HasMaxLength(RuntimeWorkflowExecutionEfModule.TenantProjectionMaximumLength).IsRequired();
        b.Property(x => x.ScopeKeyHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.WorkflowExecutionId).HasMaxLength(RuntimeWorkflowExecutionEfModule.IdentityProjectionMaximumLength).IsRequired();
        b.Property(x => x.WorkflowExecutionIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.WorkflowExecutionIdOrderKey).HasMaxLength(RuntimeWorkflowExecutionEfModule.OrderKeyMaximumLength).IsRequired();
        b.Property(x => x.TenantId).HasMaxLength(RuntimeWorkflowExecutionEfModule.TenantProjectionMaximumLength);
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
        // Hashes identify opaque values exactly; the order projection already
        // contains the complete identity and is therefore the deterministic
        // tie-breaker.  Do not append the encoded identity to these indexes:
        // the redundant wide columns exceed SQL Server/MySQL key limits.
        // Keep the encoded identity on the uniqueness boundary as well as its
        // hash: this preserves exact identity semantics even if a hash ever
        // collides, while remaining below SQL Server/MySQL key limits.
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.WorkflowExecutionId }).IsUnique();
        b.HasIndex(x => new { x.ScopeKeyHash, x.SortTimestampUtcTicks, x.WorkflowExecutionIdOrderKey });
        b.HasIndex(x => new { x.ScopeKeyHash, x.TenantIdHash, x.AuthorityPartitionKey, x.WorkflowExecutionIdOrderKey });
        b.HasIndex(x => new { x.ScopeKeyHash, x.ArtifactIdHash });
        b.HasIndex(x => new { x.ScopeKeyHash, x.Status, x.SortTimestampUtcTicks, x.WorkflowExecutionIdOrderKey });
    }
}
