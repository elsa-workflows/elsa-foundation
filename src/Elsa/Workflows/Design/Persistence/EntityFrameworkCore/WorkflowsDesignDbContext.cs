using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
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
            Entry(entry.Entity).Property<string>("DefinitionIdLookupHash").CurrentValue = Stores.EfDesignSupport.LookupHash(Stores.EfDesignSupport.SearchKey(entry.Entity.DefinitionId));
        foreach (var entry in ChangeTracker.Entries<WorkflowDefinitionDraft>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            Entry(entry.Entity).Property<string>("WorkflowDefinitionIdLookupHash").CurrentValue = Stores.EfDesignSupport.LookupHash(Stores.EfDesignSupport.SearchKey(entry.Entity.WorkflowDefinitionId));
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
