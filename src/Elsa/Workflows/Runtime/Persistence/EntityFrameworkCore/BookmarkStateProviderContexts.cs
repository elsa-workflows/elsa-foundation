using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

public sealed class BookmarkStateSqliteDbContext(DbContextOptions<BookmarkStateSqliteDbContext> options) : BookmarkStateDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => BookmarkStateProviderModel.ConfigureText(modelBuilder, "TEXT");
}

public sealed class BookmarkStateSqlServerDbContext(DbContextOptions<BookmarkStateSqlServerDbContext> options) : BookmarkStateDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => BookmarkStateProviderModel.ConfigureText(modelBuilder, "nvarchar(max)");
}

public sealed class BookmarkStatePostgreSqlDbContext(DbContextOptions<BookmarkStatePostgreSqlDbContext> options) : BookmarkStateDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => BookmarkStateProviderModel.ConfigureText(modelBuilder, "text");
}

public sealed class BookmarkStateMySqlDbContext(DbContextOptions<BookmarkStateMySqlDbContext> options) : BookmarkStateDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.MySql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => BookmarkStateProviderModel.ConfigureText(modelBuilder, "longtext");
}

file static class BookmarkStateProviderModel
{
    public static void ConfigureText(ModelBuilder modelBuilder, string type)
    {
        modelBuilder.Entity<BookmarkStateEntity>().Property(row => row.ScopeKey).HasColumnType(type);
        modelBuilder.Entity<BookmarkStateEntity>().Property(row => row.PayloadJson).HasColumnType(type);
        modelBuilder.Entity<BookmarkStateEntity>().Property(row => row.ContentJson).HasColumnType(type);
        modelBuilder.Entity<BookmarkStateEntity>().Property(row => row.MetadataJson).HasColumnType(type);
        foreach (var entity in new[] { typeof(WorkflowExecutableEntity), typeof(WorkflowExecutableCoordinationEntity), typeof(ExecutableActivityTemplateEntity), typeof(ExecutableActivityTemplateHashClaimEntity), typeof(WorkflowExecutableSourceReferenceEntity), typeof(ActivityExecutionStateEntity), typeof(ActivityExecutionInspectionEntity), typeof(ActivityExecutionHierarchyEntity) })
        {
            modelBuilder.Entity(entity).Property("ContentJson").HasColumnType(type);
            modelBuilder.Entity(entity).Property("ScopeKey").HasColumnType(type);
            if (entity == typeof(WorkflowExecutableSourceReferenceEntity))
                modelBuilder.Entity(entity).Property("ScopeKeyOrderKey").HasColumnType(type);
        }
        modelBuilder.Entity<WorkflowExecutionStateEntity>().Property("ContentJson").HasColumnType(type);
        foreach (var entity in new[] { typeof(WorkflowAlterationPlanEntity), typeof(WorkflowAlterationJobEntity), typeof(WorkflowTestScopeEntity) })
            modelBuilder.Entity(entity).Property("ContentJson").HasColumnType(type);
        foreach (var entity in new[] { typeof(DurableValueStateEntity), typeof(SchedulerStateEntity) })
        {
            modelBuilder.Entity(entity).Property("ContentJson").HasColumnType(type);
            modelBuilder.Entity(entity).Property("ScopeKey").HasColumnType(type);
        }
        foreach (var entity in new[] { typeof(ExecutionLivenessStateEntity), typeof(WorkflowHoldStateEntity) })
        {
            modelBuilder.Entity(entity).Property("ContentJson").HasColumnType(type);
            modelBuilder.Entity(entity).Property("ScopeKey").HasColumnType(type);
        }
        modelBuilder.Entity<IncidentStateEntity>().Property("ContentJson").HasColumnType(type);
        modelBuilder.Entity<IncidentStateEntity>().Property("ScopeKey").HasColumnType(type);
        modelBuilder.Entity<RuntimeCheckpointCommitEntity>().Property("PendingPostCommitWorkIdsJson").HasColumnType(type);
        modelBuilder.Entity<RuntimeCheckpointCommitEntity>().Property("ConsumedSchedulerWorkItemIdsJson").HasColumnType(type);
        modelBuilder.Entity<RuntimeCheckpointCommitEntity>().Property("ContentJson").HasColumnType(type);
    }
}
