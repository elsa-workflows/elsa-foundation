using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

public sealed class BackgroundActivityWorkEntityConfiguration : IEntityTypeConfiguration<BackgroundActivityWorkEntity>
{
    public void Configure(EntityTypeBuilder<BackgroundActivityWorkEntity> b)
    {
        b.ToTable(RuntimeBackgroundActivityWorkEfModule.TableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(64).IsRequired();
        b.Property(x => x.ScopeKey).HasMaxLength(RuntimeOperationalStateEfModule.ScopeProjectionMaximumLength).IsRequired();
        b.Property(x => x.ScopeKeyHash).HasMaxLength(64).IsRequired();
        ConfigureIdentity(b, x => x.WorkflowExecutionId, x => x.WorkflowExecutionIdHash);
        ConfigureIdentity(b, x => x.ActivityExecutionId, x => x.ActivityExecutionIdHash);
        ConfigureIdentity(b, x => x.WorkId, x => x.WorkIdHash);
        b.Property(x => x.ContentJson).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.Property(x => x.Revision).IsConcurrencyToken().IsRequired();
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.WorkIdHash }).IsUnique();
        b.HasIndex(x => new { x.ScopeKeyHash, x.ClaimableIsEligible, x.ClaimableAtUtcTicks, x.RecordedAtUtcTicks });
        b.HasIndex(x => new { x.ScopeKeyHash, x.WorkflowExecutionIdHash, x.Status, x.UpdatedAtUtcTicks });
        b.HasIndex(x => new { x.ScopeKeyHash, x.Status, x.UpdatedAtUtcTicks });
    }

    private static void ConfigureIdentity(
        EntityTypeBuilder<BackgroundActivityWorkEntity> b,
        System.Linq.Expressions.Expression<Func<BackgroundActivityWorkEntity, string>> value,
        System.Linq.Expressions.Expression<Func<BackgroundActivityWorkEntity, string>> hash)
    {
        b.Property(value).HasMaxLength(RuntimeOperationalStateEfModule.IdentityMaximumLength).IsRequired();
        b.Property(hash).HasMaxLength(64).IsRequired();
    }
}
