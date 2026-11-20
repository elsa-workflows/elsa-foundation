using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

internal static class RuntimeOperationalStateEntityConfigurationHelpers
{
    public static void ConfigureCommon<T>(EntityTypeBuilder<T> b, string tableName) where T : class
    {
        b.ToTable(tableName);
        b.Property("ScopeKey").HasMaxLength(RuntimeOperationalStateEfModule.ScopeProjectionMaximumLength).IsRequired();
        b.Property("ScopeKeyHash").HasMaxLength(64).IsRequired();
        b.Property("ContentJson").IsRequired();
        b.Property("SchemaVersion").HasMaxLength(32).IsRequired();
        b.Property("Revision").IsConcurrencyToken().IsRequired();
    }

    public static void ConfigureIdentity<T>(EntityTypeBuilder<T> b, string property, bool nullable = false) where T : class
    {
        b.Property(property).HasMaxLength(RuntimeOperationalStateEfModule.IdentityProjectionMaximumLength);
        if (!nullable) b.Property(property).IsRequired();
    }

    public static void ConfigureHash<T>(EntityTypeBuilder<T> b, string property, bool nullable = false) where T : class
    {
        b.Property(property).HasMaxLength(64);
        if (!nullable) b.Property(property).IsRequired();
    }

    public static void ConfigureOrder<T>(EntityTypeBuilder<T> b, string property, bool nullable = false) where T : class
    {
        b.Property(property).HasMaxLength(RuntimeOperationalStateEfModule.OrderKeyMaximumLength);
        if (!nullable) b.Property(property).IsRequired();
    }
}

public sealed class ExecutionLivenessStateEntityConfiguration : IEntityTypeConfiguration<ExecutionLivenessStateEntity>
{
    public void Configure(EntityTypeBuilder<ExecutionLivenessStateEntity> b)
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeOperationalStateEfModule.ExecutionLivenessTableName);
        b.HasKey(x => x.Id); b.Property(x => x.Id).HasMaxLength(64);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(ExecutionLivenessStateEntity.WorkflowExecutionId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(ExecutionLivenessStateEntity.WorkflowExecutionIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(ExecutionLivenessStateEntity.WorkflowExecutionIdOrderKey));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(ExecutionLivenessStateEntity.OperationalStateId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(ExecutionLivenessStateEntity.OperationalStateIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(ExecutionLivenessStateEntity.OperationalStateIdOrderKey));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(ExecutionLivenessStateEntity.LeaseOwnerId), nullable: true);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(ExecutionLivenessStateEntity.HeartbeatOwnerId), nullable: true);
        b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.OperationalStateIdHash }).IsUnique();
        // Keep provider index keys below SQL Server's 1700-byte limit. The full order projections remain on the
        // rows for exact keyset ordering; the hash tie-breaker gives each route a compact, deterministic index key.
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdOrderKey, x.OperationalStateIdHash });
        b.HasIndex(x => new { x.ScopeKeyHash, x.InterruptedStatus, x.InterruptedAtUtcTicks, x.WorkflowExecutionIdOrderKey, x.OperationalStateIdHash });
        b.HasIndex(x => new { x.ScopeKeyHash, x.LeaseExpiresAtUtcTicks, x.WorkflowExecutionIdOrderKey, x.OperationalStateIdHash });
        b.HasIndex(x => new { x.ScopeKeyHash, x.LeaseAcquiredAtUtcTicks, x.WorkflowExecutionIdOrderKey, x.OperationalStateIdHash });
        b.HasIndex(x => new { x.ScopeKeyHash, x.HeartbeatRecordedAtUtcTicks, x.WorkflowExecutionIdOrderKey, x.OperationalStateIdHash });
    }
}

public sealed class WorkflowHoldStateEntityConfiguration : IEntityTypeConfiguration<WorkflowHoldStateEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowHoldStateEntity> b)
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeOperationalStateEfModule.WorkflowHoldTableName);
        b.HasKey(x => x.Id); b.Property(x => x.Id).HasMaxLength(64);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowHoldStateEntity.ControlPlaneStateId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(WorkflowHoldStateEntity.ControlPlaneStateIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(WorkflowHoldStateEntity.ControlPlaneStateIdOrderKey));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowHoldStateEntity.WorkflowExecutionId), nullable: true);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(WorkflowHoldStateEntity.WorkflowExecutionIdHash), nullable: true);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(WorkflowHoldStateEntity.WorkflowExecutionIdOrderKey), nullable: true);
        b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.ScopeKeyHash, x.ControlPlaneStateIdHash }).IsUnique();
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.WorkflowExecutionIdOrderKey });
        b.HasIndex(x => new { x.ScopeKeyHash, x.ControlPlaneStateIdOrderKey });
    }
}

public sealed class IncidentStateEntityConfiguration : IEntityTypeConfiguration<IncidentStateEntity>
{
    public void Configure(EntityTypeBuilder<IncidentStateEntity> b)
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeOperationalStateEfModule.IncidentTableName);
        b.HasKey(x => x.Id); b.Property(x => x.Id).HasMaxLength(64);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(IncidentStateEntity.WorkflowExecutionId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(IncidentStateEntity.WorkflowExecutionIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(IncidentStateEntity.WorkflowExecutionIdOrderKey));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(IncidentStateEntity.IncidentId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(IncidentStateEntity.IncidentIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(IncidentStateEntity.IncidentIdOrderKey));
        b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.IncidentIdHash }).IsUnique();
        // Keep index keys within SQL Server's 1700-byte limit. The full order projections remain on
        // the rows for exact keyset ordering; the incident hash is a compact deterministic tie-breaker.
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdOrderKey, x.IncidentIdHash });
        b.HasIndex(x => new { x.ScopeKeyHash, x.Status, x.CreatedAtUtcTicks, x.WorkflowExecutionIdOrderKey, x.IncidentIdHash });
    }
}

public sealed class RuntimeCheckpointCommitEntityConfiguration : IEntityTypeConfiguration<RuntimeCheckpointCommitEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeCheckpointCommitEntity> b)
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeOperationalStateEfModule.CheckpointCommitTableName);
        b.HasKey(x => x.Id); b.Property(x => x.Id).HasMaxLength(RuntimeOperationalStateEfModule.CompositeIdentityMaximumLength);
        b.Property(x => x.CommitId).HasMaxLength(RuntimeOperationalStateEfModule.CommitIdentityProjectionMaximumLength).IsRequired();
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(RuntimeCheckpointCommitEntity.CommitIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(RuntimeCheckpointCommitEntity.WorkflowExecutionId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(RuntimeCheckpointCommitEntity.WorkflowExecutionIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(RuntimeCheckpointCommitEntity.WorkflowExecutionIdOrderKey));
        b.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.PendingPostCommitWorkIdsJson).IsRequired();
        b.Property(x => x.ConsumedSchedulerWorkItemIdsJson).IsRequired();
        // A long commit id is too wide to index on every provider; its hash owns identity and the encoded
        // residual is compared ordinally after the hash lookup.
        b.HasIndex(x => new { x.ScopeKeyHash, x.CommitIdHash }).IsUnique();
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash });
    }
}

public sealed class DurableValueStateEntityConfiguration : IEntityTypeConfiguration<DurableValueStateEntity>
{
    public void Configure(EntityTypeBuilder<DurableValueStateEntity> b)
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeOperationalStateEfModule.DurableValueTableName);
        b.HasKey(x => x.Id); b.Property(x => x.Id).HasMaxLength(RuntimeOperationalStateEfModule.CompositeIdentityMaximumLength);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(DurableValueStateEntity.WorkflowExecutionId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(DurableValueStateEntity.WorkflowExecutionIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(DurableValueStateEntity.WorkflowExecutionIdOrderKey));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(DurableValueStateEntity.DurableValueId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(DurableValueStateEntity.DurableValueIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(DurableValueStateEntity.DurableValueIdOrderKey));
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.DurableValueIdHash }).IsUnique();
        b.HasIndex(x => new { x.ScopeKeyHash, x.DurableValueIdOrderKey });
    }
}

public sealed class SchedulerStateEntityConfiguration : IEntityTypeConfiguration<SchedulerStateEntity>
{
    public void Configure(EntityTypeBuilder<SchedulerStateEntity> b)
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeOperationalStateEfModule.SchedulerTableName);
        b.HasKey(x => x.Id); b.Property(x => x.Id).HasMaxLength(64);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(SchedulerStateEntity.WorkflowExecutionId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(SchedulerStateEntity.WorkflowExecutionIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(SchedulerStateEntity.WorkflowExecutionIdOrderKey));
        b.Property(x => x.Collection).HasMaxLength(128).IsRequired();
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.WorkflowExecutionId }).IsUnique();
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdOrderKey });
    }
}

public sealed class SchedulerWorkItemEntityConfiguration : IEntityTypeConfiguration<SchedulerWorkItemEntity>
{
    public void Configure(EntityTypeBuilder<SchedulerWorkItemEntity> b)
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeOperationalStateEfModule.SchedulerWorkTableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(64).IsRequired();
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(SchedulerWorkItemEntity.WorkflowExecutionId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(SchedulerWorkItemEntity.WorkflowExecutionIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(SchedulerWorkItemEntity.WorkflowExecutionIdOrderKey));
        // Work-item IDs are application identities and are deliberately not bounded. Their encoded value is not
        // indexed; WorkOrderKey and WorkItemIdHash provide the bounded query projections.
        b.Property(x => x.WorkItemId).IsRequired();
        b.Property(x => x.WorkItemIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.WorkOrderKey).HasMaxLength(RuntimeOperationalStateEfModule.SchedulerWorkOrderKeyMaximumLength).IsRequired();
        b.Property(x => x.ClaimOwnerId).IsRequired(false);
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.WorkItemIdHash }).IsUnique();
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.WorkOrderKey });
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdOrderKey, x.WorkflowExecutionIdHash });
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.VisibleAfterUtcTicks, x.WorkOrderKey });
    }
}
