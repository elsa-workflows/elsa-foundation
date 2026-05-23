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

            builder.HasIndex(x => x.DefinitionId).HasDatabaseName($"IX_{nameof(ActivityDefinitionVersion)}_{nameof(ActivityDefinitionVersion.DefinitionId)}");
            builder.HasIndex(x => x.Version).HasDatabaseName($"IX_{nameof(ActivityDefinitionVersion)}_{nameof(ActivityDefinitionVersion.Version)}");
            builder.HasIndex(x => x.TenantId).HasDatabaseName($"IX_{nameof(ActivityDefinitionVersion)}_{nameof(ActivityDefinitionVersion.TenantId)}");
        }
    }
}
