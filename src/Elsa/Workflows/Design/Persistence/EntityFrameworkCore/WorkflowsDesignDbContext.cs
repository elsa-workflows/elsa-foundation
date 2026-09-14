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
}
