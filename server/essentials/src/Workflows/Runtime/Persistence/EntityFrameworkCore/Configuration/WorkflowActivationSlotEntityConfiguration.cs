using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

public sealed class WorkflowActivationSlotEntityConfiguration : IEntityTypeConfiguration<WorkflowActivationSlotEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowActivationSlotEntity> b)
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeActivationSlotEfModule.TableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(RuntimeOperationalStateEfModule.CompositeIdentityMaximumLength).IsRequired();
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowActivationSlotEntity.SlotId));
        b.Property(x => x.SlotId).HasMaxLength(RuntimeActivationSlotEfModule.SlotIdProjectionMaximumLength).IsRequired();
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(WorkflowActivationSlotEntity.SlotIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(WorkflowActivationSlotEntity.SlotIdOrderKey));
        b.Property(x => x.SlotIdOrderKey).HasMaxLength(RuntimeActivationSlotEfModule.SlotIdOrderKeyMaximumLength).IsRequired();
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowActivationSlotEntity.WorkflowDefinitionId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(WorkflowActivationSlotEntity.WorkflowDefinitionIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(WorkflowActivationSlotEntity.WorkflowDefinitionIdOrderKey));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowActivationSlotEntity.SlotName));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(WorkflowActivationSlotEntity.SlotNameHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(WorkflowActivationSlotEntity.SlotNameOrderKey));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowActivationSlotEntity.ActiveActivationId), nullable: true);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(b, nameof(WorkflowActivationSlotEntity.ActiveActivationIdHash), nullable: true);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(b, nameof(WorkflowActivationSlotEntity.ActiveActivationIdOrderKey), nullable: true);
        b.Property(x => x.ActiveActivationUniquenessKey).HasMaxLength(64).IsRequired();
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowActivationSlotEntity.SourceKind), nullable: true);
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(b, nameof(WorkflowActivationSlotEntity.SourceId), nullable: true);
        // Keep the uniqueness index hash-backed: the full encoded identity exceeds SQL Server and MySQL
        // nonclustered-key budgets at the legal composite slot-id boundary. Read paths recheck the exact
        // encoded identity and authoritative slot-id hash, so the hash pair is only the bounded admission key.
        b.HasIndex(x => new { x.ScopeKeyHash, x.SlotIdHash }).IsUnique();
        // The full ordinal projections are intentionally wider than SQL Server's nonclustered-key
        // budget when combined. Keep the lookup index hash-backed; the provider may sort the bounded page.
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowDefinitionIdHash, x.SlotNameHash, x.SlotIdHash });
        // SQL Server treats NULL as a value in unique indexes, so nullable active-id columns would
        // incorrectly allow only one inactive slot per scope. The key is always populated: active rows
        // hash an activation identity and inactive rows hash their own slot identity.
        b.HasIndex(x => new { x.ScopeKeyHash, x.ActiveActivationUniquenessKey }).IsUnique();
    }
}
