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
        b.Property(property).HasMaxLength(RuntimeOperationalStateEfModule.IdentityMaximumLength);
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
        b.HasIndex(x => new
        {
            x.ScopeKeyHash,
            x.WorkflowExecutionIdHash,
            x.WorkflowExecutionId,
            x.DurableValueIdHash,
            x.DurableValueId
        }).IsUnique();
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
