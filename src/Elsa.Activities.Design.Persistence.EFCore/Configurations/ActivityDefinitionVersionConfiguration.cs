using Elsa.Activities.Design.Persistence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Activities.Design.Persistence.EFCore.Configurations
{
    public sealed class ActivityDefinitionVersionConfiguration : IEntityTypeConfiguration<ActivityDefinitionVersion>
    {
        public void Configure(EntityTypeBuilder<ActivityDefinitionVersion> builder)
        {
            builder.Ignore(x => x.Inputs);
            builder.Ignore(x => x.Outputs);
            builder.Ignore(x => x.Ports);

            builder.HasKey(x => x.Id);

            builder.Property(x => x.OutputsSource).HasMaxLength(-1);
            builder.Property(x => x.InputsSource).HasMaxLength(-1);
            builder.Property(x => x.PortsSource).HasMaxLength(-1);
            builder.Property(x => x.ImplementationDescriptorPayload).HasMaxLength(-1);

            builder
                .HasOne(x => x.Definition)
                .WithMany()
                .HasForeignKey(x => x.DefinitionId)
                .IsRequired();

            // Composite unique on (DefinitionId, Version). Serves "list versions for def"
            // via leftmost-prefix matching — no standalone DefinitionId index needed.
            builder
                .HasIndex(x => new { x.DefinitionId, x.Version })
                .HasDatabaseName($"UX_{nameof(ActivityDefinitionVersion)}_{nameof(ActivityDefinitionVersion.DefinitionId)}_{nameof(ActivityDefinitionVersion.Version)}")
                .IsUnique();
        }
    }
}
