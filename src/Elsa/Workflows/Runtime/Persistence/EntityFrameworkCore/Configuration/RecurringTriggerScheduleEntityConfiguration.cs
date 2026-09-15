using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

public sealed class RecurringTriggerScheduleEntityConfiguration : IEntityTypeConfiguration<RecurringTriggerScheduleEntity>
{
    public void Configure(EntityTypeBuilder<RecurringTriggerScheduleEntity> b)
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeOperationalStateEfModule.RecurringScheduleTableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(RuntimeOperationalStateEfModule.CompositeIdentityMaximumLength).IsRequired();
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(RecurringTriggerScheduleEntity.ScheduleId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(RecurringTriggerScheduleEntity.ScheduleIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(RecurringTriggerScheduleEntity.ScheduleIdOrderKey));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(RecurringTriggerScheduleEntity.ArtifactId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(RecurringTriggerScheduleEntity.ArtifactIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(RecurringTriggerScheduleEntity.ArtifactIdOrderKey));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(RecurringTriggerScheduleEntity.ActivationId), nullable: true);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(RecurringTriggerScheduleEntity.ActivationIdHash), nullable: true);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(RecurringTriggerScheduleEntity.ActivationIdOrderKey), nullable: true);
        b.Property(x => x.ExecutableNodeId).IsRequired();
        b.Property(x => x.StimulusType).IsRequired();
        b.Property(x => x.StimulusHash).IsRequired();
        b.Property(x => x.Expression).IsRequired();
        b.Property(x => x.SlotId).IsRequired(false);
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.NextOccurrenceUtcTicks).IsRequired();
        b.Property(x => x.NextOccurrenceOffsetMinutes).IsRequired();
        b.Property(x => x.CreatedAtUtcTicks).IsRequired();
        b.Property(x => x.CreatedAtOffsetMinutes).IsRequired();
        b.HasIndex(x => new { x.ScopeKeyHash, x.ScheduleIdHash, x.ScheduleId }).IsUnique();
        b.HasIndex(x => new { x.ScopeKeyHash, x.IsActive, x.NextOccurrenceUtcTicks, x.ScheduleIdHash });
        b.HasIndex(x => new { x.ScopeKeyHash, x.ActivationIdHash, x.ScheduleIdHash });
        b.HasIndex(x => new { x.ScopeKeyHash, x.ArtifactIdHash, x.ScheduleIdHash });
    }
}

public sealed class RecurringTriggerScheduleProjectionStateEntityConfiguration : IEntityTypeConfiguration<RecurringTriggerScheduleProjectionStateEntity>
{
    public void Configure(EntityTypeBuilder<RecurringTriggerScheduleProjectionStateEntity> b)
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeOperationalStateEfModule.RecurringScheduleProjectionStateTableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(RuntimeOperationalStateEfModule.CompositeIdentityMaximumLength).IsRequired();
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(RecurringTriggerScheduleProjectionStateEntity.ActivationId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(RecurringTriggerScheduleProjectionStateEntity.ActivationIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(RecurringTriggerScheduleProjectionStateEntity.ActivationIdOrderKey));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(RecurringTriggerScheduleProjectionStateEntity.ArtifactId), nullable: true);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(RecurringTriggerScheduleProjectionStateEntity.ArtifactIdHash), nullable: true);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(RecurringTriggerScheduleProjectionStateEntity.ArtifactIdOrderKey), nullable: true);
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.ScheduleCount).IsRequired();
        b.Property(x => x.ProjectionFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.ScheduleIdsJson).IsRequired();
        b.Property(x => x.ScheduleFingerprintsJson).IsRequired();
        b.HasIndex(x => new { x.ScopeKeyHash, x.ActivationIdHash, x.ActivationId }).IsUnique();
        b.HasIndex(x => new { x.ScopeKeyHash, x.ArtifactIdHash, x.ActivationIdHash });
    }
}
