using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Configuration;

internal static class DesignEntityConfigurations
{
    public static void ConfigureDefinition(EntityTypeBuilder<WorkflowDefinition> b)
    {
        b.ToTable(WorkflowsDesignEfModule.DefinitionTable);
        b.HasKey(x => new { x.TenantId, x.Id });
        b.Ignore(x => x.RowNumber);
        b.Property(x => x.Id).HasMaxLength(128);
        b.Property(x => x.TenantId).HasMaxLength(128);
        b.Property(x => x.Name).HasMaxLength(128);
        b.Property(x => x.Description).HasMaxLength(4000);
        b.HasIndex(x => new { x.TenantId, x.Name, x.Id });
        b.HasIndex(x => new { x.TenantId, x.Id });
        b.Property(x => x.LastModifiedAt).IsConcurrencyToken();
    }

    public static void ConfigureVersion(EntityTypeBuilder<WorkflowDefinitionVersion> b)
    {
        b.ToTable(WorkflowsDesignEfModule.VersionTable);
        b.HasKey(x => new { x.TenantId, x.Id });
        b.Ignore(x => x.RowNumber);
        b.Property(x => x.Id).HasMaxLength(128);
        b.Property(x => x.TenantId).HasMaxLength(128);
        b.Property(x => x.DefinitionId).HasMaxLength(128);
        b.Property(x => x.Version).HasMaxLength(128);
        b.Property(x => x.SemVerSortKey).HasMaxLength(128);
        b.Property(x => x.StateSource).HasColumnType("TEXT");
        b.Property(x => x.Version).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.DefinitionId).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.SourceDraftId).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.StateSource).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.HasIndex(x => new { x.TenantId, x.DefinitionId, x.SemVerSortKey }).IsUnique();
        b.HasOne(x => x.Definition).WithMany().HasForeignKey("TenantId", "DefinitionId").OnDelete(DeleteBehavior.Cascade);
        b.Property(x => x.LastModifiedAt).IsConcurrencyToken();
        b.Ignore(x => x.State);
    }

    public static void ConfigureDraft(EntityTypeBuilder<WorkflowDefinitionDraft> b)
    {
        b.ToTable(WorkflowsDesignEfModule.DraftTable);
        b.HasKey(x => new { x.TenantId, x.Id });
        b.Ignore(x => x.RowNumber);
        b.Property(x => x.Id).HasMaxLength(128);
        b.Property(x => x.TenantId).HasMaxLength(128);
        b.Property(x => x.WorkflowDefinitionId).HasMaxLength(128);
        b.Property(x => x.SourceVersionId).HasMaxLength(128);
        b.Property(x => x.StateSource).HasColumnType("TEXT");
        b.HasIndex(x => new { x.TenantId, x.WorkflowDefinitionId, x.LastModifiedAt, x.Id });
        b.HasOne(x => x.WorkflowDefinition).WithMany().HasForeignKey("TenantId", "WorkflowDefinitionId").OnDelete(DeleteBehavior.Cascade);
        // Draft updates are explicitly last-writer-wins; stale writers must not be rejected by EF.
        b.Ignore(x => x.State);
    }

    public static void ConfigureDraftLayout(EntityTypeBuilder<WorkflowDefinitionDraftLayout> b)
    {
        b.ToTable(WorkflowsDesignEfModule.DraftLayoutTable);
        b.HasKey(x => new { x.TenantId, x.Id });
        b.Ignore(x => x.RowNumber);
        b.Property(x => x.Id).HasMaxLength(128);
        b.Property(x => x.TenantId).HasMaxLength(128);
        b.Property(x => x.WorkflowDefinitionDraftId).HasMaxLength(128);
        b.Property<string>("RecordsJson").HasColumnType("TEXT");
        b.Property<string>("ActivityPresentationJson").HasColumnType("TEXT");
        b.HasIndex(x => new { x.TenantId, x.WorkflowDefinitionDraftId }).IsUnique();
        b.HasOne(x => x.WorkflowDefinitionDraft).WithOne().HasForeignKey<WorkflowDefinitionDraftLayout>("TenantId", "WorkflowDefinitionDraftId").OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.Records);
        b.Ignore(x => x.ActivityPresentation);
        b.Property(x => x.LastModifiedAt).IsConcurrencyToken();
    }

    public static void ConfigureVersionLayout(EntityTypeBuilder<WorkflowDefinitionVersionLayout> b)
    {
        b.ToTable(WorkflowsDesignEfModule.VersionLayoutTable);
        b.HasKey(x => new { x.TenantId, x.Id });
        b.Ignore(x => x.RowNumber);
        b.Property(x => x.Id).HasMaxLength(128);
        b.Property(x => x.TenantId).HasMaxLength(128);
        b.Property(x => x.WorkflowDefinitionVersionId).HasMaxLength(128);
        b.Property<string>("RecordsJson").HasColumnType("TEXT");
        b.Property<string>("ActivityPresentationJson").HasColumnType("TEXT");
        b.HasIndex(x => new { x.TenantId, x.WorkflowDefinitionVersionId }).IsUnique();
        b.HasOne(x => x.WorkflowDefinitionVersion).WithOne().HasForeignKey<WorkflowDefinitionVersionLayout>("TenantId", "WorkflowDefinitionVersionId").OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.Records);
        b.Ignore(x => x.ActivityPresentation);
        b.Property(x => x.WorkflowDefinitionVersionId).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.LastModifiedAt).IsConcurrencyToken();
    }

    public static void ConfigureOperation(EntityTypeBuilder<DesignOperationEntity> b)
    {
        b.ToTable(WorkflowsDesignEfModule.OperationTable);
        b.HasKey(x => x.RowNumber);
        b.Property(x => x.TenantId).HasMaxLength(128);
        b.Property(x => x.OperationKind).HasMaxLength(200);
        b.Property(x => x.OperationKey).HasMaxLength(512);
        b.Property(x => x.RequestFingerprint).HasMaxLength(128);
        b.Property(x => x.ResultFingerprint).HasMaxLength(128);
        b.Property(x => x.ResultJson).HasColumnType("TEXT");
        b.HasIndex(x => new { x.TenantId, x.OperationKind, x.OperationKey }).IsUnique();
    }
}
