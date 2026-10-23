using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

public sealed class WorkflowAlterationPlanEntityConfiguration : IEntityTypeConfiguration<WorkflowAlterationPlanEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowAlterationPlanEntity> b)
    {
        b.ToTable(RuntimeWorkflowAlterationEfModule.PlanTableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(RuntimeWorkflowAlterationEfModule.IdentityProjectionMaximumLength).IsRequired();
        b.Property(x => x.ScopeKey).HasMaxLength(RuntimeWorkflowAlterationEfModule.TenantProjectionMaximumLength).IsRequired();
        b.Property(x => x.ScopeKeyHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.PlanId).HasMaxLength(RuntimeWorkflowAlterationEfModule.IdentityProjectionMaximumLength).IsRequired();
        b.Property(x => x.PlanIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.PlanIdOrderKey).HasMaxLength(RuntimeWorkflowAlterationEfModule.OrderKeyMaximumLength).IsRequired();
        // This projection is tenant + a 256-bit request hash (321 UTF-16 units, 428 encoded bytes/chars).
        // Keep its declared width bounded so the composite unique key remains valid on MySQL (3072 bytes).
        b.Property(x => x.TenantIdempotencyKey).HasMaxLength(RuntimeWorkflowAlterationEfModule.IdentityProjectionMaximumLength).IsRequired();
        b.Property(x => x.TenantIdempotencyKeyHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ActiveOrderKey).HasMaxLength(RuntimeWorkflowAlterationEfModule.OrderKeyMaximumLength).IsRequired();
        b.Property(x => x.ContentJson).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(16).IsRequired();
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.Property(x => x.ActiveOrderKey).IsConcurrencyToken();
        b.Property(x => x.CleanupSafeFailureJson);
        b.HasIndex(x => new { x.ScopeKeyHash, x.PlanIdHash, x.PlanId }).IsUnique();
        b.HasIndex(x => new { x.ScopeKeyHash, x.TenantIdempotencyKeyHash, x.TenantIdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.ScopeKeyHash, x.Status, x.ActiveOrderKey });
    }
}

public sealed class WorkflowAlterationJobEntityConfiguration : IEntityTypeConfiguration<WorkflowAlterationJobEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowAlterationJobEntity> b)
    {
        b.ToTable(RuntimeWorkflowAlterationEfModule.JobTableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(RuntimeWorkflowAlterationEfModule.IdentityProjectionMaximumLength).IsRequired();
        foreach (var p in new[] { nameof(WorkflowAlterationJobEntity.ScopeKey), nameof(WorkflowAlterationJobEntity.PlanId), nameof(WorkflowAlterationJobEntity.WorkflowExecutionId), nameof(WorkflowAlterationJobEntity.TenantPartition) })
            b.Property<string>(p).HasMaxLength(RuntimeWorkflowAlterationEfModule.TenantProjectionMaximumLength).IsRequired();
        foreach (var p in new[] { nameof(WorkflowAlterationJobEntity.ScopeKeyHash), nameof(WorkflowAlterationJobEntity.PlanIdHash), nameof(WorkflowAlterationJobEntity.WorkflowExecutionIdHash), nameof(WorkflowAlterationJobEntity.TenantPartitionHash), nameof(WorkflowAlterationJobEntity.JobIdHash), nameof(WorkflowAlterationJobEntity.CheckpointCommitIdHash) })
            b.Property<string?>(p).HasMaxLength(64);
        b.Property(x => x.JobId).HasMaxLength(RuntimeWorkflowAlterationEfModule.IdentityProjectionMaximumLength).IsRequired();
        b.Property(x => x.JobIdOrderKey).HasMaxLength(RuntimeWorkflowAlterationEfModule.OrderKeyMaximumLength).IsRequired();
        b.Property(x => x.WorkflowExecutionIdOrderKey).HasMaxLength(RuntimeWorkflowAlterationEfModule.OrderKeyMaximumLength).IsRequired();
        b.Property(x => x.CheckpointCommitId).HasMaxLength(RuntimeWorkflowAlterationEfModule.IdentityProjectionMaximumLength);
        b.Property(x => x.ContentJson).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(16).IsRequired();
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasIndex(x => new { x.ScopeKeyHash, x.JobIdHash, x.JobId }).IsUnique();
        b.HasIndex(x => new { x.ScopeKeyHash, x.PlanIdHash, x.CaptureOrdinal, x.JobIdOrderKey });
        b.HasIndex(x => new { x.ScopeKeyHash, x.PlanIdHash, x.Status, x.ClaimableAtUtcTicks, x.JobIdOrderKey });
        b.HasIndex(x => new { x.ScopeKeyHash, x.CheckpointCommitIdHash, x.CheckpointCommitId });
    }
}
