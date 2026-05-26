using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Persistence.EFCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Activities.Design.Persistence.EFCore.Configurations
{
    internal sealed class ActivityDefinitionVersionConfiguration : IEntityTypeConfiguration<ActivityDefinitionVersion>
    {
        public void Configure(EntityTypeBuilder<ActivityDefinitionVersion> builder)
        {
            builder.Ignore(x => x.Inputs);
            builder.Ignore(x => x.Outputs);
            builder.Ignore(x => x.Ports);

            builder.HasKey(x => x.Id);

            builder.ConfigureTypeInformation(x => x.TypeInfo);

            builder
                .Property(x => x.OutputsSource)
                .HasMaxLength(-1);
            builder
                .Property(x => x.InputsSource)
                .HasMaxLength(-1);
            builder
                .Property(x => x.PortsSource)
                .HasMaxLength(-1);

            builder
                .HasOne(x => x.Definition)
                .WithMany()
                .HasForeignKey(x => x.DefinitionId)
                .IsRequired();

            // Composite unique index on (DefinitionId, Version): prevents two rows with
            // the same definition + version number, and serves "list versions for definition"
            // queries via leftmost-prefix matching, so a standalone DefinitionId index is
            // redundant. No queries filter on Version alone, so no standalone Version index.
            builder
                .HasIndex(x => new { x.DefinitionId, x.Version })
                .HasDatabaseName($"UX_{nameof(ActivityDefinitionVersion)}_{nameof(ActivityDefinitionVersion.DefinitionId)}_{nameof(ActivityDefinitionVersion.Version)}")
                .IsUnique();

            builder.HasIndex(x => x.TenantId).HasDatabaseName($"IX_{nameof(ActivityDefinitionVersion)}_{nameof(ActivityDefinitionVersion.TenantId)}");
        }
    }
}
