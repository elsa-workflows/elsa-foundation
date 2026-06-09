using Elsa.Workflows.Design.Persistence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Design.Persistence.EFCore.Configurations
{
    internal sealed class WorkflowDefinitionDraftConfiguration : IEntityTypeConfiguration<WorkflowDefinitionDraft>
    {
        public void Configure(EntityTypeBuilder<WorkflowDefinitionDraft> builder)
        {
            builder.Ignore(x => x.State);

            builder.HasKey(x => x.Id);

            builder
                .Property(x => x.StateSource)
                .HasMaxLength(-1);

            // 1 Definition : many Drafts. Cascade-deletes Drafts when the Definition is removed;
            // the Draft's own Layout + Validation siblings cascade in turn (R5).
            builder
                .HasOne(x => x.WorkflowDefinition)
                .WithMany()
                .HasForeignKey(x => x.WorkflowDefinitionId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            // Optional, write-once FK to the source WorkflowDefinitionVersion a clone descended
            // from. No navigation property (a Draft never traverses into the version) and no
            // referential action wired through EF — deleting a version must not cascade into or
            // restrict its descendant Drafts, so the column is a plain nullable string carrying
            // the provenance id only.
            builder
                .Property(x => x.SourceVersionId)
                .IsRequired(false)
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }
    }
}
