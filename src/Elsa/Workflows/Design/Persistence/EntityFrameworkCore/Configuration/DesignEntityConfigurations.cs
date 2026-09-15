using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Elsa.Primitives.Entities;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Configuration;

internal static class DesignEntityConfigurations
{
    private const int ScopeKeyMaximumLength = 65;

    private static void ConfigureScopeKey<T>(EntityTypeBuilder<T> b) where T : class
    {
        b.Property<string>(Stores.EfDesignSupport.ScopeKeyProperty)
            .HasMaxLength(ScopeKeyMaximumLength)
            .IsRequired()
            .HasValueGenerator<TenantScopeKeyValueGenerator>();
    }

    public static void ConfigureDefinition(EntityTypeBuilder<WorkflowDefinition> b)
    {
        b.ToTable(WorkflowsDesignEfModule.DefinitionTable);
        ConfigureScopeKey(b);
        // SQL Server pads trailing spaces even under BIN2 collations. The folded hash therefore
        // owns relational identity while Id remains the exact domain value returned to callers.
        b.Property(x => x.IdLookupHash)
            .HasMaxLength(64)
            .IsRequired()
            .HasValueGenerator<WorkflowDefinitionIdentityHashValueGenerator>();
        b.HasKey(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinition.IdLookupHash));
        b.Ignore(x => x.RowNumber);
        b.Property(x => x.Id).HasMaxLength(WorkflowDefinitionLimits.IdentityMaximumLength);
        b.Property(x => x.Id).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.TenantId).HasMaxLength(WorkflowDefinitionLimits.IdentityMaximumLength);
        b.Property(x => x.TenantId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        b.Property(x => x.Name).HasMaxLength(WorkflowDefinitionLimits.TextMaximumLength);
        b.Property(x => x.Description).HasMaxLength(WorkflowDefinitionLimits.TextMaximumLength);
        // Persisted folded keys keep ordinal-ignore-case search provider-neutral while the
        // logical entity remains a domain type. They are part of the definition storage contract.
        b.Property(x => x.IdSearchKey).HasMaxLength(WorkflowDefinitionLimits.IdentitySearchKeyMaximumLength).IsRequired();
        b.Property(x => x.NameSearchKey).HasMaxLength(WorkflowDefinitionLimits.TextSearchKeyMaximumLength);
        b.Property(x => x.DescriptionSearchKey).HasMaxLength(WorkflowDefinitionLimits.TextSearchKeyMaximumLength);
        b.HasIndex(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinition.Name), nameof(WorkflowDefinition.Id));
        b.HasIndex(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinition.Id));
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

    private sealed class ExactIdentityHashValueGenerator : ValueGenerator<string>
    {
        public override bool GeneratesTemporaryValues => false;

        public override string Next(EntityEntry entry) =>
            EfDesignSupport.LookupHash((string)entry.Property("Id").CurrentValue!);
    }

    public static void ConfigureVersion(EntityTypeBuilder<WorkflowDefinitionVersion> b)
    {
        b.ToTable(WorkflowsDesignEfModule.VersionTable);
        ConfigureScopeKey(b);
        b.HasKey(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinitionVersion.IdLookupHash));
        b.Ignore(x => x.RowNumber);
        b.Property(x => x.Id).HasMaxLength(128);
        b.Property(x => x.Id).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.IdLookupHash).HasMaxLength(64).IsRequired().HasValueGenerator<ExactIdentityHashValueGenerator>();
        b.Property(x => x.TenantId).HasMaxLength(128);
        b.Property(x => x.TenantId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
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
        b.HasIndex(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinitionVersion.DefinitionIdLookupHash), nameof(WorkflowDefinitionVersion.SemVerSortKey))
            .IsUnique()
            .HasDatabaseName(WorkflowsDesignEfModule.VersionIdentityIndex);
        b.HasOne(x => x.Definition).WithMany()
            .HasForeignKey(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinitionVersion.DefinitionIdLookupHash))
            .HasPrincipalKey(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinition.IdLookupHash))
            .OnDelete(DeleteBehavior.Cascade);
        b.Property(x => x.LastModifiedAt).IsConcurrencyToken();
        b.Ignore(x => x.State);
    }

    public static void ConfigureDraft(EntityTypeBuilder<WorkflowDefinitionDraft> b)
    {
        b.ToTable(WorkflowsDesignEfModule.DraftTable);
        ConfigureScopeKey(b);
        b.HasKey(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinitionDraft.IdLookupHash));
        b.Ignore(x => x.RowNumber);
        b.Property(x => x.Id).HasMaxLength(128);
        b.Property(x => x.Id).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.IdLookupHash).HasMaxLength(64).IsRequired().HasValueGenerator<ExactIdentityHashValueGenerator>();
        b.Property(x => x.TenantId).HasMaxLength(128);
        b.Property(x => x.TenantId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        b.Property(x => x.WorkflowDefinitionId).HasMaxLength(128);
        b.Property(x => x.WorkflowDefinitionIdLookupHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.SourceVersionId).HasMaxLength(128);
        b.Property(x => x.SourceVersionId).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.StateSource);
        b.HasIndex(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinitionDraft.WorkflowDefinitionIdLookupHash), nameof(WorkflowDefinitionDraft.LastModifiedAt), nameof(WorkflowDefinitionDraft.IdLookupHash));
        b.HasOne(x => x.WorkflowDefinition).WithMany()
            .HasForeignKey(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinitionDraft.WorkflowDefinitionIdLookupHash))
            .HasPrincipalKey(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinition.IdLookupHash))
            .OnDelete(DeleteBehavior.Cascade);
        // Draft updates are explicitly last-writer-wins; stale writers must not be rejected by EF.
        b.Ignore(x => x.State);
    }

    public static void ConfigureDraftLayout(EntityTypeBuilder<WorkflowDefinitionDraftLayout> b)
    {
        b.ToTable(WorkflowsDesignEfModule.DraftLayoutTable);
        ConfigureScopeKey(b);
        b.HasKey(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinitionDraftLayout.IdLookupHash));
        b.Ignore(x => x.RowNumber);
        b.Property(x => x.Id).HasMaxLength(128);
        b.Property(x => x.IdLookupHash).HasMaxLength(64).IsRequired().HasValueGenerator<ExactIdentityHashValueGenerator>();
        b.Property(x => x.TenantId).HasMaxLength(128);
        b.Property(x => x.TenantId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        b.Property(x => x.WorkflowDefinitionDraftId).HasMaxLength(128);
        b.Property(x => x.WorkflowDefinitionDraftId).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.WorkflowDefinitionDraftIdLookupHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.RecordsJson);
        b.Property(x => x.ActivityPresentationJson);
        b.HasIndex(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinitionDraftLayout.WorkflowDefinitionDraftIdLookupHash)).IsUnique();
        b.HasOne(x => x.WorkflowDefinitionDraft).WithOne()
            .HasForeignKey<WorkflowDefinitionDraftLayout>(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinitionDraftLayout.WorkflowDefinitionDraftIdLookupHash))
            .HasPrincipalKey<WorkflowDefinitionDraft>(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinitionDraft.IdLookupHash))
            .OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.Records);
        b.Ignore(x => x.ActivityPresentation);
        b.Property(x => x.LastModifiedAt).IsConcurrencyToken();
    }

    public static void ConfigureVersionLayout(EntityTypeBuilder<WorkflowDefinitionVersionLayout> b)
    {
        b.ToTable(WorkflowsDesignEfModule.VersionLayoutTable);
        ConfigureScopeKey(b);
        b.HasKey(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinitionVersionLayout.IdLookupHash));
        b.Ignore(x => x.RowNumber);
        b.Property(x => x.Id).HasMaxLength(128);
        b.Property(x => x.Id).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        b.Property(x => x.IdLookupHash).HasMaxLength(64).IsRequired().HasValueGenerator<ExactIdentityHashValueGenerator>();
        b.Property(x => x.TenantId).HasMaxLength(128);
        b.Property(x => x.TenantId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        b.Property(x => x.WorkflowDefinitionVersionId).HasMaxLength(128);
        b.Property(x => x.WorkflowDefinitionVersionIdLookupHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.RecordsJson);
        b.Property(x => x.ActivityPresentationJson);
        b.HasIndex(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinitionVersionLayout.WorkflowDefinitionVersionIdLookupHash)).IsUnique();
        b.HasOne(x => x.WorkflowDefinitionVersion).WithOne()
            .HasForeignKey<WorkflowDefinitionVersionLayout>(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinitionVersionLayout.WorkflowDefinitionVersionIdLookupHash))
            .HasPrincipalKey<WorkflowDefinitionVersion>(Stores.EfDesignSupport.ScopeKeyProperty, nameof(WorkflowDefinitionVersion.IdLookupHash))
            .OnDelete(DeleteBehavior.Cascade);
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
        ConfigureScopeKey(b);
        b.Property(x => x.TenantId).HasMaxLength(128);
        b.Property(x => x.OperationKind).HasMaxLength(256);
        b.Property(x => x.OperationKey).HasMaxLength(256);
        b.Property(x => x.OperationKindLookupHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.OperationKeyLookupHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.RequestFingerprint).HasMaxLength(128);
        b.Property(x => x.ResultFingerprint).HasMaxLength(128);
        b.Property(x => x.ResultJson);
        // SQL Server ignores trailing spaces in string equality and unique indexes, even under
        // binary collations. Hashes over the exact UTF-8 values own identity; raw values remain
        // available for diagnostics and residual ordinal validation after a hash match.
        // The exact operation identity is the key; a database-generated surrogate would need a
        // provider-specific identity strategy that a provider-free module cannot express.
        b.HasKey(Stores.EfDesignSupport.ScopeKeyProperty, nameof(DesignOperationEntity.OperationKindLookupHash), nameof(DesignOperationEntity.OperationKeyLookupHash));
    }

    private sealed class TenantScopeKeyValueGenerator : ValueGenerator<string>
    {
        public override bool GeneratesTemporaryValues => false;

        public override string Next(EntityEntry entry) => entry.Entity switch
        {
            TenantEntity tenant => Stores.EfDesignSupport.ScopeKey(tenant.TenantId),
            DesignOperationEntity operation => Stores.EfDesignSupport.ScopeKey(operation.TenantId),
            _ => throw new InvalidOperationException($"The {Stores.EfDesignSupport.ScopeKeyProperty} generator cannot process {entry.Entity.GetType().Name}.")
        };
    }
}
