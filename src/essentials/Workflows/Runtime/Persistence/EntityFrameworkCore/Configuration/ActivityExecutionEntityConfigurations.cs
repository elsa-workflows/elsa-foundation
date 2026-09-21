using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

public sealed class ActivityExecutionStateEntityConfiguration : IEntityTypeConfiguration<ActivityExecutionStateEntity>
{
    public void Configure(EntityTypeBuilder<ActivityExecutionStateEntity> builder)
    {
        builder.ToTable(RuntimeActivityExecutionEfModule.ActivityExecutionStateTableName);
        builder.HasKey(row => row.Id);
        ConfigureEnvelope(builder);
        builder.Property(row => row.WorkflowExecutionId).HasMaxLength(RuntimeActivityExecutionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.WorkflowExecutionIdHash).HasMaxLength(RuntimeActivityExecutionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.ActivityExecutionId).HasMaxLength(RuntimeActivityExecutionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.ActivityExecutionIdHash).HasMaxLength(RuntimeActivityExecutionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.ActivityExecutionIdOrderKey).HasMaxLength(RuntimeActivityExecutionEfModule.OrderKeyMaximumLength).IsRequired();
        builder.Property(row => row.ParentActivityExecutionId).HasMaxLength(RuntimeActivityExecutionEfModule.EncodedIdentityMaximumLength).IsRequired(false);
        builder.Property(row => row.ParentActivityExecutionIdHash).HasMaxLength(RuntimeActivityExecutionEfModule.HashMaximumLength).IsRequired(false);
        builder.Property(row => row.ExecutionScopeId).HasMaxLength(RuntimeActivityExecutionEfModule.EncodedIdentityMaximumLength).IsRequired(false);
        builder.Property(row => row.ExecutionScopeIdHash).HasMaxLength(RuntimeActivityExecutionEfModule.HashMaximumLength).IsRequired(false);
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.Property(row => row.ExecutionSequence).IsRequired();
        builder.Property(row => row.ScheduledAtUtcTicks).IsRequired();
        builder.Property(row => row.ScheduledAtOffsetMinutes).IsRequired();
        // Keep index keys below MySQL's 3072-byte utf8mb4 limit. The fixed-width
        // ordinal key remains the deterministic ORDER BY/keyset boundary; the
        // provider can use the compact scope/hash predicates before sorting it.
        builder.HasIndex(row => new { row.ScopeKeyHash, row.WorkflowExecutionIdHash });
        builder.HasIndex(row => new { row.ScopeKeyHash, row.WorkflowExecutionIdHash, row.ParentActivityExecutionIdHash });
    }

    internal static void ConfigureEnvelope<T>(EntityTypeBuilder<T> builder) where T : class
    {
        builder.Property("Id").HasMaxLength(RuntimeActivityExecutionEfModule.HashMaximumLength).IsRequired();
        builder.Property("ScopeKey").IsRequired();
        builder.Property("ScopeKeyHash").HasMaxLength(RuntimeActivityExecutionEfModule.HashMaximumLength).IsRequired();
        builder.Property("ContentJson").IsRequired();
        builder.Property("SchemaVersion").HasMaxLength(32).IsRequired();
        builder.Property("Revision").IsRequired().IsConcurrencyToken();
    }
}

public sealed class ActivityExecutionInspectionEntityConfiguration : IEntityTypeConfiguration<ActivityExecutionInspectionEntity>
{
    public void Configure(EntityTypeBuilder<ActivityExecutionInspectionEntity> builder)
    {
        builder.ToTable(RuntimeActivityExecutionEfModule.ActivityExecutionInspectionTableName);
        builder.HasKey(row => row.Id);
        ActivityExecutionStateEntityConfiguration.ConfigureEnvelope(builder);
        builder.Property(row => row.WorkflowExecutionId).HasMaxLength(RuntimeActivityExecutionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.WorkflowExecutionIdHash).HasMaxLength(RuntimeActivityExecutionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.ActivityExecutionId).HasMaxLength(RuntimeActivityExecutionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.ActivityExecutionIdHash).HasMaxLength(RuntimeActivityExecutionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.ActivityExecutionIdOrderKey).HasMaxLength(RuntimeActivityExecutionEfModule.OrderKeyMaximumLength).IsRequired();
        builder.Property(row => row.ExecutionScopeId).HasMaxLength(RuntimeActivityExecutionEfModule.EncodedIdentityMaximumLength).IsRequired(false);
        builder.Property(row => row.ExecutionScopeIdHash).HasMaxLength(RuntimeActivityExecutionEfModule.HashMaximumLength).IsRequired(false);
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.Property(row => row.SummaryExecutionSequence).IsRequired();
        builder.Property(row => row.SummaryScheduledAtUtcTicks).IsRequired();
        builder.Property(row => row.SummaryScheduledAtOffsetMinutes).IsRequired();
        builder.HasIndex(row => new { row.ScopeKeyHash, row.WorkflowExecutionIdHash, row.SummaryExecutionSequence, row.SummaryScheduledAtUtcTicks });
    }
}

public sealed class ActivityExecutionHierarchyEntityConfiguration : IEntityTypeConfiguration<ActivityExecutionHierarchyEntity>
{
    public void Configure(EntityTypeBuilder<ActivityExecutionHierarchyEntity> builder)
    {
        builder.ToTable(RuntimeActivityExecutionEfModule.ActivityExecutionHierarchyTableName);
        builder.HasKey(row => row.Id);
        ActivityExecutionStateEntityConfiguration.ConfigureEnvelope(builder);
        builder.Property(row => row.WorkflowExecutionId).HasMaxLength(RuntimeActivityExecutionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.WorkflowExecutionIdHash).HasMaxLength(RuntimeActivityExecutionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.ActivityExecutionId).HasMaxLength(RuntimeActivityExecutionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.ActivityExecutionIdHash).HasMaxLength(RuntimeActivityExecutionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.ActivityExecutionIdOrderKey).HasMaxLength(RuntimeActivityExecutionEfModule.OrderKeyMaximumLength).IsRequired();
        builder.Property(row => row.ExecutionScopeId).HasMaxLength(RuntimeActivityExecutionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.ExecutionScopeIdHash).HasMaxLength(RuntimeActivityExecutionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.ParentActivityExecutionId).HasMaxLength(RuntimeActivityExecutionEfModule.EncodedIdentityMaximumLength).IsRequired(false);
        builder.Property(row => row.ParentActivityExecutionIdHash).HasMaxLength(RuntimeActivityExecutionEfModule.HashMaximumLength).IsRequired(false);
        builder.Property(row => row.IsScopeRoot).IsRequired();
        builder.Property(row => row.ExecutionSequence).IsRequired();
        builder.HasIndex(row => new { row.ScopeKeyHash, row.WorkflowExecutionIdHash, row.ExecutionScopeIdHash, row.IsScopeRoot, row.ExecutionSequence });
        builder.HasIndex(row => new { row.ScopeKeyHash, row.WorkflowExecutionIdHash, row.ExecutionSequence });
    }
}
