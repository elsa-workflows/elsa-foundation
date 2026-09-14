using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Configuration;

internal static class DesignEntityConfigurations
{
    public static void ConfigureDefinition(EntityTypeBuilder<WorkflowDefinition> b)
    {
        b.ToTable(WorkflowsDesignEfModule.DefinitionTable);
        // SQL Server pads trailing spaces even under BIN2 collations. The folded hash therefore
        // owns relational identity while Id remains the exact domain value returned to callers.
        b.Property(x => x.IdLookupHash)
            .HasMaxLength(64)
            .IsRequired()
            .HasValueGenerator<WorkflowDefinitionIdentityHashValueGenerator>();
        b.HasKey(x => new { x.TenantId, x.IdLookupHash });
        b.Ignore(x => x.RowNumber);
        b.Property(x => x.Id).HasMaxLength(WorkflowDefinitionLimits.IdentityMaximumLength);
        b.Property(x => x.Id).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.TenantId).HasMaxLength(WorkflowDefinitionLimits.IdentityMaximumLength);
        b.Property(x => x.Name).HasMaxLength(WorkflowDefinitionLimits.TextMaximumLength);
        b.Property(x => x.Description).HasMaxLength(WorkflowDefinitionLimits.TextMaximumLength);
        // Persisted folded keys keep ordinal-ignore-case search provider-neutral while the
        // logical entity remains a domain type. They are part of the definition storage contract.
        b.Property(x => x.IdSearchKey).HasMaxLength(WorkflowDefinitionLimits.IdentitySearchKeyMaximumLength).IsRequired();
        b.Property(x => x.NameSearchKey).HasMaxLength(WorkflowDefinitionLimits.TextSearchKeyMaximumLength);
        b.Property(x => x.DescriptionSearchKey).HasMaxLength(WorkflowDefinitionLimits.TextSearchKeyMaximumLength);
        b.HasIndex(x => new { x.TenantId, x.Name, x.Id });
        b.HasIndex(x => new { x.TenantId, x.Id });
        b.Property(x => x.LastModifiedAt).IsConcurrencyToken();
    }

    private sealed class WorkflowDefinitionIdentityHashValueGenerator : ValueGenerator<string>
    {
        public override bool GeneratesTemporaryValues => false;

        public override string Next(EntityEntry entry)
        {
            var definition = (WorkflowDefinition)entry.Entity;
            return EfDesignSupport.LookupHash(EfDesignSupport.SearchKey(definition.Id));
        }
    }

    public static void ConfigureVersion(EntityTypeBuilder<WorkflowDefinitionVersion> b)
    {
        b.ToTable(WorkflowsDesignEfModule.VersionTable);
        b.HasKey(x => new { x.TenantId, x.Id });
        b.Ignore(x => x.RowNumber);
        b.Property(x => x.Id).HasMaxLength(128);
        b.Property(x => x.TenantId).HasMaxLength(128);
        b.Property(x => x.DefinitionId).HasMaxLength(128);
        b.Property(x => x.DefinitionIdLookupHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.Version).HasMaxLength(128);
        b.Property(x => x.SemVerSortKey).HasMaxLength(128);
        b.Property(x => x.StateSource);
        b.Property(x => x.Version).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.SemVerSortKey).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.DefinitionId).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.SourceDraftId).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.SourceCreatedAt).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.StateSource).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.HasIndex(x => new { x.TenantId, x.DefinitionIdLookupHash, x.SemVerSortKey }).IsUnique();
        b.HasOne(x => x.Definition).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DefinitionIdLookupHash })
            .HasPrincipalKey(x => new { x.TenantId, x.IdLookupHash })
            .OnDelete(DeleteBehavior.Cascade);
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
        b.Property(x => x.WorkflowDefinitionIdLookupHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.SourceVersionId).HasMaxLength(128);
        b.Property(x => x.SourceVersionId).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.StateSource);
        b.HasIndex(x => new { x.TenantId, x.WorkflowDefinitionIdLookupHash, x.LastModifiedAt, x.Id });
        b.HasOne(x => x.WorkflowDefinition).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.WorkflowDefinitionIdLookupHash })
            .HasPrincipalKey(x => new { x.TenantId, x.IdLookupHash })
            .OnDelete(DeleteBehavior.Cascade);
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
        b.Property(x => x.RecordsJson);
        b.Property(x => x.ActivityPresentationJson);
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
        b.Property(x => x.RecordsJson);
        b.Property(x => x.ActivityPresentationJson);
        b.HasIndex(x => new { x.TenantId, x.WorkflowDefinitionVersionId }).IsUnique();
        b.HasOne(x => x.WorkflowDefinitionVersion).WithOne().HasForeignKey<WorkflowDefinitionVersionLayout>("TenantId", "WorkflowDefinitionVersionId").OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.Records);
        b.Ignore(x => x.ActivityPresentation);
        b.Property(x => x.WorkflowDefinitionVersionId).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.RecordsJson).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.ActivityPresentationJson).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.LastModifiedAt).IsConcurrencyToken();
    }

    public static void ConfigureOperation(EntityTypeBuilder<DesignOperationEntity> b)
    {
        b.ToTable(WorkflowsDesignEfModule.OperationTable);
        b.HasKey(x => x.RowNumber);
        b.Property(x => x.TenantId).HasMaxLength(128);
        b.Property(x => x.OperationKind).HasMaxLength(256);
        b.Property(x => x.OperationKey).HasMaxLength(256);
        b.Property(x => x.RequestFingerprint).HasMaxLength(128);
        b.Property(x => x.ResultFingerprint).HasMaxLength(128);
        b.Property(x => x.ResultJson);
        b.HasIndex(x => new { x.TenantId, x.OperationKind, x.OperationKey }).IsUnique();
    }
}
