using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
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

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules (spec 171 slice 11,
// #1881); EfModuleDescriptorTests guards that the two never drift apart.
[assembly: ManifestExtension("efModules", "Workflows.Runtime")]

// The schema families this module owns (spec 180, FR-001), each at the version its skew check reads. A host's
// readability report is derived from these alone (spec 183, FR-020); EfSchemaFamilyDeclarationGuardTests fails the
// build when a family the stores check is not declared here, or is declared at another version.
[assembly: EfSchemaFamily(BookmarkStateEfModule.SchemaFamily, "Workflows.Runtime", BookmarkStateEfModule.SchemaVersion)]
[assembly: EfSchemaFamily(RuntimeActivationSlotEfModule.SchemaFamily, "Workflows.Runtime", RuntimeActivationSlotEfModule.SchemaVersion)]
[assembly: EfSchemaFamily(RuntimeActivityExecutionEfModule.SchemaFamily, "Workflows.Runtime", RuntimeActivityExecutionEfModule.SchemaVersion)]
[assembly: EfSchemaFamily(RuntimeArtifactEfModule.SchemaFamily, "Workflows.Runtime", RuntimeArtifactEfModule.SchemaVersion)]
[assembly: EfSchemaFamily(RuntimeOperationalStateEfModule.SchemaFamily, "Workflows.Runtime", RuntimeOperationalStateEfModule.SchemaVersion)]
[assembly: EfSchemaFamily(RuntimePostCommitOutboxEfModule.SchemaFamily, "Workflows.Runtime", RuntimePostCommitOutboxEfModule.SchemaVersion)]
[assembly: EfSchemaFamily(RuntimeSchedulerPoisonEfModule.SchemaFamily, "Workflows.Runtime", RuntimeSchedulerPoisonEfModule.SchemaVersion)]
[assembly: EfSchemaFamily(RuntimeTriggerBindingEfModule.SchemaFamily, "Workflows.Runtime", RuntimeTriggerBindingEfModule.SchemaVersion)]
[assembly: EfSchemaFamily(RuntimeWorkflowAlterationEfModule.SchemaFamily, "Workflows.Runtime", RuntimeWorkflowAlterationEfModule.SchemaVersion)]
[assembly: EfSchemaFamily(RuntimeWorkflowDispatchEfModule.SchemaFamily, "Workflows.Runtime", RuntimeWorkflowDispatchEfModule.SchemaVersion)]
[assembly: EfSchemaFamily(RuntimeWorkflowExecutionEfModule.SchemaFamily, "Workflows.Runtime", RuntimeWorkflowExecutionEfModule.SchemaVersion)]
[assembly: EfSchemaFamily(RuntimeWorkflowTestScopeEfModule.SchemaFamily, "Workflows.Runtime", RuntimeWorkflowTestScopeEfModule.SchemaVersion)]

// Content and integrity columns: see EXTENSION_POINTS.md, "Content and integrity columns" (spec 180, FR-008, FR-009
// and FR-014).
// BookmarkState's payload and metadata, and the checkpoint marker's and the recurring-schedule projection's id sets,
// restate parts of the row's content document, but each is deserialized and returned or compared with the upcast
// content, so each is content too (#2140).
[assembly: EfSchemaContent(BookmarkStateEfModule.SchemaFamily, typeof(BookmarkStateEntity),
    nameof(BookmarkStateEntity.ContentJson), nameof(BookmarkStateEntity.PayloadJson), nameof(BookmarkStateEntity.MetadataJson))]
[assembly: EfSchemaContent(RuntimeActivationSlotEfModule.SchemaFamily, typeof(WorkflowActivationSlotEntity), nameof(WorkflowActivationSlotEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeActivityExecutionEfModule.SchemaFamily, typeof(ActivityExecutionStateEntity), nameof(ActivityExecutionStateEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeActivityExecutionEfModule.SchemaFamily, typeof(ActivityExecutionInspectionEntity), nameof(ActivityExecutionInspectionEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeActivityExecutionEfModule.SchemaFamily, typeof(ActivityExecutionHierarchyEntity), nameof(ActivityExecutionHierarchyEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeArtifactEfModule.SchemaFamily, typeof(WorkflowExecutableEntity), nameof(WorkflowExecutableEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeArtifactEfModule.SchemaFamily, typeof(WorkflowExecutableCoordinationEntity), nameof(WorkflowExecutableCoordinationEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeArtifactEfModule.SchemaFamily, typeof(ExecutableActivityTemplateEntity), nameof(ExecutableActivityTemplateEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeArtifactEfModule.SchemaFamily, typeof(ExecutableActivityTemplateHashClaimEntity), nameof(ExecutableActivityTemplateHashClaimEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeArtifactEfModule.SchemaFamily, typeof(WorkflowExecutableSourceReferenceEntity), nameof(WorkflowExecutableSourceReferenceEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeOperationalStateEfModule.SchemaFamily, typeof(DurableValueStateEntity), nameof(DurableValueStateEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeOperationalStateEfModule.SchemaFamily, typeof(SchedulerStateEntity), nameof(SchedulerStateEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeOperationalStateEfModule.SchemaFamily, typeof(DurableTimerEntity), nameof(DurableTimerEntity.ContentJson))]
[assembly: EfSchemaContent(RuntimeOperationalStateEfModule.SchemaFamily, typeof(SchedulerWorkItemEntity), nameof(SchedulerWorkItemEntity.ContentJson))]
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
