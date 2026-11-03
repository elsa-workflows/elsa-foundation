using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Configuration;

internal static class WorkflowLibraryEntityConfigurations
{
    private static EntityTypeBuilder<T> Table<T>(ModelBuilder model, string name) where T : TenantEntity
    {
        var b = model.Entity<T>();
        b.ToTable(name);
        b.Property<string>(EfDesignSupport.ScopeKeyProperty).HasMaxLength(65).IsRequired().HasValueGenerator<LibraryScopeKeyGenerator>();
        b.HasKey(EfDesignSupport.ScopeKeyProperty, nameof(TenantEntity.Id));
        b.Property(row => row.Id).HasMaxLength(64).IsUnicode(false);
        b.Property(row => row.TenantId).HasMaxLength(128);
        b.Property<string>(EfSchemaVersionMaterializationInterceptor.PropertyName).HasMaxLength(32).IsRequired();
        b.Ignore(row => row.RowNumber);
        return b;
    }

    private sealed class LibraryScopeKeyGenerator : ValueGenerator<string>
    {
        public override bool GeneratesTemporaryValues => false;
        public override string Next(EntityEntry entry) => EfDesignSupport.ScopeKey(((TenantEntity)entry.Entity).TenantId);
    }

    public static void Configure(ModelBuilder model)
    {
        var folders = Table<WorkflowFolderEntity>(model, "elsa_workflow_folders");
        folders.Property(row => row.ParentId).HasMaxLength(64).IsUnicode(false);
        folders.Property(row => row.ParentKey).HasMaxLength(64).IsUnicode(false).IsRequired();
        folders.Property(row => row.Name).HasMaxLength(256).IsRequired();
        folders.Property(row => row.NormalizedName).HasMaxLength(512).IsRequired();
        folders.Property(row => row.NameHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        folders.Property(row => row.Path).HasMaxLength(641).IsUnicode(false).IsRequired();
        folders.Property(row => row.Revision).HasMaxLength(32).IsUnicode(false).IsConcurrencyToken();
        folders.HasIndex(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowFolderEntity.ParentKey), nameof(WorkflowFolderEntity.NameHash)).IsUnique();
        folders.HasIndex(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowFolderEntity.Path));
        folders.HasOne<WorkflowFolderEntity>().WithMany().HasForeignKey(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowFolderEntity.ParentId))
            .HasPrincipalKey(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowFolderEntity.Id)).OnDelete(DeleteBehavior.Restrict);

        var organizations = Table<WorkflowOrganizationEntity>(model, "elsa_workflow_organization");
        organizations.Property(row => row.DefinitionHash).HasMaxLength(64);
        organizations.Property(row => row.DefinitionId).HasMaxLength(128);
        organizations.Property(row => row.FolderId).HasMaxLength(64).IsUnicode(false);
        organizations.Property(row => row.Revision).HasMaxLength(32).IsUnicode(false).IsConcurrencyToken();
        organizations.HasIndex(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowOrganizationEntity.DefinitionHash)).IsUnique();
        organizations.HasIndex(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowOrganizationEntity.FolderId));
        organizations.HasIndex(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowOrganizationEntity.Published), nameof(WorkflowOrganizationEntity.DefinitionHash));
        organizations.HasOne<WorkflowDefinition>().WithMany().HasForeignKey(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowOrganizationEntity.DefinitionHash))
            .HasPrincipalKey(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinition.IdLookupHash)).OnDelete(DeleteBehavior.Cascade);
        organizations.HasOne<WorkflowFolderEntity>().WithMany().HasForeignKey(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowOrganizationEntity.FolderId))
            .HasPrincipalKey(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowFolderEntity.Id)).OnDelete(DeleteBehavior.Restrict);

        var tags = Table<WorkflowTagEntity>(model, "elsa_workflow_tags");
        tags.Property(row => row.Name).HasMaxLength(256).IsRequired();
        tags.Property(row => row.NormalizedName).HasMaxLength(512).IsRequired();
        tags.Property(row => row.NameHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        tags.Property(row => row.Description).HasMaxLength(2048);
        tags.Property(row => row.Mode).HasMaxLength(16).IsUnicode(false);
        tags.Property(row => row.Cardinality).HasMaxLength(16).IsUnicode(false);
        tags.Property(row => row.Revision).HasMaxLength(32).IsUnicode(false).IsConcurrencyToken();
        tags.Property(row => row.ValuesJson).IsRequired();
        tags.HasIndex(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowTagEntity.NameHash)).IsUnique();

        var assignments = Table<WorkflowTagAssignmentEntity>(model, "elsa_workflow_tag_assignments");
        assignments.Property(row => row.DefinitionHash).HasMaxLength(64);
        assignments.Property(row => row.TagId).HasMaxLength(64).IsUnicode(false);
        assignments.Property(row => row.ValueKey).HasMaxLength(64).IsUnicode(false);
        assignments.HasIndex(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowTagAssignmentEntity.DefinitionHash), nameof(WorkflowTagAssignmentEntity.TagId), nameof(WorkflowTagAssignmentEntity.ValueKey)).IsUnique();
        assignments.HasIndex(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowTagAssignmentEntity.TagId), nameof(WorkflowTagAssignmentEntity.ValueKey), nameof(WorkflowTagAssignmentEntity.DefinitionHash));
        assignments.HasOne<WorkflowDefinition>().WithMany().HasForeignKey(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowTagAssignmentEntity.DefinitionHash))
            .HasPrincipalKey(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinition.IdLookupHash)).OnDelete(DeleteBehavior.Cascade);
        assignments.HasOne<WorkflowTagEntity>().WithMany().HasForeignKey(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowTagAssignmentEntity.TagId))
            .HasPrincipalKey(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowTagEntity.Id)).OnDelete(DeleteBehavior.Restrict);

        var views = Table<WorkflowLibraryViewEntity>(model, "elsa_workflow_library_views");
        views.Property(row => row.Name).HasMaxLength(256).IsRequired();
        views.Property(row => row.OwnerId).HasMaxLength(256).IsRequired();
        views.Property(row => row.OwnerHash).HasMaxLength(64).IsUnicode(false);
        views.Property(row => row.Revision).HasMaxLength(32).IsUnicode(false).IsConcurrencyToken();
        views.Property(row => row.ContentJson).IsRequired();
        views.HasIndex(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowLibraryViewEntity.OwnerHash));
        views.HasIndex(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowLibraryViewEntity.Shared));

        var favorites = Table<WorkflowFavoriteEntity>(model, "elsa_workflow_favorites");
        favorites.Property(row => row.DefinitionHash).HasMaxLength(64);
        favorites.Property(row => row.SubjectId).HasMaxLength(256).IsRequired();
        favorites.Property(row => row.SubjectHash).HasMaxLength(64).IsUnicode(false);
        favorites.HasIndex(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowFavoriteEntity.SubjectHash), nameof(WorkflowFavoriteEntity.DefinitionHash)).IsUnique();
        favorites.HasOne<WorkflowDefinition>().WithMany().HasForeignKey(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowFavoriteEntity.DefinitionHash))
            .HasPrincipalKey(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinition.IdLookupHash)).OnDelete(DeleteBehavior.Cascade);

        var audits = Table<WorkflowLibraryAuditEntity>(model, "elsa_workflow_library_audit");
        audits.Property(row => row.TargetId).HasMaxLength(128);
        audits.Property(row => row.ActorId).HasMaxLength(256);
        audits.Property(row => row.Operation).HasMaxLength(64).IsUnicode(false);
        audits.Property(row => row.OperationKey).HasMaxLength(256);
        audits.Property(row => row.BeforeJson).IsRequired();
        audits.Property(row => row.AfterJson).IsRequired();
        audits.HasIndex(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowLibraryAuditEntity.TargetId));

        var fence = Table<WorkflowLibraryFenceEntity>(model, "elsa_workflow_library_fence");
        fence.Property(row => row.Revision).HasMaxLength(32).IsUnicode(false).IsConcurrencyToken();
        model.Entity<WorkflowDefinition>().Property<long>("LibraryModifiedTicks");
        model.Entity<WorkflowDefinition>().HasIndex(EfDesignSupport.ScopeKeyProperty, "LibraryModifiedTicks", nameof(WorkflowDefinition.IdLookupHash));
        model.Entity<WorkflowDefinitionDraft>().Property<long>("LibraryModifiedTicks");
        model.Entity<WorkflowDefinitionDraft>().Property<long>("LibraryCreatedTicks");
        model.Entity<WorkflowDefinitionDraft>().Property<string>("LibraryIdentitySortKey").HasMaxLength(512).IsUnicode(false).IsRequired();
        model.Entity<WorkflowDefinitionDraft>().HasIndex(EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinitionDraft.WorkflowDefinitionIdLookupHash), "LibraryModifiedTicks", "LibraryCreatedTicks", "LibraryIdentitySortKey");
    }
}
