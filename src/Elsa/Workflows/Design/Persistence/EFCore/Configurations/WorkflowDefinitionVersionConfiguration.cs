using Elsa.Workflows.Design.Persistence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Design.Persistence.EFCore.Configurations;

internal sealed class WorkflowDefinitionVersionConfiguration : IEntityTypeConfiguration<WorkflowDefinitionVersion>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinitionVersion> builder)
    {
        builder.Ignore(x => x.State);

        builder
            .HasKey(x => x.Id);

        builder.Property(x => x.Version).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(x => x.SemVerSortKey).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(x => x.DefinitionId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(x => x.SourceDraftId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(x => x.StateSource).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(x => x.SourceCreatedAt).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        builder
            .HasOne(x => x.Definition)
            .WithMany()
            .HasForeignKey(x => x.DefinitionId)
            .IsRequired();

        builder
            .Property(x => x.StateSource)
            .HasMaxLength(-1);

        // Composite unique index on (DefinitionId, Version): prevents two rows with
        // the same definition + version number, and serves "list versions for definition"
        // queries via leftmost-prefix matching, so a standalone DefinitionId index is
        // redundant. No queries filter on Version alone, so no standalone Version index.
        builder
            .HasIndex(x => new { x.DefinitionId, x.Version })
            .HasDatabaseName($"UX_{nameof(WorkflowDefinitionVersion)}_{nameof(WorkflowDefinitionVersion.DefinitionId)}_{nameof(WorkflowDefinitionVersion.Version)}")
            .IsUnique();

    }
}
