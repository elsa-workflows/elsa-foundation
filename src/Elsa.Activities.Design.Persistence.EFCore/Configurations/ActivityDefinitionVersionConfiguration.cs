using Elsa.Activities.Design.Persistence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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

            builder.Property(x => x.Version).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            builder.Property(x => x.SemVerSortKey).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            builder.Property(x => x.DefinitionId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            builder.Property(x => x.DescriptorType).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            builder.Property(x => x.DescriptorPayloadSource).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            builder.Property(x => x.InputsSource).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            builder.Property(x => x.OutputsSource).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            builder.Property(x => x.PortsSource).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            builder.Property(x => x.ExecutionType).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            builder.Property(x => x.SourceKind).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            builder.Property(x => x.SourceId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            builder.Property(x => x.ReconciledAt).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            builder.Property(x => x.ReconciledBy).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            builder.Property(x => x.ReconcilliationHash).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

            builder.Property(x => x.OutputsSource).HasMaxLength(-1);
            builder.Property(x => x.InputsSource).HasMaxLength(-1);
            builder.Property(x => x.PortsSource).HasMaxLength(-1);
            builder.Property(x => x.DescriptorPayloadSource).HasMaxLength(-1);

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
