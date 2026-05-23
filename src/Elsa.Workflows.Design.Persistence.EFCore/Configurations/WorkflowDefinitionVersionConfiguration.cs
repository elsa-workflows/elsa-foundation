using Elsa.Persistence.EFCore;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Design.Persistence.EFCore.Configurations
{
    internal sealed class WorkflowDefinitionVersionConfiguration : IEntityTypeConfiguration<WorkflowDefinitionVersion>
    {
        public void Configure(EntityTypeBuilder<WorkflowDefinitionVersion> builder)
        {
            builder.Ignore(x => x.State);

            builder
                .HasKey(x => x.Id);

            builder
                .HasOne(x => x.Definition)
                .WithMany()
                .HasForeignKey(x => x.DefinitionId)
                .IsRequired();

            builder
                .Property(x => x.StateSource)
                .HasMaxLength(-1);

            builder.HasIndex(x => x.DefinitionId).HasDatabaseName($"IX_{nameof(WorkflowDefinitionVersion)}_{nameof(WorkflowDefinitionVersion.DefinitionId)}");
            builder.HasIndex(x => x.Version).HasDatabaseName($"IX_{nameof(WorkflowDefinitionVersion)}_{nameof(WorkflowDefinitionVersion.Version)}");
            builder.HasIndex(x => x.TenantId).HasDatabaseName($"IX_{nameof(WorkflowDefinitionVersion)}_{nameof(WorkflowDefinitionVersion.TenantId)}");
        }
    }
}
