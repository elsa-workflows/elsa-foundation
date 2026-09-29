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
// readability report is derived from these alone (spec 183, FR-020); EfSchemaFamilyChainDeclarationGuardTests fails the
// build when a family the stores check is not declared here, or is declared at another version. Each names the tables
// whose rows it stamps, so the finalization gate holds every write to its own family's write version (spec 181, FR-009).
// RuntimeArtifact names its content-addressed tables, executables and executable activity templates, whose identity is
// their content hash (ADR 0038): the post-finalization backfill never rewrites them (spec 186, FR-010a and FR-010b).
[assembly: EfSchemaFamily(BookmarkStateEfModule.SchemaFamily, "Workflows.Runtime", BookmarkStateEfModule.SchemaVersion, Entities = [typeof(BookmarkStateEntity)])]
[assembly: EfSchemaFamily(RuntimeActivationSlotEfModule.SchemaFamily, "Workflows.Runtime", RuntimeActivationSlotEfModule.SchemaVersion, Entities = [typeof(WorkflowActivationSlotEntity)])]
[assembly: EfSchemaFamily(RuntimeActivityExecutionEfModule.SchemaFamily, "Workflows.Runtime", RuntimeActivityExecutionEfModule.SchemaVersion, Entities = [typeof(ActivityExecutionStateEntity), typeof(ActivityExecutionInspectionEntity), typeof(ActivityExecutionHierarchyEntity)])]
[assembly: EfSchemaFamily(RuntimeArtifactEfModule.SchemaFamily, "Workflows.Runtime", RuntimeArtifactEfModule.SchemaVersion, Entities = [typeof(WorkflowExecutableEntity), typeof(WorkflowExecutableCoordinationEntity), typeof(ExecutableActivityTemplateEntity), typeof(ExecutableActivityTemplateHashClaimEntity), typeof(WorkflowExecutableSourceReferenceEntity)], ContentAddressed = [typeof(WorkflowExecutableEntity), typeof(ExecutableActivityTemplateEntity)])]
[assembly: EfSchemaFamily(RuntimeOperationalStateEfModule.SchemaFamily, "Workflows.Runtime", RuntimeOperationalStateEfModule.SchemaVersion, Entities = [typeof(DurableValueStateEntity), typeof(SchedulerStateEntity), typeof(DurableTimerEntity), typeof(SchedulerWorkItemEntity), typeof(ExecutionLivenessStateEntity), typeof(WorkflowHoldStateEntity), typeof(IncidentStateEntity), typeof(WorkflowRunHealthStateEntity), typeof(RuntimeCheckpointCommitEntity), typeof(RecurringTriggerScheduleEntity), typeof(RecurringTriggerScheduleProjectionStateEntity)])]
[assembly: EfSchemaFamily(RuntimePostCommitOutboxEfModule.SchemaFamily, "Workflows.Runtime", RuntimePostCommitOutboxEfModule.SchemaVersion, Entities = [typeof(RuntimePostCommitOutboxEntity)])]
[assembly: EfSchemaFamily(RuntimeSchedulerPoisonEfModule.SchemaFamily, "Workflows.Runtime", RuntimeSchedulerPoisonEfModule.SchemaVersion, Entities = [typeof(WorkflowSchedulerPoisonEntity)])]
[assembly: EfSchemaFamily(RuntimeTriggerBindingEfModule.SchemaFamily, "Workflows.Runtime", RuntimeTriggerBindingEfModule.SchemaVersion, Entities = [typeof(WorkflowTriggerBindingEntity), typeof(WorkflowTriggerBindingProjectionStateEntity)])]
[assembly: EfSchemaFamily(RuntimeWorkflowAlterationEfModule.SchemaFamily, "Workflows.Runtime", RuntimeWorkflowAlterationEfModule.SchemaVersion, Entities = [typeof(WorkflowAlterationPlanEntity), typeof(WorkflowAlterationJobEntity)])]
[assembly: EfSchemaFamily(RuntimeWorkflowDispatchEfModule.SchemaFamily, "Workflows.Runtime", RuntimeWorkflowDispatchEfModule.SchemaVersion, Entities = [typeof(WorkflowDispatchEntity)])]
[assembly: EfSchemaFamily(RuntimeWorkflowExecutionEfModule.SchemaFamily, "Workflows.Runtime", RuntimeWorkflowExecutionEfModule.SchemaVersion, Entities = [typeof(WorkflowExecutionStateEntity)])]
[assembly: EfSchemaFamily(RuntimeWorkflowTestScopeEfModule.SchemaFamily, "Workflows.Runtime", RuntimeWorkflowTestScopeEfModule.SchemaVersion, Entities = [typeof(WorkflowTestScopeEntity)])]

// Content and integrity columns: see src/essentials/Persistence/EntityFramework/EXTENSION_POINTS.md, "Content and integrity columns" (spec 180, FR-008, FR-009
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
