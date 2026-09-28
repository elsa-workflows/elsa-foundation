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
// build when a family the stores check is not declared here, or is declared at another version. Each names the tables
// whose rows it stamps, so the finalization gate holds every write to its own family's write version (spec 181, FR-009).
[assembly: EfSchemaFamily(BookmarkStateEfModule.SchemaFamily, "Workflows.Runtime", BookmarkStateEfModule.SchemaVersion, Entities = [typeof(BookmarkStateEntity)])]
[assembly: EfSchemaFamily(RuntimeActivationSlotEfModule.SchemaFamily, "Workflows.Runtime", RuntimeActivationSlotEfModule.SchemaVersion, Entities = [typeof(WorkflowActivationSlotEntity)])]
[assembly: EfSchemaFamily(RuntimeActivityExecutionEfModule.SchemaFamily, "Workflows.Runtime", RuntimeActivityExecutionEfModule.SchemaVersion, Entities = [typeof(ActivityExecutionStateEntity), typeof(ActivityExecutionInspectionEntity), typeof(ActivityExecutionHierarchyEntity)])]
[assembly: EfSchemaFamily(RuntimeArtifactEfModule.SchemaFamily, "Workflows.Runtime", RuntimeArtifactEfModule.SchemaVersion, Entities = [typeof(WorkflowExecutableEntity), typeof(WorkflowExecutableCoordinationEntity), typeof(ExecutableActivityTemplateEntity), typeof(ExecutableActivityTemplateHashClaimEntity), typeof(WorkflowExecutableSourceReferenceEntity)])]
[assembly: EfSchemaFamily(RuntimeOperationalStateEfModule.SchemaFamily, "Workflows.Runtime", RuntimeOperationalStateEfModule.SchemaVersion, Entities = [typeof(DurableValueStateEntity), typeof(SchedulerStateEntity), typeof(DurableTimerEntity), typeof(SchedulerWorkItemEntity), typeof(ExecutionLivenessStateEntity), typeof(WorkflowHoldStateEntity), typeof(IncidentStateEntity), typeof(WorkflowRunHealthStateEntity), typeof(RuntimeCheckpointCommitEntity), typeof(RecurringTriggerScheduleEntity), typeof(RecurringTriggerScheduleProjectionStateEntity)])]
[assembly: EfSchemaFamily(RuntimePostCommitOutboxEfModule.SchemaFamily, "Workflows.Runtime", RuntimePostCommitOutboxEfModule.SchemaVersion, Entities = [typeof(RuntimePostCommitOutboxEntity)])]
[assembly: EfSchemaFamily(RuntimeSchedulerPoisonEfModule.SchemaFamily, "Workflows.Runtime", RuntimeSchedulerPoisonEfModule.SchemaVersion, Entities = [typeof(WorkflowSchedulerPoisonEntity)])]
[assembly: EfSchemaFamily(RuntimeTriggerBindingEfModule.SchemaFamily, "Workflows.Runtime", RuntimeTriggerBindingEfModule.SchemaVersion, Entities = [typeof(WorkflowTriggerBindingEntity), typeof(WorkflowTriggerBindingProjectionStateEntity)])]
[assembly: EfSchemaFamily(RuntimeWorkflowAlterationEfModule.SchemaFamily, "Workflows.Runtime", RuntimeWorkflowAlterationEfModule.SchemaVersion, Entities = [typeof(WorkflowAlterationPlanEntity), typeof(WorkflowAlterationJobEntity)])]
[assembly: EfSchemaFamily(RuntimeWorkflowDispatchEfModule.SchemaFamily, "Workflows.Runtime", RuntimeWorkflowDispatchEfModule.SchemaVersion, Entities = [typeof(WorkflowDispatchEntity)])]
[assembly: EfSchemaFamily(RuntimeWorkflowExecutionEfModule.SchemaFamily, "Workflows.Runtime", RuntimeWorkflowExecutionEfModule.SchemaVersion, Entities = [typeof(WorkflowExecutionStateEntity)])]
[assembly: EfSchemaFamily(RuntimeWorkflowTestScopeEfModule.SchemaFamily, "Workflows.Runtime", RuntimeWorkflowTestScopeEfModule.SchemaVersion, Entities = [typeof(WorkflowTestScopeEntity)])]
