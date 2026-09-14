using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore;

public abstract class WorkflowsDesignDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<WorkflowDefinition> Definitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowDefinitionVersion> Versions => Set<WorkflowDefinitionVersion>();
    public DbSet<WorkflowDefinitionDraft> Drafts => Set<WorkflowDefinitionDraft>();
    public DbSet<WorkflowDefinitionDraftLayout> DraftLayouts => Set<WorkflowDefinitionDraftLayout>();
    public DbSet<WorkflowDefinitionVersionLayout> VersionLayouts => Set<WorkflowDefinitionVersionLayout>();
    public DbSet<DesignOperationEntity> Operations => Set<DesignOperationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        DesignEntityConfigurations.ConfigureDefinition(modelBuilder.Entity<WorkflowDefinition>());
        DesignEntityConfigurations.ConfigureVersion(modelBuilder.Entity<WorkflowDefinitionVersion>());
        DesignEntityConfigurations.ConfigureDraft(modelBuilder.Entity<WorkflowDefinitionDraft>());
        DesignEntityConfigurations.ConfigureDraftLayout(modelBuilder.Entity<WorkflowDefinitionDraftLayout>());
        DesignEntityConfigurations.ConfigureVersionLayout(modelBuilder.Entity<WorkflowDefinitionVersionLayout>());
        DesignEntityConfigurations.ConfigureOperation(modelBuilder.Entity<DesignOperationEntity>());
        ConfigureProvider(modelBuilder);
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);

    protected static void ConfigureOrdinalCollation(ModelBuilder modelBuilder, string collation)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        foreach (var property in entityType.GetProperties().Where(property => property.ClrType == typeof(string)))
            property.SetCollation(collation);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareDefinitionKeys();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareDefinitionKeys();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareDefinitionKeys()
    {
        foreach (var entry in ChangeTracker.Entries<WorkflowDefinition>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            Stores.EfDesignSupport.SetDefinitionSearchKeys(this, entry.Entity);
        foreach (var entry in ChangeTracker.Entries<WorkflowDefinitionVersion>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            ValidateIdentity(entry.Entity.Id, nameof(WorkflowDefinitionVersion.Id));
            ValidateOptionalIdentity(entry.Entity.TenantId, nameof(WorkflowDefinitionVersion.TenantId));
            ValidateIdentity(entry.Entity.DefinitionId, nameof(WorkflowDefinitionVersion.DefinitionId));
            ValidateIdentity(entry.Entity.Version, nameof(WorkflowDefinitionVersion.Version));
            ValidateIdentity(entry.Entity.SemVerSortKey, nameof(WorkflowDefinitionVersion.SemVerSortKey));
            ValidateOptionalIdentity(entry.Entity.SourceDraftId, nameof(WorkflowDefinitionVersion.SourceDraftId));
            entry.Entity.DefinitionIdLookupHash = Stores.EfDesignSupport.LookupHash(Stores.EfDesignSupport.SearchKey(entry.Entity.DefinitionId));
        }
        foreach (var entry in ChangeTracker.Entries<WorkflowDefinitionDraft>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            ValidateIdentity(entry.Entity.Id, nameof(WorkflowDefinitionDraft.Id));
            ValidateOptionalIdentity(entry.Entity.TenantId, nameof(WorkflowDefinitionDraft.TenantId));
            ValidateIdentity(entry.Entity.WorkflowDefinitionId, nameof(WorkflowDefinitionDraft.WorkflowDefinitionId));
            ValidateOptionalIdentity(entry.Entity.SourceVersionId, nameof(WorkflowDefinitionDraft.SourceVersionId));
            entry.Entity.WorkflowDefinitionIdLookupHash = Stores.EfDesignSupport.LookupHash(Stores.EfDesignSupport.SearchKey(entry.Entity.WorkflowDefinitionId));
        }
        foreach (var entry in ChangeTracker.Entries<WorkflowDefinitionDraftLayout>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            ValidateIdentity(entry.Entity.Id, nameof(WorkflowDefinitionDraftLayout.Id));
            ValidateOptionalIdentity(entry.Entity.TenantId, nameof(WorkflowDefinitionDraftLayout.TenantId));
            ValidateIdentity(entry.Entity.WorkflowDefinitionDraftId, nameof(WorkflowDefinitionDraftLayout.WorkflowDefinitionDraftId));
        }
        foreach (var entry in ChangeTracker.Entries<WorkflowDefinitionVersionLayout>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            ValidateIdentity(entry.Entity.Id, nameof(WorkflowDefinitionVersionLayout.Id));
            ValidateOptionalIdentity(entry.Entity.TenantId, nameof(WorkflowDefinitionVersionLayout.TenantId));
            ValidateIdentity(entry.Entity.WorkflowDefinitionVersionId, nameof(WorkflowDefinitionVersionLayout.WorkflowDefinitionVersionId));
        }
        foreach (var entry in ChangeTracker.Entries<DesignOperationEntity>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            ValidateOptionalIdentity(entry.Entity.TenantId, nameof(DesignOperationEntity.TenantId));
            ValidateBounded(entry.Entity.OperationKind, DesignOperationKey.MaximumLength, nameof(DesignOperationEntity.OperationKind));
            ValidateBounded(entry.Entity.OperationKey, DesignOperationKey.MaximumLength, nameof(DesignOperationEntity.OperationKey));
            ValidateBounded(entry.Entity.RequestFingerprint, 128, nameof(DesignOperationEntity.RequestFingerprint));
            ValidateBounded(entry.Entity.ResultFingerprint, 128, nameof(DesignOperationEntity.ResultFingerprint));
        }
    }

    private static void ValidateIdentity(string value, string parameterName) => WorkflowDefinitionLimits.ValidateIdentity(value, parameterName);

    private static void ValidateOptionalIdentity(string? value, string parameterName)
    {
        if (value is not null)
            ValidateIdentity(value, parameterName);
    }

    private static void ValidateBounded(string value, int maximumLength, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length > maximumLength)
            throw new ArgumentException($"Workflow-design values cannot exceed {maximumLength} UTF-16 code units.", parameterName);
    }

    protected static void ConfigureDateTime(ModelBuilder modelBuilder, string columnType)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        foreach (var property in entityType.GetProperties().Where(x => x.ClrType == typeof(DateTimeOffset) || x.ClrType == typeof(DateTimeOffset?)))
            property.SetColumnType(columnType);
    }

    protected static void ConfigureLongText(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        foreach (var property in entityType.GetProperties().Where(x => x.Name is "StateSource" or "RecordsJson" or "ActivityPresentationJson" or "ResultJson"))
            property.SetColumnType("longtext");
    }

    protected static void ConfigureText(ModelBuilder modelBuilder, string columnType)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        foreach (var property in entityType.GetProperties().Where(x => x.Name is "StateSource" or "RecordsJson" or "ActivityPresentationJson" or "ResultJson"))
            property.SetColumnType(columnType);
    }
}
