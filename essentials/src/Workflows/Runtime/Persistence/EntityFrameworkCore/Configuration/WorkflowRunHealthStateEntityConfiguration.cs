using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

/// <summary>Provider-neutral relational model for the R19 workflow run-health checkpoint projection.</summary>
public sealed class WorkflowRunHealthStateEntityConfiguration : IEntityTypeConfiguration<WorkflowRunHealthStateEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowRunHealthStateEntity> builder)
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(
            builder,
            RuntimeOperationalStateEfModule.WorkflowRunHealthTableName);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasMaxLength(RuntimeOperationalStateEfModule.CompositeIdentityMaximumLength).IsRequired();
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(builder, nameof(WorkflowRunHealthStateEntity.WorkflowExecutionId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(builder, nameof(WorkflowRunHealthStateEntity.WorkflowExecutionIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(builder, nameof(WorkflowRunHealthStateEntity.WorkflowExecutionIdOrderKey));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureIdentity(builder, nameof(WorkflowRunHealthStateEntity.DefinitionId));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureHash(builder, nameof(WorkflowRunHealthStateEntity.DefinitionIdHash));
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureOrder(builder, nameof(WorkflowRunHealthStateEntity.DefinitionIdOrderKey));
        builder.Property(row => row.RunKind).IsRequired();
        builder.Property(row => row.StartedAtUtcTicks).IsRequired(false);
        builder.Property(row => row.StartedAtOffsetMinutes).IsRequired(false);
        builder.Property(row => row.Status).IsRequired();
        builder.Property(row => row.IncidentCount).IsRequired();
        builder.Property(row => row.IncidentBearingCount).IsRequired();
        builder.HasIndex(row => new { row.ScopeKeyHash, row.WorkflowExecutionIdHash, row.WorkflowExecutionId }).IsUnique();
        builder.HasIndex(row => new { row.ScopeKeyHash, row.StartedAtUtcTicks, row.WorkflowExecutionIdOrderKey });
        builder.HasIndex(row => new { row.ScopeKeyHash, row.Status, row.StartedAtUtcTicks, row.WorkflowExecutionIdOrderKey });
        builder.HasIndex(row => new { row.ScopeKeyHash, row.DefinitionIdHash, row.StartedAtUtcTicks, row.WorkflowExecutionIdOrderKey });
        builder.HasIndex(row => new { row.ScopeKeyHash, row.RunKind, row.Status, row.DefinitionIdHash, row.StartedAtUtcTicks, row.WorkflowExecutionIdOrderKey });
    }
}
