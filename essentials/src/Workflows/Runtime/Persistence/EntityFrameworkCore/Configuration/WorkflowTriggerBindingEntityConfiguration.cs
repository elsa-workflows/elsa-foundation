using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

public sealed class WorkflowTriggerBindingEntityConfiguration : IEntityTypeConfiguration<WorkflowTriggerBindingEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowTriggerBindingEntity> b)
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeTriggerBindingEfModule.TableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(RuntimeOperationalStateEfModule.CompositeIdentityMaximumLength).IsRequired();
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowTriggerBindingEntity.TriggerBindingId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(WorkflowTriggerBindingEntity.TriggerBindingIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(WorkflowTriggerBindingEntity.TriggerBindingIdOrderKey));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowTriggerBindingEntity.ArtifactId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(WorkflowTriggerBindingEntity.ArtifactIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(WorkflowTriggerBindingEntity.ArtifactIdOrderKey));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowTriggerBindingEntity.DefinitionId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowTriggerBindingEntity.ArtifactVersion));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowTriggerBindingEntity.ArtifactHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowTriggerBindingEntity.ExecutableNodeId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowTriggerBindingEntity.StimulusType));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowTriggerBindingEntity.StimulusHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowTriggerBindingEntity.StimulusLookupKey));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowTriggerBindingEntity.StimulusTypeLookupKey));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowTriggerBindingEntity.CorrelationScope), nullable: true);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowTriggerBindingEntity.ActivationId), nullable: true);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(WorkflowTriggerBindingEntity.ActivationIdHash), nullable: true);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(WorkflowTriggerBindingEntity.ActivationIdOrderKey), nullable: true);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowTriggerBindingEntity.SlotId), nullable: true);
        b.Property(x => x.StimulusType).HasMaxLength(RuntimeTriggerBindingEfModule.StimulusTypeProjectionMaximumLength);
        b.Property(x => x.Cardinality).IsRequired();
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.CreatedAtUtcTicks).IsRequired();
        b.Property(x => x.CreatedAtOffsetMinutes).IsRequired();
        b.HasIndex(x => new { x.ScopeKeyHash, x.TriggerBindingIdHash, x.TriggerBindingId }).IsUnique();
        // Keep provider index keys below SQL Server/MySQL limits. Queries still use the full
        // fixed-width ordinal projections for deterministic ordering; hashes are compact tie-breakers.
        b.HasIndex(x => new { x.ScopeKeyHash, x.ArtifactIdHash, x.TriggerBindingIdHash });
        b.HasIndex(x => new { x.ScopeKeyHash, x.ActivationIdHash, x.TriggerBindingIdHash });
        b.HasIndex(x => new { x.ScopeKeyHash, x.StimulusLookupKey, x.IsActive, x.TriggerBindingIdHash });
        b.HasIndex(x => new { x.ScopeKeyHash, x.StimulusTypeLookupKey, x.IsActive, x.TriggerBindingIdHash });
    }
}

public sealed class WorkflowTriggerBindingProjectionStateEntityConfiguration : IEntityTypeConfiguration<WorkflowTriggerBindingProjectionStateEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowTriggerBindingProjectionStateEntity> b)
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeTriggerBindingEfModule.ProjectionStateTableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(RuntimeOperationalStateEfModule.CompositeIdentityMaximumLength).IsRequired();
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowTriggerBindingProjectionStateEntity.ActivationId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(WorkflowTriggerBindingProjectionStateEntity.ActivationIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(WorkflowTriggerBindingProjectionStateEntity.ActivationIdOrderKey));
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.BindingCount).IsRequired();
        b.Property(x => x.ProjectionFingerprint).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.ScopeKeyHash, x.ActivationIdHash, x.ActivationId }).IsUnique();
    }
}
