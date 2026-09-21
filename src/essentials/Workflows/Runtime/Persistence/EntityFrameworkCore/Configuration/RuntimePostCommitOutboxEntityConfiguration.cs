using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

public sealed class RuntimePostCommitOutboxEntityConfiguration : IEntityTypeConfiguration<RuntimePostCommitOutboxEntity>
{
    public void Configure(EntityTypeBuilder<RuntimePostCommitOutboxEntity> b)
    {
        b.ToTable(RuntimePostCommitOutboxEfModule.TableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(RuntimePostCommitOutboxEfModule.PhysicalIdentityMaximumLength + 64).IsRequired();
        b.Property(x => x.ScopeKey).HasMaxLength(RuntimeOperationalStateEfModule.ScopeProjectionMaximumLength).IsRequired();
        b.Property(x => x.ScopeKeyHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.OutboxItemId).IsRequired();
        b.Property(x => x.OutboxItemIdHash).HasMaxLength(64).IsRequired();
        // The logical outbox ID has no enforced length bound: sorting its full ordinal projection cannot use a
        // narrow-provider composite index. Filter by the bounded candidate/time indexes and sort before Take.
        b.Property(x => x.OutboxItemIdOrderKey).IsRequired();
        b.Property(x => x.WorkflowExecutionId).IsRequired();
        b.Property(x => x.WorkflowExecutionIdHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.WorkflowExecutionIdOrderKey).HasMaxLength(RuntimePostCommitOutboxEfModule.PhysicalIdentityOrderKeyMaximumLength).IsRequired();
        b.Property(x => x.IntentKind).HasMaxLength(230).IsRequired();
        b.Property(x => x.IntentKindHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ContentJson).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.Property(x => x.Revision).IsConcurrencyToken().IsRequired();
        b.HasIndex(x => new { x.ScopeKeyHash, x.OutboxItemIdHash });
        b.HasIndex(x => new { x.ScopeKeyHash, x.DeliverableAtUtcTicks, x.RecordedAtUtcTicks });
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.DeliverableAtUtcTicks, x.RecordedAtUtcTicks });
        b.HasIndex(x => new { x.ScopeKeyHash, x.IntentKindHash, x.DeliverableAtUtcTicks, x.RecordedAtUtcTicks });
        b.HasIndex(x => new { x.ScopeKeyHash, x.ClaimableIsEligible, x.ClaimableAtUtcTicks, x.RecordedAtUtcTicks });
    }
}
