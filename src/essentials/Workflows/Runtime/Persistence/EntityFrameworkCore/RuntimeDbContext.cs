using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>
/// Provider-neutral model for every Runtime table: bookmarks and stimulus lookup, workflow and activity
/// execution state, scheduler work, durable timers, the post-commit outbox, dispatch, alterations, test
/// scopes, trigger bindings and activation slots. One context, so one migration set and one history table
/// (<see cref="RuntimeEfModule.HistoryModuleName"/>) covers the whole Runtime module.
/// </summary>
public abstract class RuntimeDbContext(DbContextOptions options) : DbContext(options)
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
    public DbSet<WorkflowExecutionStateEntity> WorkflowExecutionStates => Set<WorkflowExecutionStateEntity>();
    public DbSet<WorkflowAlterationPlanEntity> WorkflowAlterationPlans => Set<WorkflowAlterationPlanEntity>();
    public DbSet<WorkflowAlterationJobEntity> WorkflowAlterationJobs => Set<WorkflowAlterationJobEntity>();
    public DbSet<WorkflowTestScopeEntity> WorkflowTestScopes => Set<WorkflowTestScopeEntity>();
    public DbSet<DurableValueStateEntity> DurableValueStates => Set<DurableValueStateEntity>();
    public DbSet<SchedulerStateEntity> SchedulerStates => Set<SchedulerStateEntity>();
    public DbSet<DurableTimerEntity> DurableTimers => Set<DurableTimerEntity>();
    public DbSet<SchedulerWorkItemEntity> SchedulerWorkItems => Set<SchedulerWorkItemEntity>();
    public DbSet<ExecutionLivenessStateEntity> ExecutionLivenessStates => Set<ExecutionLivenessStateEntity>();
    public DbSet<WorkflowHoldStateEntity> WorkflowHoldStates => Set<WorkflowHoldStateEntity>();
    public DbSet<IncidentStateEntity> IncidentStates => Set<IncidentStateEntity>();
    public DbSet<WorkflowRunHealthStateEntity> WorkflowRunHealthStates => Set<WorkflowRunHealthStateEntity>();
    public DbSet<RuntimeCheckpointCommitEntity> RuntimeCheckpointCommits => Set<RuntimeCheckpointCommitEntity>();
    public DbSet<RuntimePostCommitOutboxEntity> RuntimePostCommitOutbox => Set<RuntimePostCommitOutboxEntity>();
    public DbSet<WorkflowDispatchEntity> WorkflowDispatches => Set<WorkflowDispatchEntity>();
    public DbSet<WorkflowSchedulerPoisonEntity> WorkflowSchedulerPoisonRecords => Set<WorkflowSchedulerPoisonEntity>();
    public DbSet<WorkflowTriggerBindingEntity> WorkflowTriggerBindings => Set<WorkflowTriggerBindingEntity>();
    public DbSet<WorkflowTriggerBindingProjectionStateEntity> WorkflowTriggerBindingProjectionStates => Set<WorkflowTriggerBindingProjectionStateEntity>();
    public DbSet<WorkflowActivationSlotEntity> WorkflowActivationSlots => Set<WorkflowActivationSlotEntity>();
    public DbSet<RecurringTriggerScheduleEntity> RecurringTriggerSchedules => Set<RecurringTriggerScheduleEntity>();
    public DbSet<RecurringTriggerScheduleProjectionStateEntity> RecurringTriggerScheduleProjectionStates => Set<RecurringTriggerScheduleProjectionStateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The host's optional schema; nothing changes when none is configured.
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.ApplyConfiguration(new BookmarkStateEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowExecutableEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowExecutableCoordinationEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ExecutableActivityTemplateEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ExecutableActivityTemplateHashClaimEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowExecutableSourceReferenceEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ActivityExecutionStateEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ActivityExecutionInspectionEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ActivityExecutionHierarchyEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowExecutionStateEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowAlterationPlanEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowAlterationJobEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowTestScopeEntityConfiguration());
        modelBuilder.ApplyConfiguration(new DurableValueStateEntityConfiguration());
        modelBuilder.ApplyConfiguration(new SchedulerStateEntityConfiguration());
        modelBuilder.ApplyConfiguration(new DurableTimerEntityConfiguration());
        modelBuilder.ApplyConfiguration(new SchedulerWorkItemEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ExecutionLivenessStateEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowHoldStateEntityConfiguration());
        modelBuilder.ApplyConfiguration(new IncidentStateEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowRunHealthStateEntityConfiguration());
        modelBuilder.ApplyConfiguration(new RuntimeCheckpointCommitEntityConfiguration());
        modelBuilder.ApplyConfiguration(new RuntimePostCommitOutboxEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowDispatchEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowSchedulerPoisonEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowTriggerBindingEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowTriggerBindingProjectionStateEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowActivationSlotEntityConfiguration());
        modelBuilder.ApplyConfiguration(new RecurringTriggerScheduleEntityConfiguration());
        modelBuilder.ApplyConfiguration(new RecurringTriggerScheduleProjectionStateEntityConfiguration());
        ConfigureProvider(modelBuilder);
        // Installed unconditionally, so this context reads a frame whatever wrote it. Nothing here enables an
        // encoder: with no codec configured these columns are written exactly as they were before.
        modelBuilder.UseElsaPayloadColumns(
            this,
            "ContentJson",
            "PayloadJson",
            "MetadataJson",
            "CleanupSafeFailureJson");
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}
