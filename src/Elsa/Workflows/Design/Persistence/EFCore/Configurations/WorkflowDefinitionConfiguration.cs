using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Design.Persistence.EFCore.Configurations;

internal sealed class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(WorkflowDefinitionConstraints.MaximumIdLength);
        builder.Property(x => x.Name).HasMaxLength(WorkflowDefinitionConstraints.MaximumNameLength);

        // The Draft → Definition relationship is configured on the child side
        // (WorkflowDefinitionDraftConfiguration). The Definition no longer holds an inverse
        // DraftId pointer; the FK lives on each Draft per the 1-Definition-to-many-Drafts
        // cardinality.

        builder.HasIndex(x => x.Name).HasDatabaseName($"IX_{nameof(WorkflowDefinition)}_{nameof(WorkflowDefinition.Name)}");
        builder.HasIndex(x => x.DeletedAt).HasDatabaseName($"IX_{nameof(WorkflowDefinition)}_{nameof(WorkflowDefinition.DeletedAt)}");
    }
}
