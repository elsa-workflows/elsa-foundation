using Elsa.Workflows.Design.Persistence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Design.Persistence.EFCore.Configurations
{
    internal sealed class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
    {
        public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
        {
            builder.HasKey(x => x.Id);
            builder
                .HasOne(def => def.Draft)
                .WithOne()
                .HasForeignKey<WorkflowDefinition>(def => def.DraftId);

            builder.HasIndex(x => x.Name).HasDatabaseName($"IX_{nameof(WorkflowDefinition)}_{nameof(WorkflowDefinition.Name)}");
            builder.HasIndex(x => x.TenantId).HasDatabaseName($"IX_{nameof(WorkflowDefinition)}_{nameof(WorkflowDefinition.TenantId)}");

            ConfigureMetaDataValueObject(builder);
        }

        private static void ConfigureMetaDataValueObject(EntityTypeBuilder<WorkflowDefinition> builder)
        {
            builder.OwnsOne(
                def => def.MetaData,
                metaData =>
                {
                    metaData.Property(m => m.ToolVersion);
                    metaData.Property(m => m.Materializer);
                    metaData.Property(m => m.MaterializerContext);
                    metaData.Property(m => m.IsSystem);
                }
            );
        }
    }
}
