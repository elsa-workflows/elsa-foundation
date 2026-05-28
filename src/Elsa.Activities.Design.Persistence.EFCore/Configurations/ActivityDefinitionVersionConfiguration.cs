using Elsa.Activities.Design.Persistence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Activities.Design.Persistence.EFCore.Configurations
{
    internal sealed class ActivityDefinitionVersionConfiguration : IEntityTypeConfiguration<ActivityDefinitionVersion>
    {
        /// <summary>
        /// Persisted name of the implementation-descriptor JSON shadow column. Distinct from
        /// the <c>[NotMapped]</c> <see cref="ActivityDefinitionVersion.ImplementationDescriptor"/>
        /// CLR property — EF Core does NOT treat <c>[NotMapped]</c> as invisible when resolving
        /// shadow-property names, so the two cannot share a name (plan §T063 risk note,
        /// observed empirically). The column suffix <c>Payload</c> marks it as the serialised
        /// form of the descriptor.
        /// </summary>
        internal const string DescriptorShadowName = "ImplementationDescriptorPayload";

        public void Configure(EntityTypeBuilder<ActivityDefinitionVersion> builder)
        {
            builder.Ignore(x => x.Inputs);
            builder.Ignore(x => x.Outputs);
            builder.Ignore(x => x.Ports);

            builder.HasKey(x => x.Id);

            builder.Property(x => x.OutputsSource).HasMaxLength(-1);
            builder.Property(x => x.InputsSource).HasMaxLength(-1);
            builder.Property(x => x.PortsSource).HasMaxLength(-1);

            // Shadow column holding the serialized polymorphic descriptor. Marked
            // PropertySaveBehavior.Throw so EF blocks updates post-insert — the descriptor
            // is part of the version's immutable identity.
            var descriptor = builder.Property<string?>(DescriptorShadowName)
                .HasMaxLength(-1);
            descriptor.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

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
