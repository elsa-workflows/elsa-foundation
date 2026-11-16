using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

public sealed class DurableTimerEntityConfiguration : IEntityTypeConfiguration<DurableTimerEntity>
{
    public void Configure(EntityTypeBuilder<DurableTimerEntity> b)
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeOperationalStateEfModule.DurableTimerTableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(64).IsRequired();
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(DurableTimerEntity.WorkflowExecutionId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(DurableTimerEntity.WorkflowExecutionIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(DurableTimerEntity.WorkflowExecutionIdOrderKey));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(DurableTimerEntity.TimerId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(DurableTimerEntity.TimerIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(DurableTimerEntity.TimerIdOrderKey));
        b.Property(x => x.StimulusType).HasMaxLength(RuntimeOperationalStateEfModule.DurableTimerStimulusTypeProjectionMaximumLength).IsRequired();
        b.Property(x => x.StimulusHash).HasMaxLength(RuntimeOperationalStateEfModule.DurableTimerStimulusHashProjectionMaximumLength).IsRequired();
        b.Property(x => x.ClaimOrderKey).HasMaxLength(RuntimeOperationalStateEfModule.DurableTimerClaimOrderKeyMaximumLength).IsRequired();
        b.Property(x => x.ClaimOwnerId).HasMaxLength(RuntimeOperationalStateEfModule.IdentityProjectionMaximumLength).IsRequired(false);
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.TimerIdHash }).IsUnique();
        b.HasIndex(x => new { x.ScopeKeyHash, x.DueTimeUtcTicks, x.TimerIdOrderKey });
        b.HasIndex(x => new { x.ScopeKeyHash, x.ClaimOrderKey });
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.TimerIdOrderKey });
    }
}
