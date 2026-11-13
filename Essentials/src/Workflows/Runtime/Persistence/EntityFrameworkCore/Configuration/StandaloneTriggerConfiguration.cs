using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

internal sealed class StandaloneTriggerConfiguration : IEntityTypeConfiguration<StandaloneTriggerEntity>
{
    public void Configure(EntityTypeBuilder<StandaloneTriggerEntity> b)
    {
        b.ToTable("elsa_runtime_standalone_triggers");
        b.HasKey(row => row.Id);
        b.Property(row => row.Id).HasMaxLength(64);
        b.Property(row => row.ScopeKey).IsRequired();
        b.Property(row => row.ScopeHash).HasMaxLength(64);
        b.Property(row => row.TenantKey).IsRequired();
        b.Property(row => row.TenantHash).HasMaxLength(64);
        b.Property(row => row.TriggerKey).IsRequired();
        b.Property(row => row.TriggerOrderKey).HasMaxLength(RuntimeOperationalStateEfModule.OrderKeyMaximumLength);
        b.Property(row => row.TypeKey).IsRequired();
        b.Property(row => row.SearchName).HasMaxLength(256);
        b.Property(row => row.ContentJson).IsRequired();
        b.Property(row => row.SchemaVersion).HasMaxLength(32);
        b.Property(row => row.Revision).IsConcurrencyToken();
        b.HasIndex(row => new { row.ScopeHash, row.TenantHash, row.DesiredState, row.TriggerOrderKey });
    }
}

internal sealed class TriggerSubscriptionConfiguration : IEntityTypeConfiguration<TriggerSubscriptionEntity>
{
    public void Configure(EntityTypeBuilder<TriggerSubscriptionEntity> b)
    {
        b.ToTable("elsa_runtime_trigger_subscriptions");
        b.HasKey(row => row.Id);
        b.Property(row => row.Id).HasMaxLength(64);
        b.Property(row => row.ScopeKey).IsRequired();
        b.Property(row => row.ScopeHash).HasMaxLength(64);
        b.Property(row => row.TenantKey).IsRequired();
        b.Property(row => row.TenantHash).HasMaxLength(64);
        b.Property(row => row.TriggerRowId).HasMaxLength(64);
        b.Property(row => row.SubscriptionKey).IsRequired();
        b.Property(row => row.SubscriptionOrderKey).HasMaxLength(RuntimeOperationalStateEfModule.OrderKeyMaximumLength);
        b.Property(row => row.DefinitionHash).HasMaxLength(64);
        b.Property(row => row.DefinitionKey).IsRequired();
        b.Property(row => row.SlotKey).IsRequired();
        b.Property(row => row.ContentJson).IsRequired();
        b.Property(row => row.SchemaVersion).HasMaxLength(32);
        b.Property(row => row.Revision).IsConcurrencyToken();
        b.HasIndex(row => new { row.ScopeHash, row.TenantHash, row.DefinitionHash, row.IsDeleted, row.SubscriptionOrderKey });
        b.HasIndex(row => new { row.TriggerRowId, row.IsDeleted });
    }
}

internal sealed class TriggerMutationConfiguration : IEntityTypeConfiguration<TriggerMutationEntity>
{
    public void Configure(EntityTypeBuilder<TriggerMutationEntity> b)
    {
        b.ToTable("elsa_runtime_trigger_mutations");
        b.HasKey(row => row.Id);
        b.Property(row => row.Id).HasMaxLength(64);
        b.Property(row => row.ScopeKey).IsRequired();
        b.Property(row => row.TenantKey).IsRequired();
        b.Property(row => row.ActorKey).IsRequired();
        b.Property(row => row.RequestKey).IsRequired();
        b.Property(row => row.TriggerRowId).HasMaxLength(64);
        b.Property(row => row.ContentJson).IsRequired();
        b.Property(row => row.SchemaVersion).HasMaxLength(32);
        b.HasIndex(row => row.TriggerRowId);
    }
}
