using Elsa.Activities.Design.Persistence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Activities.Design.Persistence.EFCore.Configurations;

public sealed class ActivityDefinitionReconciliationStateConfiguration
    : IEntityTypeConfiguration<ActivityDefinitionReconciliationState>
{
    public void Configure(EntityTypeBuilder<ActivityDefinitionReconciliationState> builder)
    {
        builder.HasKey(x => x.Id);

        // 1:0..1 with ActivityDefinition — enforced by uniqueness on the FK column.
        builder
            .HasIndex(x => x.ActivityDefinitionId)
            .HasDatabaseName($"UX_{nameof(ActivityDefinitionReconciliationState)}_{nameof(ActivityDefinitionReconciliationState.ActivityDefinitionId)}")
            .IsUnique();

        builder
            .HasIndex(x => x.IsStale)
            .HasDatabaseName($"IX_{nameof(ActivityDefinitionReconciliationState)}_{nameof(ActivityDefinitionReconciliationState.IsStale)}");
    }
}
