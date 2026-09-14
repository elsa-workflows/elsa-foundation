using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Provider-neutral model for R01 bookmark state and stimulus lookup.</summary>
public abstract class BookmarkStateDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<BookmarkStateEntity> Bookmarks => Set<BookmarkStateEntity>();
    public DbSet<WorkflowExecutableEntity> WorkflowExecutables => Set<WorkflowExecutableEntity>();
    public DbSet<WorkflowExecutableCoordinationEntity> WorkflowExecutableCoordinations => Set<WorkflowExecutableCoordinationEntity>();
    public DbSet<ExecutableActivityTemplateEntity> ExecutableActivityTemplates => Set<ExecutableActivityTemplateEntity>();
    public DbSet<ExecutableActivityTemplateHashClaimEntity> ExecutableActivityTemplateHashClaims => Set<ExecutableActivityTemplateHashClaimEntity>();
    public DbSet<WorkflowExecutableSourceReferenceEntity> WorkflowExecutableSourceReferences => Set<WorkflowExecutableSourceReferenceEntity>();
    public DbSet<ActivityExecutionStateEntity> ActivityExecutionStates => Set<ActivityExecutionStateEntity>();
    public DbSet<ActivityExecutionInspectionEntity> ActivityExecutionInspections => Set<ActivityExecutionInspectionEntity>();
    public DbSet<ActivityExecutionHierarchyEntity> ActivityExecutionHierarchies => Set<ActivityExecutionHierarchyEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new BookmarkStateEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowExecutableEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowExecutableCoordinationEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ExecutableActivityTemplateEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ExecutableActivityTemplateHashClaimEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowExecutableSourceReferenceEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ActivityExecutionStateEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ActivityExecutionInspectionEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ActivityExecutionHierarchyEntityConfiguration());
        ConfigureProvider(modelBuilder);
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}
