using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

public sealed class RuntimeSqliteDbContext(DbContextOptions<RuntimeSqliteDbContext> options) : RuntimeDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        RuntimeProviderModel.ConfigureText(modelBuilder, "TEXT");
        RuntimeProviderModel.ConfigureRouteConvergenceIndexes(modelBuilder, includeAnnotation: null, coverBindings: true);
    }
}

public sealed class RuntimeSqlServerDbContext(DbContextOptions<RuntimeSqlServerDbContext> options) : RuntimeDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        RuntimeProviderModel.ConfigureText(modelBuilder, "nvarchar(max)");
        RuntimeProviderModel.ConfigureRouteConvergenceIndexes(modelBuilder, includeAnnotation: "SqlServer:Include", coverBindings: true);
    }
}

public sealed class RuntimePostgreSqlDbContext(DbContextOptions<RuntimePostgreSqlDbContext> options) : RuntimeDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        RuntimeProviderModel.ConfigureText(modelBuilder, "text");
        RuntimeProviderModel.ConfigureRouteConvergenceIndexes(modelBuilder, includeAnnotation: "Npgsql:IndexInclude", coverBindings: true);
    }
}

public sealed class RuntimeMySqlDbContext(DbContextOptions<RuntimeMySqlDbContext> options) : RuntimeDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.MySql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        RuntimeProviderModel.ConfigureText(modelBuilder, "longtext");
        // No binding index: the binding identity columns are declared at 344 characters, and scope hash, type lookup
        // key, activity, lookup key and hash come to more than InnoDB's 3072-byte key limit in utf8mb4, while MySQL has
        // no INCLUDE. Its binding projection keeps one row lookup per active binding of the type, a publish-time count.
        RuntimeProviderModel.ConfigureRouteConvergenceIndexes(modelBuilder, includeAnnotation: null, coverBindings: false);
    }
}

file static class RuntimeProviderModel
{
    private static readonly string[] RouteConvergenceProjection = ["StimulusLookupKey", "StimulusHash"];

    /// <summary>
    /// The covering indexes of the HTTP route-table convergence check (#2190), which every node runs on an interval:
    /// both of its projections are answered from an index alone, so a check reads index entries, never rows. A provider
    /// with an INCLUDE annotation carries the projected columns there; elsewhere they join the key.
    /// </summary>
    public static void ConfigureRouteConvergenceIndexes(ModelBuilder modelBuilder, string? includeAnnotation, bool coverBindings)
    {
        Covering(
            modelBuilder.Entity<BookmarkStateEntity>(),
            [nameof(BookmarkStateEntity.ScopeKeyHash), nameof(BookmarkStateEntity.StimulusTypeLookupKey), nameof(BookmarkStateEntity.ExpiresAtUtcTicks)],
            BookmarkStateEfModule.RouteConvergenceIndexName,
            includeAnnotation);
        if (coverBindings)
            Covering(
                modelBuilder.Entity<WorkflowTriggerBindingEntity>(),
                [nameof(WorkflowTriggerBindingEntity.ScopeKeyHash), nameof(WorkflowTriggerBindingEntity.StimulusTypeLookupKey), nameof(WorkflowTriggerBindingEntity.IsActive)],
                RuntimeTriggerBindingEfModule.RouteConvergenceIndexName,
                includeAnnotation);
    }

    private static void Covering<TEntity>(EntityTypeBuilder<TEntity> entity, string[] keys, string name, string? includeAnnotation)
        where TEntity : class
    {
        var index = includeAnnotation is null
            ? entity.HasIndex([.. keys, .. RouteConvergenceProjection])
            : entity.HasIndex(keys).HasAnnotation(includeAnnotation, RouteConvergenceProjection);
        index.HasDatabaseName(name);
    }

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
        foreach (var entity in new[] { typeof(DurableTimerEntity) })
        {
            modelBuilder.Entity(entity).Property("ContentJson").HasColumnType(type);
            modelBuilder.Entity(entity).Property("ScopeKey").HasColumnType(type);
            modelBuilder.Entity(entity).Property("StimulusType").HasColumnType(type);
            modelBuilder.Entity(entity).Property("StimulusHash").HasColumnType(type);
            modelBuilder.Entity(entity).Property("ClaimOwnerId").HasColumnType(type);
        }
        modelBuilder.Entity<SchedulerWorkItemEntity>().Property("ContentJson").HasColumnType(type);
        modelBuilder.Entity<SchedulerWorkItemEntity>().Property("ScopeKey").HasColumnType(type);
        modelBuilder.Entity<SchedulerWorkItemEntity>().Property("WorkItemId").HasColumnType(type);
        modelBuilder.Entity<SchedulerWorkItemEntity>().Property("ClaimOwnerId").HasColumnType(type);
        foreach (var entity in new[] { typeof(ExecutionLivenessStateEntity), typeof(WorkflowHoldStateEntity) })
        {
            modelBuilder.Entity(entity).Property("ContentJson").HasColumnType(type);
            modelBuilder.Entity(entity).Property("ScopeKey").HasColumnType(type);
        }
        modelBuilder.Entity<IncidentStateEntity>().Property("ContentJson").HasColumnType(type);
        modelBuilder.Entity<IncidentStateEntity>().Property("ScopeKey").HasColumnType(type);
        modelBuilder.Entity<WorkflowRunHealthStateEntity>().Property("ContentJson").HasColumnType(type);
        modelBuilder.Entity<WorkflowRunHealthStateEntity>().Property("ScopeKey").HasColumnType(type);
        modelBuilder.Entity<RuntimeCheckpointCommitEntity>().Property("PendingPostCommitWorkIdsJson").HasColumnType(type);
        modelBuilder.Entity<RuntimeCheckpointCommitEntity>().Property("ConsumedSchedulerWorkItemIdsJson").HasColumnType(type);
        modelBuilder.Entity<RuntimeCheckpointCommitEntity>().Property("ContentJson").HasColumnType(type);
        modelBuilder.Entity<RuntimePostCommitOutboxEntity>().Property("ScopeKey").HasColumnType(type);
        modelBuilder.Entity<RuntimePostCommitOutboxEntity>().Property("OutboxItemId").HasColumnType(type);
        modelBuilder.Entity<RuntimePostCommitOutboxEntity>().Property("WorkflowExecutionId").HasColumnType(type);
        modelBuilder.Entity<RuntimePostCommitOutboxEntity>().Property("ContentJson").HasColumnType(type);
        modelBuilder.Entity<WorkflowDispatchEntity>().Property("ScopeKey").HasColumnType(type);
        modelBuilder.Entity<WorkflowDispatchEntity>().Property("DispatchId").HasColumnType(type);
        modelBuilder.Entity<WorkflowDispatchEntity>().Property("ParentWorkflowExecutionId").HasColumnType(type);
        modelBuilder.Entity<WorkflowDispatchEntity>().Property("ParentActivityExecutionId").HasColumnType(type);
        modelBuilder.Entity<WorkflowDispatchEntity>().Property("ChildWorkflowExecutionId").HasColumnType(type);
        modelBuilder.Entity<WorkflowDispatchEntity>().Property("ChildArtifactId").HasColumnType(type);
        modelBuilder.Entity<WorkflowDispatchEntity>().Property("TestScopeId").HasColumnType(type);
        modelBuilder.Entity<WorkflowDispatchEntity>().Property("TenantId").HasColumnType(type);
        modelBuilder.Entity<WorkflowDispatchEntity>().Property("ContentJson").HasColumnType(type);
        modelBuilder.Entity<WorkflowSchedulerPoisonEntity>().Property("ScopeKey").HasColumnType(type);
        modelBuilder.Entity<WorkflowSchedulerPoisonEntity>().Property("ContentJson").HasColumnType(type);
        foreach (var entity in new[] { typeof(WorkflowTriggerBindingEntity), typeof(WorkflowTriggerBindingProjectionStateEntity) })
        {
            modelBuilder.Entity(entity).Property("ScopeKey").HasColumnType(type);
            modelBuilder.Entity(entity).Property("ContentJson").HasColumnType(type);
            if (entity == typeof(WorkflowTriggerBindingEntity))
            {
                modelBuilder.Entity(entity).Property("ContentJson").HasColumnType(type);
                modelBuilder.Entity(entity).Property("CorrelationScope").HasColumnType(type);
                modelBuilder.Entity(entity).Property("ActivationId").HasColumnType(type);
                modelBuilder.Entity(entity).Property("SlotId").HasColumnType(type);
            }
        }
        modelBuilder.Entity<WorkflowActivationSlotEntity>().Property("ScopeKey").HasColumnType(type);
        modelBuilder.Entity<WorkflowActivationSlotEntity>().Property("ContentJson").HasColumnType(type);
        modelBuilder.Entity<WorkflowActivationSlotEntity>().Property("SourceKind").HasColumnType(type);
        modelBuilder.Entity<WorkflowActivationSlotEntity>().Property("SourceId").HasColumnType(type);
        modelBuilder.Entity<WorkflowActivationSlotEntity>().Property("ActiveActivationId").HasColumnType(type);
        foreach (var entity in new[] { typeof(RecurringTriggerScheduleEntity), typeof(RecurringTriggerScheduleProjectionStateEntity) })
        {
            modelBuilder.Entity(entity).Property("ScopeKey").HasColumnType(type);
            modelBuilder.Entity(entity).Property("ContentJson").HasColumnType(type);
            if (entity == typeof(RecurringTriggerScheduleEntity))
            {
                modelBuilder.Entity(entity).Property("ExecutableNodeId").HasColumnType(type);
                modelBuilder.Entity(entity).Property("StimulusType").HasColumnType(type);
                modelBuilder.Entity(entity).Property("StimulusHash").HasColumnType(type);
                modelBuilder.Entity(entity).Property("Expression").HasColumnType(type);
                modelBuilder.Entity(entity).Property("ActivationId").HasColumnType(type);
                modelBuilder.Entity(entity).Property("SlotId").HasColumnType(type);
            }
            else
            {
                modelBuilder.Entity(entity).Property("ScheduleIdsJson").HasColumnType(type);
                modelBuilder.Entity(entity).Property("ScheduleFingerprintsJson").HasColumnType(type);
                modelBuilder.Entity(entity).Property("ArtifactId").HasColumnType(type);
            }
        }
    }
}
