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
            .HasIndex(x => x.UniqueName)
            .HasDatabaseName($"IX_{nameof(ActivityDefinition)}_{nameof(ActivityDefinition.UniqueName)}")
            .IsUnique();

        builder.HasIndex(x => x.TenantId).HasDatabaseName($"IX_{nameof(ActivityDefinition)}_{nameof(ActivityDefinition.TenantId)}");
        builder.HasIndex(x => x.IsBrowsable).HasDatabaseName($"IX_{nameof(ActivityDefinition)}_{nameof(ActivityDefinition.IsBrowsable)}");
        builder.HasIndex(x => x.Category).HasDatabaseName($"IX_{nameof(ActivityDefinition)}_{nameof(ActivityDefinition.Category)}");
    }
}
