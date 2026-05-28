using Elsa.Activities.Design.Persistence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Activities.Design.Persistence.EFCore.Configurations;

public sealed class ActivityDefinitionConfiguration : IEntityTypeConfiguration<ActivityDefinition>
{
    public void Configure(EntityTypeBuilder<ActivityDefinition> builder)
    {
        builder
            .HasKey(x => x.Id);

        builder
            .HasIndex(x => new { x.SourceKind, x.SourceId, x.ActivityTypeKey })
            .HasDatabaseName($"UX_{nameof(ActivityDefinition)}_{nameof(ActivityDefinition.SourceKind)}_{nameof(ActivityDefinition.SourceId)}_{nameof(ActivityDefinition.ActivityTypeKey)}")
            .IsUnique();

        builder
            .HasIndex(x => new { x.SourceKind, x.SourceId })
            .HasDatabaseName($"IX_{nameof(ActivityDefinition)}_{nameof(ActivityDefinition.SourceKind)}_{nameof(ActivityDefinition.SourceId)}");

        builder.HasIndex(x => x.Category)
            .HasDatabaseName($"IX_{nameof(ActivityDefinition)}_{nameof(ActivityDefinition.Category)}");
    }
}
