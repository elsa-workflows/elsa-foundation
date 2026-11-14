using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

// The single, discoverable declaration of this module. EfModuleCatalog.Discover reads this,
// and EfModuleBinding.For derives the registration class's binding from it (#1872).
[assembly: EfModule(
    "Workflows.Runtime",
    typeof(RuntimeDbContext),
    HistoryModule = RuntimeEfModule.HistoryModuleName,
    Sqlite = typeof(RuntimeSqliteDbContext),
    SqlServer = typeof(RuntimeSqlServerDbContext),
    PostgreSql = typeof(RuntimePostgreSqlDbContext),
    MySql = typeof(RuntimeMySqlDbContext),
    DisplayName = "Runtime")]

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules (#1881);
// EfModuleDescriptorTests guards that the two never drift apart.
[assembly: ManifestExtension("efModules", "Workflows.Runtime")]

// The schema families this module owns, each at the version its skew check reads. A host's
// readability report is derived from these alone; every family the stores check must be declared
// here, at the version they read; that holds by review until #2606 restores a compiled check. Each names the tables
// whose rows it stamps, so the finalization gate holds every write to its own family's write version.
// RuntimeArtifact names its content-addressed tables, each marked [EfSchemaContentAddressed] with its reason:
// executables and executable activity templates, whose identity is their content hash, and the template hash claims,
// keyed by that hash, which bind it to one template. The post-finalization backfill never rewrites them.
[assembly: EfSchemaFamily(BookmarkStateEfModule.SchemaFamily, "Workflows.Runtime", BookmarkStateEfModule.SchemaVersion, Entities = [typeof(BookmarkStateEntity)])]
[assembly: EfSchemaFamily(RuntimeActivationSlotEfModule.SchemaFamily, "Workflows.Runtime", RuntimeActivationSlotEfModule.SchemaVersion, Entities = [typeof(WorkflowActivationSlotEntity)])]
[assembly: EfSchemaFamily(RuntimeActivityExecutionEfModule.SchemaFamily, "Workflows.Runtime", RuntimeActivityExecutionEfModule.SchemaVersion, Entities = [typeof(ActivityExecutionStateEntity), typeof(ActivityExecutionInspectionEntity), typeof(ActivityExecutionHierarchyEntity)])]
[assembly: EfSchemaFamily(RuntimeArtifactEfModule.SchemaFamily, "Workflows.Runtime", RuntimeArtifactEfModule.ScopedPublicationSchemaVersion, Entities = [typeof(WorkflowExecutableEntity), typeof(WorkflowExecutableCoordinationEntity), typeof(WorkflowExecutableRootWriteLeaseEntity), typeof(ExecutableActivityTemplateEntity), typeof(ExecutableActivityTemplateHashClaimEntity), typeof(WorkflowExecutableSourceReferenceEntity)], ContentAddressed = [typeof(WorkflowExecutableEntity), typeof(ExecutableActivityTemplateEntity), typeof(ExecutableActivityTemplateHashClaimEntity)], Upcasters = [typeof(RuntimeScopedPublicationV1Upcaster)])]
[assembly: EfSchemaFamily(RuntimeOperationalStateEfModule.SchemaFamily, "Workflows.Runtime", RuntimeOperationalStateEfModule.StandaloneScheduleSchemaVersion, Entities = [typeof(DurableValueStateEntity), typeof(SchedulerStateEntity), typeof(DurableTimerEntity), typeof(ExecutionLivenessStateEntity), typeof(WorkflowHoldStateEntity), typeof(IncidentStateEntity), typeof(WorkflowRunHealthStateEntity), typeof(RuntimeCheckpointCommitEntity), typeof(RecurringTriggerScheduleEntity), typeof(RecurringTriggerScheduleProjectionStateEntity)], Upcasters = [typeof(RuntimeStandaloneScheduleV1Upcaster)])]
[assembly: EfSchemaFamily(RuntimeBackgroundActivityWorkEfModule.SchemaFamily, "Workflows.Runtime", RuntimeBackgroundActivityWorkEfModule.SchemaVersion, Entities = [typeof(BackgroundActivityWorkEntity)])]
[assembly: EfSchemaFamily(RuntimePostCommitOutboxEfModule.SchemaFamily, "Workflows.Runtime", RuntimePostCommitOutboxEfModule.SchemaVersion, Entities = [typeof(RuntimePostCommitOutboxEntity)], Upcasters = [typeof(RuntimePostCommitOutboxV1Upcaster), typeof(RuntimePostCommitOutboxV2Upcaster)], Rewriter = typeof(Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.RuntimePostCommitOutboxRowRewriter))]
[assembly: EfSchemaFamily(RuntimeSchedulerWorkEfModule.SchemaFamily, "Workflows.Runtime", RuntimeSchedulerWorkEfModule.IngressSchemaVersion, Entities = [typeof(SchedulerWorkItemEntity)], Upcasters = [typeof(RuntimeSchedulerWorkV1Upcaster), typeof(RuntimeSchedulerWorkV2Upcaster), typeof(RuntimeSchedulerIngressV3Upcaster)])]
[assembly: EfSchemaFamily(RuntimeSchedulerPoisonEfModule.SchemaFamily, "Workflows.Runtime", RuntimeSchedulerPoisonEfModule.SchemaVersion, Entities = [typeof(WorkflowSchedulerPoisonEntity)])]
[assembly: EfSchemaFamily(RuntimeTriggerBindingEfModule.SchemaFamily, "Workflows.Runtime", RuntimeTriggerBindingEfModule.SchemaVersion, Entities = [typeof(WorkflowTriggerBindingEntity), typeof(WorkflowTriggerBindingProjectionStateEntity)])]
[assembly: EfSchemaFamily(RuntimeWorkflowAlterationEfModule.SchemaFamily, "Workflows.Runtime", RuntimeWorkflowAlterationEfModule.SchemaVersion, Entities = [typeof(WorkflowAlterationPlanEntity), typeof(WorkflowAlterationJobEntity)])]
[assembly: EfSchemaFamily(RuntimeWorkflowDispatchEfModule.SchemaFamily, "Workflows.Runtime", RuntimeWorkflowDispatchEfModule.SchemaVersion, Entities = [typeof(WorkflowDispatchEntity)])]
[assembly: EfSchemaFamily(RuntimeWorkflowExecutionEfModule.SchemaFamily, "Workflows.Runtime", RuntimeWorkflowExecutionEfModule.IngressSchemaVersion, Entities = [typeof(WorkflowExecutionStateEntity)], Upcasters = [typeof(RuntimeWorkflowExecutionV1Upcaster), typeof(RuntimeWorkflowIngressV2Upcaster)])]
[assembly: EfSchemaFamily(RuntimeModelStateEfModule.SchemaFamily, "Workflows.Runtime", RuntimeModelStateEfModule.IngressSchemaVersion, Entities = [typeof(RuntimeModelExecutionEntity), typeof(RuntimeModelInputEntity)], Upcasters = [typeof(RuntimeModelIngressV1Upcaster)])]
[assembly: EfSchemaContent(RuntimeModelStateEfModule.SchemaFamily, typeof(RuntimeModelExecutionEntity), nameof(RuntimeModelExecutionEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeModelStateEfModule.SchemaFamily, typeof(RuntimeModelInputEntity), nameof(RuntimeModelInputEntity.ContentJson))]
[assembly: EfSchemaFamily(RuntimeWorkflowTestScopeEfModule.SchemaFamily, "Workflows.Runtime", RuntimeWorkflowTestScopeEfModule.SchemaVersion, Entities = [typeof(WorkflowTestScopeEntity)])]

// Content and integrity columns: see Essentials/src/Persistence/EntityFramework/EXTENSION_POINTS.md, "Content and integrity columns".
// BookmarkState's payload and metadata, and the checkpoint marker's and the recurring-schedule projection's id sets,
// restate parts of the row's content document, but each is deserialized and returned or compared with the upcast
// content, so each is content too (#2140).
[assembly: EfSchemaContent(BookmarkStateEfModule.SchemaFamily, typeof(BookmarkStateEntity),
    nameof(BookmarkStateEntity.ContentJson), nameof(BookmarkStateEntity.PayloadJson), nameof(BookmarkStateEntity.MetadataJson))]
[assembly: EfSchemaContent(RuntimeActivationSlotEfModule.SchemaFamily, typeof(WorkflowActivationSlotEntity), nameof(WorkflowActivationSlotEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeActivityExecutionEfModule.SchemaFamily, typeof(ActivityExecutionStateEntity), nameof(ActivityExecutionStateEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeActivityExecutionEfModule.SchemaFamily, typeof(ActivityExecutionInspectionEntity), nameof(ActivityExecutionInspectionEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeActivityExecutionEfModule.SchemaFamily, typeof(ActivityExecutionHierarchyEntity), nameof(ActivityExecutionHierarchyEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeBackgroundActivityWorkEfModule.SchemaFamily, typeof(BackgroundActivityWorkEntity), nameof(BackgroundActivityWorkEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeArtifactEfModule.SchemaFamily, typeof(WorkflowExecutableEntity), nameof(WorkflowExecutableEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeArtifactEfModule.SchemaFamily, typeof(WorkflowExecutableCoordinationEntity), nameof(WorkflowExecutableCoordinationEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeArtifactEfModule.SchemaFamily, typeof(ExecutableActivityTemplateEntity), nameof(ExecutableActivityTemplateEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeArtifactEfModule.SchemaFamily, typeof(ExecutableActivityTemplateHashClaimEntity), nameof(ExecutableActivityTemplateHashClaimEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeArtifactEfModule.SchemaFamily, typeof(WorkflowExecutableSourceReferenceEntity), nameof(WorkflowExecutableSourceReferenceEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeOperationalStateEfModule.SchemaFamily, typeof(DurableValueStateEntity), nameof(DurableValueStateEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeOperationalStateEfModule.SchemaFamily, typeof(SchedulerStateEntity), nameof(SchedulerStateEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeOperationalStateEfModule.SchemaFamily, typeof(DurableTimerEntity), nameof(DurableTimerEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeSchedulerWorkEfModule.SchemaFamily, typeof(SchedulerWorkItemEntity), nameof(SchedulerWorkItemEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeOperationalStateEfModule.SchemaFamily, typeof(ExecutionLivenessStateEntity), nameof(ExecutionLivenessStateEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeOperationalStateEfModule.SchemaFamily, typeof(WorkflowHoldStateEntity), nameof(WorkflowHoldStateEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeOperationalStateEfModule.SchemaFamily, typeof(IncidentStateEntity), nameof(IncidentStateEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeOperationalStateEfModule.SchemaFamily, typeof(WorkflowRunHealthStateEntity), nameof(WorkflowRunHealthStateEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeOperationalStateEfModule.SchemaFamily, typeof(RuntimeCheckpointCommitEntity),
    nameof(RuntimeCheckpointCommitEntity.ContentJson), nameof(RuntimeCheckpointCommitEntity.PendingPostCommitWorkIdsJson),
    nameof(RuntimeCheckpointCommitEntity.ConsumedSchedulerWorkItemIdsJson))]
[assembly: EfSchemaContent(RuntimeOperationalStateEfModule.SchemaFamily, typeof(RecurringTriggerScheduleEntity), nameof(RecurringTriggerScheduleEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeOperationalStateEfModule.SchemaFamily, typeof(RecurringTriggerScheduleProjectionStateEntity),
    nameof(RecurringTriggerScheduleProjectionStateEntity.ContentJson), nameof(RecurringTriggerScheduleProjectionStateEntity.ScheduleIdsJson),
    nameof(RecurringTriggerScheduleProjectionStateEntity.ScheduleFingerprintsJson))]
[assembly: EfSchemaContent(RuntimePostCommitOutboxEfModule.SchemaFamily, typeof(RuntimePostCommitOutboxEntity), nameof(RuntimePostCommitOutboxEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeSchedulerPoisonEfModule.SchemaFamily, typeof(WorkflowSchedulerPoisonEntity), nameof(WorkflowSchedulerPoisonEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeTriggerBindingEfModule.SchemaFamily, typeof(WorkflowTriggerBindingEntity), nameof(WorkflowTriggerBindingEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeTriggerBindingEfModule.SchemaFamily, typeof(WorkflowTriggerBindingProjectionStateEntity), nameof(WorkflowTriggerBindingProjectionStateEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeWorkflowAlterationEfModule.SchemaFamily, typeof(WorkflowAlterationPlanEntity),
    nameof(WorkflowAlterationPlanEntity.ContentJson), nameof(WorkflowAlterationPlanEntity.CleanupSafeFailureJson))]
[assembly: EfSchemaContent(RuntimeWorkflowAlterationEfModule.SchemaFamily, typeof(WorkflowAlterationJobEntity), nameof(WorkflowAlterationJobEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeWorkflowDispatchEfModule.SchemaFamily, typeof(WorkflowDispatchEntity), nameof(WorkflowDispatchEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeWorkflowExecutionEfModule.SchemaFamily, typeof(WorkflowExecutionStateEntity), nameof(WorkflowExecutionStateEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeWorkflowTestScopeEfModule.SchemaFamily, typeof(WorkflowTestScopeEntity), nameof(WorkflowTestScopeEntity.ContentJson))]

[assembly: EfSchemaFamily(UserTasksEfModule.SchemaFamily, "Workflows.Runtime", UserTasksEfModule.SchemaVersion, Entities = [typeof(UserTaskEntity), typeof(UserTaskFormEntity)])]
[assembly: EfSchemaContent(UserTasksEfModule.SchemaFamily, typeof(UserTaskEntity), nameof(UserTaskEntity.ContentJson))]
[assembly: EfSchemaContent(UserTasksEfModule.SchemaFamily, typeof(UserTaskFormEntity), nameof(UserTaskFormEntity.ContentJson))]

[assembly: EfSchemaFamily(StandaloneTriggersEfModule.SchemaFamily, "Workflows.Runtime", StandaloneTriggersEfModule.SchemaVersion,
    Entities = [typeof(StandaloneTriggerEntity), typeof(TriggerSubscriptionEntity), typeof(TriggerMutationEntity), typeof(RuntimeIngressReservationEntity)])]
[assembly: EfSchemaContent(StandaloneTriggersEfModule.SchemaFamily, typeof(StandaloneTriggerEntity), nameof(StandaloneTriggerEntity.ContentJson))]
[assembly: EfSchemaContent(StandaloneTriggersEfModule.SchemaFamily, typeof(TriggerSubscriptionEntity), nameof(TriggerSubscriptionEntity.ContentJson))]
[assembly: EfSchemaContent(StandaloneTriggersEfModule.SchemaFamily, typeof(TriggerMutationEntity), nameof(TriggerMutationEntity.ContentJson))]
[assembly: EfSchemaContent(StandaloneTriggersEfModule.SchemaFamily, typeof(RuntimeIngressReservationEntity), nameof(RuntimeIngressReservationEntity.ContentJson))]
