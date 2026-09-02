using Groundwork.Kernel;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>
/// Fresh-catalog Groundwork v2 declarations for Elsa's shared workflow runtime.
/// </summary>
/// <remarks>
/// These are ordinary v2 rows. The runtime keeps its existing logical unit identities, projected
/// lookup fields, optimistic version token, and tenant boundary, but does not carry the v1 document
/// physicalizer or bounded-query declarations into the new catalog. Query callers use the public
/// Groundwork AST against the indexes declared here.
/// </remarks>
public static class ElsaRuntimeV2StorageManifest
{
    public const string SchemaVersion = "1.0.0";
    public const int StorageSchemaVersion = 1;
    public const int IdMaximumLength = 450;
    public const int SchemaVersionMaximumLength = 32;
    public const int RuntimeStatusProjectionLength = 32;
    public const int RuntimeExecutionIdProjectionLength = 128;
    public const int RuntimeTenantProjectionLength = 256;
    public const int RuntimeCollectionProjectionLength = 128;
    public const int DurableTimerClaimOrderKeyProjectionLength = 84;
    public const int SchedulerWorkOrderKeyProjectionLength = 170;
    public const int BookmarkStimulusLookupKeyProjectionLength = 64;
    public const int WorkflowDispatchIdProjectionLength = 76;
    public const int WorkflowExecutionHistoryAuthorityPartitionProjectionLength = 64;
    public const int WorkflowAlterationPlanIdempotencyKeyHashProjectionLength = 64;
    public const int WorkflowAlterationPlanTenantIdempotencyKeyProjectionLength =
        RuntimeTenantProjectionLength + 1 + WorkflowAlterationPlanIdempotencyKeyHashProjectionLength;
    public const int WorkflowAlterationPlanActiveOrderKeyProjectionLength =
        19 + 1 + RuntimeExecutionIdProjectionLength;
    public const int PostCommitOutboxItemIdProjectionLength = 256;
    public const int PostCommitOutboxIntentKindProjectionLength = 230;
    public const int StimulusHashProjectionLength = IdMaximumLength;
    public const int StimulusTypeProjectionLength = 256;
    public const int WorkflowTriggerBindingStimulusTypeProjectionLength = 240;
    public const int SchedulerPoisonWorkItemProjectionLength = RuntimeExecutionIdProjectionLength;

    public const string IdField = "id";
    public const string SchemaVersionField = "schemaVersion";
    public const string ContentField = "content";
    public const string VersionField = "version";

    public const string BookmarkStateDocumentKind = "bookmarkState";
    public const string WorkflowExecutableDocumentKind = "workflowExecutable";
    public const string WorkflowExecutableCoordinationDocumentKind = "workflowExecutableCoordination";
    public const string ExecutableActivityTemplateDocumentKind = "executableActivityTemplate";
    public const string ExecutableActivityTemplateHashClaimDocumentKind = "executableActivityTemplateHashClaim";
    public const string WorkflowExecutableSourceReferenceDocumentKind = "workflowExecutableSourceReference";
    public const string ActivityExecutionStateDocumentKind = "activityExecutionState";
    public const string ActivityExecutionInspectionDocumentKind = "activityExecutionInspection";
    public const string ActivityExecutionHierarchyDocumentKind = "activityExecutionHierarchy";
    public const string WorkflowExecutionStateDocumentKind = "workflowExecutionState";
    public const string WorkflowAlterationPlanDocumentKind = "workflowAlterationPlan";
    public const string WorkflowAlterationJobDocumentKind = "workflowAlterationJob";
    public const string WorkflowTestScopeDocumentKind = "workflowTestScope";
    public const string DurableValueStateDocumentKind = "durableValueState";
    public const string SchedulerStateDocumentKind = "schedulerState";
    public const string ExecutionLivenessStateDocumentKind = "operationalState";
    public const string WorkflowHoldStateDocumentKind = "controlPlaneState";
    public const string IncidentStateDocumentKind = "incidentState";
    public const string CheckpointCommitDocumentKind = "checkpointCommit";
    public const string PostCommitOutboxDocumentKind = "postCommitOutbox";
    public const string WorkflowDispatchDocumentKind = "workflowDispatch";
    public const string SchedulerWorkItemDocumentKind = "schedulerWorkItem";
    public const string SchedulerPoisonDocumentKind = "schedulerPoison";
    public const string DurableTimerDocumentKind = "durableTimer";
    public const string WorkflowRunHealthStateDocumentKind = "workflowRunHealthState";
    public const string WorkflowTriggerBindingDocumentKind = "workflowTriggerBinding";
    public const string RecurringTriggerScheduleDocumentKind = "recurringTriggerSchedule";
    public const string PublicationProjectionStateDocumentKind = "publicationProjectionState";
    public const string WorkflowActivationSlotDocumentKind = "workflowActivationSlot";

    public const string WorkflowExecutionIdField = "workflowExecutionId";
    public const string CollectionField = "collection";
    public const string StimulusHashField = "stimulusHash";
    public const string StimulusTypeField = "stimulusType";
    public const string ArtifactIdField = "artifactId";
    public const string TemplateHashField = "templateHash";
    public const string ExecutionScopeIdField = "executionScopeId";
    public const string ActivationIdField = "activationId";
    public const string ParentActivityExecutionIdField = "parentActivityExecutionId";
    public const string ParentWorkflowExecutionIdField = "parentWorkflowExecutionId";
    public const string ChildWorkflowExecutionIdField = "childWorkflowExecutionId";
    public const string StatusField = "status";
    public const string TestScopeIdField = "testScopeId";
    public const string ScopeIdField = "scopeId";
    public const string ScopeField = "scope";
    public const string ExpiresAtField = "expiresAt";
    public const string IsRetiredField = "isRetired";
    public const string StateField = "state";
    public const string IncidentIdField = "incidentId";
    public const string CreatedAtField = "createdAt";
    public const string WorkflowAlterationPlanIdField = "planId";
    public const string WorkflowAlterationPlanTenantPartitionField = "tenantPartition";
    public const string WorkflowAlterationPlanIdempotencyKeyHashField = "idempotencyKeyHash";
    public const string WorkflowAlterationPlanTenantIdempotencyKeyField = "tenantIdempotencyKey";
    public const string WorkflowAlterationPlanStatusField = "status";
    public const string WorkflowAlterationPlanActiveOrderKeyField = "activeOrderKey";
    public const string WorkflowAlterationJobIdField = "jobId";
    public const string WorkflowAlterationJobPlanIdField = "planId";
    public const string WorkflowAlterationJobCaptureOrdinalField = "captureOrdinal";
    public const string WorkflowAlterationJobClaimableAtField = "claimableAt";
    public const string WorkflowAlterationJobStatusField = "status";
    public const string WorkflowAlterationJobCheckpointCommitIdField = "checkpointCommitId";
    public const string WorkflowExecutableSourceReferenceIdField = "sourceReferenceId";
    public const string WorkflowExecutableSourceReferenceDefinitionIdField = "definitionId";
    public const string WorkflowExecutableSourceReferenceDefinitionVersionIdField = "definitionVersionId";
    public const string DurableValueIdField = "durableValueId";
    public const string ExecutionLivenessOperationalStateIdField = "operationalStateId";
    public const string ActivityExecutionIdField = "activityExecutionId";
    public const string DurableTimerDueTimeField = "timerDueTime";
    public const string DurableTimerIdField = "timerId";
    public const string DurableTimerClaimOrderKeyField = "claimOrderKey";
    public const string WorkflowRunHealthDefinitionIdField = "definitionId";
    public const string WorkflowRunHealthRunKindField = "runKind";
    public const string WorkflowRunHealthStartedAtField = "startedAt";
    public const string WorkflowRunHealthBucketField = "bucket";
    public const string WorkflowRunHealthStatusField = StatusField;
    public const string WorkflowRunHealthIncidentCountField = "incidentCount";
    public const string WorkflowRunHealthIncidentBearingCountField = "incidentBearingCount";
    public const string TriggerBindingIdField = "triggerBindingId";
    public const string WorkflowTriggerBindingIsActiveField = "isActive";
    public const string RecurringTriggerScheduleActivationIdField = "scheduleActivationId";
    public const string RecurringTriggerScheduleNextOccurrenceField = "scheduleNextOccurrence";
    public const string RecurringTriggerScheduleIdField = "scheduleId";
    public const string RecurringTriggerScheduleIsActiveField = "scheduleIsActive";
    public const string WorkflowActivationSlotDefinitionIdField = "workflowDefinitionId";
    public const string WorkflowActivationSlotNameField = "slotName";
    public const string WorkflowActivationSlotActiveActivationIdField = "activeActivationId";
    public const string PublicationProjectionKindField = "projectionKind";
    public const string PublicationProjectionArtifactIdField = "projectionArtifactId";
    public const string SchedulerWorkOrderKeyField = "orderKey";
    public const string SchedulerWorkRecordedAtField = "recordedAt";
    // Stable scheduler-claim projections used by the provider-owned compare-and-delete predicate.
    public const string SchedulerWorkClaimOwnerIdField = "claimOwnerId";
    public const string SchedulerWorkFencingTokenField = "fencingToken";
    public const string SchedulerPoisonWorkItemIdField = "workItemId";
    public const string SchedulerPoisonFirstFailedAtField = "firstFailedAt";
    public const string SchedulerPoisonLastFailedAtField = "lastFailedAt";
    public const string BookmarkIdField = "bookmarkId";
    public const string PostCommitOutboxStatusField = "outboxStatus";
    public const string PostCommitOutboxDeliverableAtField = "deliverableAt";
    public const string PostCommitOutboxClaimableAtField = "claimableAt";
    public const string PostCommitOutboxRecordedAtField = "outboxRecordedAt";
    public const string PostCommitOutboxItemIdField = "outboxItemId";
    public const string PostCommitOutboxIntentKindField = "outboxIntentKind";
    public const string WorkflowDispatchCreatedAtField = "dispatchCreatedAt";
    public const string WorkflowDispatchIdField = "dispatchId";
    public const string WorkflowExecutableArtifactIdField = "executableArtifactId";
    public const string ExecutableActivityTemplateIdField = "templateId";
    public const string ActivityExecutionInspectionSummaryExecutionSequenceField = "summaryExecutionSequence";
    public const string ActivityExecutionInspectionSummaryScheduledAtField = "summaryScheduledAt";
    public const string ActivityExecutionInspectionSummaryActivityExecutionIdField = "summaryActivityExecutionId";
    public const string ActivityExecutionHierarchyIsScopeRootField = "isScopeRoot";
    public const string ActivityExecutionHierarchyExecutionSequenceField = "executionSequence";
    public const string ActivityExecutionHierarchyActivityExecutionIdField = "activityExecutionId";
    public const string StimulusLookupKeyField = "stimulusLookupKey";
    public const string StimulusTypeLookupKeyField = "stimulusTypeLookupKey";
    public const string WorkflowExecutionHistorySortTicksField = "historySortTicks";
    public const string WorkflowExecutionHistoryWorkflowExecutionIdField = "historyWorkflowExecutionId";
    public const string WorkflowExecutionHistoryTenantIdField = "historyTenantId";
    public const string WorkflowExecutionHistoryAuthorityPartitionField = "historyAuthorityPartition";
    public const string WorkflowExecutionHistoryDefinitionIdField = "historyDefinitionId";
    public const string WorkflowExecutionHistoryRunKindField = "historyRunKind";
    public const string WorkflowExecutionHistoryCorrelationIdField = "historyCorrelationId";
    public const string WorkflowExecutionHistoryArtifactIdField = "historyArtifactId";
    public const string WorkflowExecutionHistoryArtifactTimestampField = "historyArtifactTimestamp";
    public const string WorkflowExecutionHistoryStatusField = "historyStatus";
    public const string RecoveryInterruptedStatusField = "interruptedExecutionStatus";
    public const string RecoveryInterruptedAtField = "interruptedExecutionAt";
    public const string RecoveryLeaseOwnerIdField = "executionLeaseOwnerId";
    public const string RecoveryLeaseAcquiredAtField = "executionLeaseAcquiredAt";
    public const string RecoveryLeaseExpiresAtField = "executionLeaseExpiresAt";
    public const string RecoveryHeartbeatOwnerIdField = "heartbeatOwnerId";
    public const string RecoveryHeartbeatRecordedAtField = "heartbeatRecordedAt";
    public const string RecoveryHasOperationalOwnerField = "hasOperationalOwner";

    public const string ByWorkflowExecutionIndex = "by_workflow_execution";
    public const string ByCollectionIndex = "by_collection";
    public const string ByStimulusIndex = "by_stimulus";
    public const string ByStimulusTypeIndex = "by_stimulus_type";
    public const string ByArtifactIndex = "by_artifact";
    public const string ByDefinitionVersionIndex = "by_definition_version";
    public const string ByTemplateHashIndex = "by_template_hash";
    public const string ByExecutionScopeIndex = "by_execution_scope";
    public const string ByActivationIndex = "by_activation";
    public const string ByParentActivityExecutionIndex = "by_parent_activity_execution";
    public const string ByParentWorkflowExecutionIndex = "by_parent_workflow_execution";
    public const string ByChildWorkflowExecutionIndex = "by_child_workflow_execution";
    public const string ByStatusIndex = "by_status";
    public const string ByTestScopeIndex = "by_test_scope";
    public const string ByScopeIdIndex = "by_scope_id";
    public const string ByExpiresAtIndex = "by_expires_at";
    public const string ByCreatedAtIndex = "by_created_at";
    public const string ByDispatchIdIndex = "by_dispatch_id";
    public const string ByOutboxStatusIndex = "by_outbox_status";
    public const string ByOutboxDeliverableAtIndex = "by_outbox_deliverable_at";
    public const string ByOutboxClaimableAtIndex = "by_outbox_claimable_at";
    public const string ByOutboxRecordedAtIndex = "by_outbox_recorded_at";
    public const string ByOutboxItemIdIndex = "by_outbox_item_id";
    public const string ByOutboxIntentKindIndex = "by_outbox_intent_kind";
    public const string DurableTimerByDueTimeAndTimerIdIndex = "by_due_time_and_timer_id";
    public const string RecurringScheduleByActiveNextOccurrenceAndScheduleIdIndex = "by_active_next_occurrence_and_schedule_id";
    public const string RecurringScheduleByActivationAndScheduleIdIndex = "by_activation_and_schedule_id";
    public const string RecurringScheduleByArtifactAndScheduleIdIndex = "by_artifact_and_schedule_id";
    public const string BySchedulerWorkOrderIndex = "by_scheduler_work_order";
    public const string ByTimerIdIndex = "by_timer_id";
    public const string ByRecurringScheduleIdIndex = "by_recurring_schedule_id";
    public const string ByRecurringScheduleActiveIndex = "by_recurring_schedule_active";
    public const string ByScopeIndex = "by_scope";
    public const string ByRetiredIndex = "by_retired";
    public const string WorkflowTriggerBindingByActive = "by_active";
    public const string WorkflowActivationSlotByDefinition = "by_definition";
    public const string WorkflowActivationSlotByDefinitionAndSlotId = "by_definition_and_slot_id";
    public const string WorkflowActivationSlotByActiveActivation = "by_active_activation";
    public const string WorkflowActivationSlotByActiveActivationAndSlotId = "by_active_activation_and_slot_id";
    public const string WorkflowRunHealthStatusProfile = "workflow_run_health_status";
    public const string WorkflowRunHealthHourlyProfile = "workflow_run_health_hourly";
    public const string WorkflowRunHealthDailyProfile = "workflow_run_health_daily";
    public const string WorkflowRunHealthRunningProfile = "workflow_run_health_running";
    public const string WorkflowRunHealthTopFailuresProfile = "workflow_run_health_top_failures";
    public const int WorkflowRunHealthAggregationMaxInputRows = 100_000;
    public const int WorkflowRunHealthBucketMaxGroups = 744 * 6;
    public const int WorkflowRunHealthStatusMaxGroups = 8;
    public const int WorkflowRunHealthRunningMaxGroups = 1;
    public const int WorkflowRunHealthTopFailuresMaxGroups = 1_000;

    private static readonly IReadOnlyList<StorageUnit> units = CreateAll();

    public static IReadOnlyList<StorageUnit> CreateUnits() => units;

    public static StorageUnit Require(string unitId) =>
        units.Single(unit => StringComparer.Ordinal.Equals(unit.Id.Value, unitId));

    private static IReadOnlyList<StorageUnit> CreateAll() =>
    [
        Unit(BookmarkStateDocumentKind, "runtime_bookmark_state", [
            String(WorkflowExecutionIdField, RuntimeExecutionIdProjectionLength), String(StimulusHashField, StimulusHashProjectionLength), String(StimulusTypeField, StimulusTypeProjectionLength), String(StimulusLookupKeyField, BookmarkStimulusLookupKeyProjectionLength), String(StimulusTypeLookupKeyField, BookmarkStimulusLookupKeyProjectionLength), String(BookmarkIdField, RuntimeExecutionIdProjectionLength)], [
            Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), Index(ByStimulusIndex, StimulusHashField), Index(ByStimulusTypeIndex, StimulusTypeField),
            IncludedIndex("by_stimulus_and_type_and_bookmark_identity", StimulusLookupKeyField, WorkflowExecutionIdField, BookmarkIdField),
            IncludedIndex("by_stimulus_type_and_bookmark_identity", StimulusTypeLookupKeyField, WorkflowExecutionIdField, BookmarkIdField),
            IncludedIndex("by_workflow_execution_and_bookmark_id", WorkflowExecutionIdField, BookmarkIdField)]),
        Unit(WorkflowExecutableDocumentKind, "runtime_workflow_executable", [String(CollectionField, 128), String(WorkflowExecutableArtifactIdField, 128)], [Index(ByCollectionIndex, CollectionField), IncludedIndex("by_collection_and_document_id", CollectionField, WorkflowExecutableArtifactIdField)]),
        Unit(WorkflowExecutableCoordinationDocumentKind, "runtime_workflow_executable_coordination", [], []),
        Unit(ExecutableActivityTemplateDocumentKind, "runtime_executable_activity_template", [String(CollectionField, 128), String(TemplateHashField), String(ExecutableActivityTemplateIdField, 128)], [Index(ByCollectionIndex, CollectionField), IncludedIndex("by_collection_and_document_id", CollectionField, ExecutableActivityTemplateIdField), Index(ByTemplateHashIndex, TemplateHashField)]),
        Unit(ExecutableActivityTemplateHashClaimDocumentKind, "runtime_executable_activity_template_hash_claim", [], []),
        Unit(WorkflowExecutableSourceReferenceDocumentKind, "runtime_workflow_executable_source_reference", [String(CollectionField, 128), String(ArtifactIdField, 128), String(ScopeField), DateTime(ExpiresAtField), Boolean(IsRetiredField), String(WorkflowExecutableSourceReferenceIdField, 128), String(WorkflowExecutableSourceReferenceDefinitionIdField, 128, required: true), String(WorkflowExecutableSourceReferenceDefinitionVersionIdField, 128, required: true)], [Index(ByCollectionIndex, CollectionField), Index(ByArtifactIndex, ArtifactIdField), Index(ByDefinitionVersionIndex, WorkflowExecutableSourceReferenceDefinitionVersionIdField), Index(ByScopeIndex, ScopeField), Index(ByExpiresAtIndex, ExpiresAtField), Index(ByRetiredIndex, IsRetiredField), IncludedIndex("by_artifact_and_document_id", ArtifactIdField, WorkflowExecutableSourceReferenceIdField), IncludedIndex("by_artifact_retired_expiry_and_document_id", ArtifactIdField, IsRetiredField, ExpiresAtField, WorkflowExecutableSourceReferenceIdField), IncludedIndex("by_collection_and_document_id", CollectionField, WorkflowExecutableSourceReferenceIdField), IncludedIndex("by_collection_retired_expiry_and_document_id", CollectionField, IsRetiredField, ExpiresAtField, WorkflowExecutableSourceReferenceIdField), IncludedIndex("by_expiry_and_document_id", ExpiresAtField, WorkflowExecutableSourceReferenceIdField), IncludedIndex("by_retired_and_document_id", IsRetiredField, ExpiresAtField, WorkflowExecutableSourceReferenceIdField), IncludedIndex("by_scope_and_document_id", ScopeField, WorkflowExecutableSourceReferenceIdField), IncludedIndex("by_scope_retired_expiry_and_document_id", ScopeField, IsRetiredField, ExpiresAtField, WorkflowExecutableSourceReferenceIdField), IncludedIndex("by_scope_retired_expiry_definition_and_document_id", ScopeField, IsRetiredField, ExpiresAtField, WorkflowExecutableSourceReferenceDefinitionIdField, WorkflowExecutableSourceReferenceIdField), IncludedIndex("by_definition_version_and_document_id", WorkflowExecutableSourceReferenceDefinitionVersionIdField, WorkflowExecutableSourceReferenceIdField)]),
        Unit(ActivityExecutionStateDocumentKind, "runtime_activity_execution_state", [String(WorkflowExecutionIdField, 128), String(ParentActivityExecutionIdField, 128), String(ActivityExecutionIdField, 128), String(ExecutionScopeIdField, 128), String(StatusField, RuntimeStatusProjectionLength)], [Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), Index(ByParentActivityExecutionIndex, ParentActivityExecutionIdField), IncludedIndex("by_workflow_execution_and_activity_execution_id", WorkflowExecutionIdField, ActivityExecutionIdField), IncludedIndex("by_workflow_parent_and_activity_execution_id", WorkflowExecutionIdField, ParentActivityExecutionIdField, ActivityExecutionIdField)]),
        Unit(ActivityExecutionInspectionDocumentKind, "runtime_activity_execution_inspection", [String(WorkflowExecutionIdField, 128), Int64(ActivityExecutionInspectionSummaryExecutionSequenceField), DateTime(ActivityExecutionInspectionSummaryScheduledAtField), String(ActivityExecutionInspectionSummaryActivityExecutionIdField, 128)], [Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), IncludedIndex("by_workflow_execution_and_summary_order", WorkflowExecutionIdField, ActivityExecutionInspectionSummaryExecutionSequenceField, ActivityExecutionInspectionSummaryScheduledAtField, ActivityExecutionInspectionSummaryActivityExecutionIdField)]),
        Unit(ActivityExecutionHierarchyDocumentKind, "runtime_activity_execution_hierarchy", [String(WorkflowExecutionIdField, 128), String(ExecutionScopeIdField, 128), Boolean(ActivityExecutionHierarchyIsScopeRootField), Int64(ActivityExecutionHierarchyExecutionSequenceField), String(ActivityExecutionHierarchyActivityExecutionIdField, 128)], [Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), Index(ByExecutionScopeIndex, ExecutionScopeIdField), IncludedIndex("by_workflow_execution_and_hierarchy_order", WorkflowExecutionIdField, ActivityExecutionHierarchyExecutionSequenceField, ActivityExecutionHierarchyActivityExecutionIdField), IncludedIndex("by_workflow_execution_scope_and_hierarchy_order", WorkflowExecutionIdField, ExecutionScopeIdField, ActivityExecutionHierarchyIsScopeRootField, ActivityExecutionHierarchyExecutionSequenceField, ActivityExecutionHierarchyActivityExecutionIdField)]),
        Unit(WorkflowExecutionStateDocumentKind, "runtime_workflow_execution_state", [String(CollectionField, RuntimeCollectionProjectionLength), Int64(WorkflowExecutionHistorySortTicksField), String(WorkflowExecutionHistoryWorkflowExecutionIdField, RuntimeExecutionIdProjectionLength), String(WorkflowExecutionHistoryTenantIdField, RuntimeTenantProjectionLength), String(WorkflowExecutionHistoryAuthorityPartitionField, WorkflowExecutionHistoryAuthorityPartitionProjectionLength), String(WorkflowExecutionHistoryDefinitionIdField), Int32(WorkflowExecutionHistoryStatusField), Int32(WorkflowExecutionHistoryRunKindField), String(WorkflowExecutionHistoryCorrelationIdField), String(WorkflowExecutionHistoryArtifactIdField, RuntimeExecutionIdProjectionLength), DateTime(WorkflowExecutionHistoryArtifactTimestampField, required: true)], [IncludedIndex("by_history_order", WorkflowExecutionHistorySortTicksField, WorkflowExecutionHistoryWorkflowExecutionIdField), IncludedIndex("by_alteration_capture_tenant_and_execution", WorkflowExecutionHistoryTenantIdField, WorkflowExecutionHistoryAuthorityPartitionField, WorkflowExecutionHistoryWorkflowExecutionIdField), Index("by_collection_and_pinned_artifact", CollectionField, WorkflowExecutionHistoryArtifactIdField, WorkflowExecutionHistoryArtifactTimestampField, WorkflowExecutionHistoryWorkflowExecutionIdField), UniqueIndex("by_collection_and_pinned_artifact_v2", CollectionField, WorkflowExecutionHistoryArtifactIdField, WorkflowExecutionHistoryWorkflowExecutionIdField), IncludedIndex("by_attention_fault_history", WorkflowExecutionHistoryStatusField, WorkflowExecutionHistorySortTicksField, WorkflowExecutionHistoryWorkflowExecutionIdField)]),
        Unit(WorkflowAlterationPlanDocumentKind, "runtime_workflow_alteration_plan", [String(CollectionField, 128), String(WorkflowAlterationPlanIdField, 128), String(WorkflowAlterationPlanTenantPartitionField, 256), String(WorkflowAlterationPlanIdempotencyKeyHashField, WorkflowAlterationPlanIdempotencyKeyHashProjectionLength), String(WorkflowAlterationPlanTenantIdempotencyKeyField, WorkflowAlterationPlanTenantIdempotencyKeyProjectionLength), String(WorkflowAlterationPlanStatusField, 32), String(WorkflowAlterationPlanActiveOrderKeyField, WorkflowAlterationPlanActiveOrderKeyProjectionLength)], [IncludedIndex(ByCollectionIndex, CollectionField, WorkflowAlterationPlanIdField), IncludedIndex("by_tenant_and_idempotency_key", WorkflowAlterationPlanTenantPartitionField, WorkflowAlterationPlanIdempotencyKeyHashField, WorkflowAlterationPlanIdField), UniqueIndex("unique_tenant_and_idempotency_key", WorkflowAlterationPlanTenantIdempotencyKeyField), IncludedIndex(WorkflowAlterationPlanStatusField, WorkflowAlterationPlanStatusField, WorkflowAlterationPlanActiveOrderKeyField), IncludedIndex("by_tenant_and_status", WorkflowAlterationPlanTenantPartitionField, WorkflowAlterationPlanStatusField, WorkflowAlterationPlanActiveOrderKeyField)]),
        Unit(WorkflowAlterationJobDocumentKind, "runtime_workflow_alteration_job", [String(WorkflowAlterationJobIdField, 128), String(WorkflowAlterationJobPlanIdField, 128), Int64(WorkflowAlterationJobCaptureOrdinalField), DateTime(WorkflowAlterationJobClaimableAtField), String(WorkflowAlterationJobStatusField, 32), String(WorkflowAlterationJobCheckpointCommitIdField, 128)], [IncludedIndex("by_plan_and_capture_ordinal", WorkflowAlterationJobPlanIdField, WorkflowAlterationJobCaptureOrdinalField, WorkflowAlterationJobIdField), IncludedIndex("by_claimable_at", WorkflowAlterationJobClaimableAtField, WorkflowAlterationJobIdField), IncludedIndex("by_plan_and_claimable_at", WorkflowAlterationJobPlanIdField, WorkflowAlterationJobClaimableAtField, WorkflowAlterationJobIdField), IncludedIndex("by_plan_status_and_claimable_at", WorkflowAlterationJobPlanIdField, WorkflowAlterationJobStatusField, WorkflowAlterationJobClaimableAtField, WorkflowAlterationJobIdField), IncludedIndex("alteration_jobs_counts", WorkflowAlterationJobPlanIdField, WorkflowAlterationJobStatusField, WorkflowAlterationJobIdField), IncludedIndex("alteration_job_checkpoint", WorkflowAlterationJobCheckpointCommitIdField, WorkflowAlterationJobIdField), Index("checkpointCommitId", WorkflowAlterationJobCheckpointCommitIdField), Index("status", WorkflowAlterationJobStatusField)]),
        Unit(WorkflowTestScopeDocumentKind, "runtime_workflow_test_scope", [String(CollectionField, 128), String(StateField, 32), String(ScopeIdField, 128), DateTime(ExpiresAtField)], [Index(ByCollectionIndex, CollectionField), Index(ByExpiresAtIndex, ExpiresAtField), Index(ByScopeIdIndex, ScopeIdField), Index("by_state_and_expires_at", StateField, ExpiresAtField, ScopeIdField), Index("by_state_and_scope_id", StateField, ScopeIdField), Index(ByStatusIndex, StateField)]),
        Unit(DurableValueStateDocumentKind, "runtime_durable_value_state", [String(WorkflowExecutionIdField, 128), String(DurableValueIdField, 128)], [Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), IncludedIndex("by_workflow_execution_and_durable_value_id", WorkflowExecutionIdField, DurableValueIdField)]),
        Unit(SchedulerStateDocumentKind, "runtime_scheduler_state", [String(CollectionField, 128)], [Index(ByCollectionIndex, CollectionField)]),
        Unit(ExecutionLivenessStateDocumentKind, "runtime_execution_liveness_state", [String(WorkflowExecutionIdField, 128), String(CollectionField, 128), String(ExecutionLivenessOperationalStateIdField, 128), Int32(RecoveryInterruptedStatusField), DateTime(RecoveryInterruptedAtField), String(RecoveryLeaseOwnerIdField, IdMaximumLength), DateTime(RecoveryLeaseAcquiredAtField), DateTime(RecoveryLeaseExpiresAtField), String(RecoveryHeartbeatOwnerIdField, IdMaximumLength), DateTime(RecoveryHeartbeatRecordedAtField), Boolean(RecoveryHasOperationalOwnerField)], [Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), Index(ByCollectionIndex, CollectionField), IncludedIndex("by_collection_workflow_execution_and_operational_state_id", CollectionField, WorkflowExecutionIdField, ExecutionLivenessOperationalStateIdField), IncludedIndex("by_workflow_execution_and_operational_state_id", WorkflowExecutionIdField, ExecutionLivenessOperationalStateIdField), IncludedIndex("by_recovery_detected", RecoveryInterruptedStatusField, RecoveryInterruptedAtField), IncludedIndex("by_recovery_detected_heartbeat_owner", RecoveryInterruptedStatusField, RecoveryHeartbeatOwnerIdField, RecoveryInterruptedAtField), IncludedIndex("by_recovery_detected_lease_owner", RecoveryInterruptedStatusField, RecoveryLeaseOwnerIdField, RecoveryInterruptedAtField), IncludedIndex("by_recovery_detected_ownerless", RecoveryInterruptedStatusField, RecoveryHasOperationalOwnerField, RecoveryInterruptedAtField), IncludedIndex("by_recovery_heartbeat", RecoveryHeartbeatRecordedAtField), IncludedIndex("by_recovery_heartbeat_owner", RecoveryHeartbeatOwnerIdField, RecoveryHeartbeatRecordedAtField), IncludedIndex("by_recovery_lease_acquisition", RecoveryLeaseAcquiredAtField), IncludedIndex("by_recovery_lease_acquisition_owner", RecoveryLeaseOwnerIdField, RecoveryLeaseAcquiredAtField), IncludedIndex("by_recovery_lease_expiry", RecoveryLeaseExpiresAtField), IncludedIndex("by_recovery_lease_expiry_owner", RecoveryLeaseOwnerIdField, RecoveryLeaseExpiresAtField)]),
        Unit(WorkflowHoldStateDocumentKind, "runtime_workflow_hold_state", [String(WorkflowExecutionIdField, 128), String(CollectionField, 128)], [Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), Index(ByCollectionIndex, CollectionField)]),
        Unit(IncidentStateDocumentKind, "runtime_incident_state", [String(WorkflowExecutionIdField, 128), String(StatusField, 32), DateTime(CreatedAtField), String(IncidentIdField, 128)], [IncludedIndex("by_status_created_at_workflow_and_incident", StatusField, CreatedAtField, WorkflowExecutionIdField, IncidentIdField), Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), IncludedIndex("by_workflow_execution_and_incident_id", WorkflowExecutionIdField, IncidentIdField), IncludedIndex("by_workflow_execution_and_status_and_incident_id", WorkflowExecutionIdField, StatusField, IncidentIdField)]),
        Unit(CheckpointCommitDocumentKind, "runtime_checkpoint_commit", [String(CollectionField, 128)], [Index(ByCollectionIndex, CollectionField)]),
        Unit(PostCommitOutboxDocumentKind, "runtime_post_commit_outbox", [String(WorkflowExecutionIdField, 128), String(CollectionField, 128), Int32(PostCommitOutboxStatusField), DateTime(PostCommitOutboxDeliverableAtField), DateTime(PostCommitOutboxClaimableAtField), DateTime(PostCommitOutboxRecordedAtField), String(PostCommitOutboxItemIdField, PostCommitOutboxItemIdProjectionLength), String(PostCommitOutboxIntentKindField, PostCommitOutboxIntentKindProjectionLength)], [Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), Index(ByCollectionIndex, CollectionField), Index(ByOutboxStatusIndex, PostCommitOutboxStatusField), Index(ByOutboxDeliverableAtIndex, PostCommitOutboxDeliverableAtField), Index(ByOutboxClaimableAtIndex, PostCommitOutboxClaimableAtField), Index(ByOutboxRecordedAtIndex, PostCommitOutboxRecordedAtField), Index(ByOutboxItemIdIndex, PostCommitOutboxItemIdField), Index(ByOutboxIntentKindIndex, PostCommitOutboxIntentKindField), Index("by_claimable_time_recorded_id", PostCommitOutboxClaimableAtField, PostCommitOutboxRecordedAtField, PostCommitOutboxItemIdField), Index("by_claimable_by_workflow_time_recorded_id", WorkflowExecutionIdField, PostCommitOutboxClaimableAtField, PostCommitOutboxRecordedAtField, PostCommitOutboxItemIdField), Index("by_claimable_by_intent_kind_time_recorded_id", PostCommitOutboxIntentKindField, PostCommitOutboxClaimableAtField, PostCommitOutboxRecordedAtField, PostCommitOutboxItemIdField), Index("by_claimable_by_workflow_and_intent_kind_time_recorded_id", WorkflowExecutionIdField, PostCommitOutboxIntentKindField, PostCommitOutboxClaimableAtField, PostCommitOutboxRecordedAtField, PostCommitOutboxItemIdField), Index("by_deliverable_time_recorded_id", PostCommitOutboxDeliverableAtField, PostCommitOutboxRecordedAtField, PostCommitOutboxItemIdField), Index("by_deliverable_by_workflow_time_recorded_id", WorkflowExecutionIdField, PostCommitOutboxDeliverableAtField, PostCommitOutboxRecordedAtField, PostCommitOutboxItemIdField), Index("by_deliverable_by_intent_kind_time_recorded_id", PostCommitOutboxIntentKindField, PostCommitOutboxDeliverableAtField, PostCommitOutboxRecordedAtField, PostCommitOutboxItemIdField), Index("by_deliverable_by_workflow_and_intent_kind_time_recorded_id", WorkflowExecutionIdField, PostCommitOutboxIntentKindField, PostCommitOutboxDeliverableAtField, PostCommitOutboxRecordedAtField, PostCommitOutboxItemIdField)]),
        Unit(WorkflowDispatchDocumentKind, "runtime_workflow_dispatch", [String(CollectionField, 128), String(ParentWorkflowExecutionIdField, 128), String(ChildWorkflowExecutionIdField, 128), String(StatusField, 32), String(TestScopeIdField, 128), DateTime(WorkflowDispatchCreatedAtField), String(WorkflowDispatchIdField, WorkflowDispatchIdProjectionLength)], [Index(ByCollectionIndex, CollectionField), Index(ByParentWorkflowExecutionIndex, ParentWorkflowExecutionIdField), Index(ByChildWorkflowExecutionIndex, ChildWorkflowExecutionIdField), Index(ByStatusIndex, StatusField), Index(ByTestScopeIndex, TestScopeIdField), Index(ByCreatedAtIndex, WorkflowDispatchCreatedAtField), Index(ByDispatchIdIndex, WorkflowDispatchIdField), Index("by_child_workflow_execution_and_status", ChildWorkflowExecutionIdField, StatusField), Index("by_parent_workflow_execution_and_status", ParentWorkflowExecutionIdField, StatusField), Index("by_parent_workflow_execution_created_at_dispatch_id", ParentWorkflowExecutionIdField, WorkflowDispatchCreatedAtField, WorkflowDispatchIdField), Index("by_parent_workflow_execution_status_created_at_dispatch_id", ParentWorkflowExecutionIdField, StatusField, WorkflowDispatchCreatedAtField, WorkflowDispatchIdField), Index("by_parent_execution_status_scope_created_at_dispatch_id", ParentWorkflowExecutionIdField, StatusField, TestScopeIdField, WorkflowDispatchCreatedAtField, WorkflowDispatchIdField), Index("by_parent_workflow_execution_test_scope_created_at_dispatch_id", ParentWorkflowExecutionIdField, TestScopeIdField, WorkflowDispatchCreatedAtField, WorkflowDispatchIdField), Index("by_status_created_at_dispatch_id", StatusField, WorkflowDispatchCreatedAtField, WorkflowDispatchIdField), Index("by_status_test_scope_created_at_dispatch_id", StatusField, TestScopeIdField, WorkflowDispatchCreatedAtField, WorkflowDispatchIdField), Index("by_test_scope_created_at_dispatch_id", TestScopeIdField, WorkflowDispatchCreatedAtField, WorkflowDispatchIdField)]),
        Unit(SchedulerWorkItemDocumentKind, "runtime_scheduler_work_item", [String(SchedulerWorkOrderKeyField, SchedulerWorkOrderKeyProjectionLength), DateTime(SchedulerWorkRecordedAtField, required: true), String(WorkflowExecutionIdField, RuntimeExecutionIdProjectionLength), String(CollectionField, RuntimeCollectionProjectionLength), String(SchedulerWorkClaimOwnerIdField, IdMaximumLength), Int64(SchedulerWorkFencingTokenField)], [IncludedIndex(BySchedulerWorkOrderIndex, WorkflowExecutionIdField, SchedulerWorkOrderKeyField), Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), IncludedIndex("by_workflow_execution_and_scheduler_recorded_at_and_order", WorkflowExecutionIdField, SchedulerWorkRecordedAtField, SchedulerWorkOrderKeyField), IncludedIndex("by_workflow_execution_and_scheduler_work_order", CollectionField, WorkflowExecutionIdField, SchedulerWorkOrderKeyField)]),
        Unit(SchedulerPoisonDocumentKind, "runtime_scheduler_poison", [String(WorkflowExecutionIdField, RuntimeExecutionIdProjectionLength), String(SchedulerPoisonWorkItemIdField, SchedulerPoisonWorkItemProjectionLength), DateTime(SchedulerPoisonFirstFailedAtField), DateTime(SchedulerPoisonLastFailedAtField)], [Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), IncludedIndex("by_workflow_execution_and_failure_window", WorkflowExecutionIdField, SchedulerPoisonFirstFailedAtField, SchedulerPoisonLastFailedAtField, SchedulerPoisonWorkItemIdField)]),
        Unit(DurableTimerDocumentKind, "runtime_durable_timer", [String(CollectionField, RuntimeCollectionProjectionLength), String(WorkflowExecutionIdField, RuntimeExecutionIdProjectionLength), String(DurableTimerIdField, RuntimeExecutionIdProjectionLength), DateTime(DurableTimerDueTimeField), String(DurableTimerClaimOrderKeyField, DurableTimerClaimOrderKeyProjectionLength)], [Index(ByCollectionIndex, CollectionField), Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), Index("by_due_time", DurableTimerDueTimeField), IncludedIndex(DurableTimerByDueTimeAndTimerIdIndex, DurableTimerDueTimeField, DurableTimerIdField), Index(ByTimerIdIndex, DurableTimerIdField), IncludedIndex("by_workflow_execution_and_timer_id", WorkflowExecutionIdField, DurableTimerIdField), IncludedIndex("by_claim_order", DurableTimerClaimOrderKeyField)]),
        Unit(WorkflowRunHealthStateDocumentKind, "runtime_workflow_run_health_state", [String(WorkflowRunHealthDefinitionIdField, required: true), Int32(WorkflowRunHealthRunKindField, required: true), DateTime(WorkflowRunHealthStartedAtField), Int32(WorkflowRunHealthStatusField, required: true), Int64(WorkflowRunHealthIncidentCountField, required: true), Int64(WorkflowRunHealthIncidentBearingCountField, required: true)], [
            Index("by_started_at", WorkflowRunHealthStartedAtField),
            IncludedIndex("by_status_and_started_at", WorkflowRunHealthStatusField, WorkflowRunHealthStartedAtField),
            IncludedIndex("by_definition_and_started_at", WorkflowRunHealthDefinitionIdField, WorkflowRunHealthStartedAtField),
            IncludedIndex("by_run_kind_and_started_at", WorkflowRunHealthRunKindField, WorkflowRunHealthStartedAtField),
            IncludedIndex("by_run_kind_status_started_at", WorkflowRunHealthRunKindField, WorkflowRunHealthStatusField, WorkflowRunHealthStartedAtField),
            IncludedIndex("by_run_kind_status_definition_started_at", WorkflowRunHealthRunKindField, WorkflowRunHealthStatusField, WorkflowRunHealthDefinitionIdField, WorkflowRunHealthStartedAtField)
        ], AddWorkflowRunHealthAggregations),
        Unit(WorkflowTriggerBindingDocumentKind, "runtime_workflow_trigger_binding", [String(StimulusHashField, StimulusHashProjectionLength), String(StimulusTypeField, WorkflowTriggerBindingStimulusTypeProjectionLength), String(StimulusLookupKeyField, BookmarkStimulusLookupKeyProjectionLength), String(StimulusTypeLookupKeyField, BookmarkStimulusLookupKeyProjectionLength), Boolean(WorkflowTriggerBindingIsActiveField), String(TriggerBindingIdField, RuntimeExecutionIdProjectionLength), String(ArtifactIdField, RuntimeExecutionIdProjectionLength), String(ActivationIdField, RuntimeExecutionIdProjectionLength)], [Index(WorkflowTriggerBindingByActive, WorkflowTriggerBindingIsActiveField), Index(ByArtifactIndex, ArtifactIdField), IncludedIndex("by_artifact_and_trigger_binding_id", ArtifactIdField, TriggerBindingIdField), Index(ByActivationIndex, ActivationIdField), IncludedIndex("by_activation_and_trigger_binding_id", ActivationIdField, TriggerBindingIdField), Index(ByStimulusIndex, StimulusHashField), IncludedIndex("by_stimulus_and_type", StimulusLookupKeyField, WorkflowTriggerBindingIsActiveField, TriggerBindingIdField), Index(ByStimulusTypeIndex, StimulusTypeField), IncludedIndex("by_stimulus_type_and_active", StimulusTypeLookupKeyField, WorkflowTriggerBindingIsActiveField, TriggerBindingIdField), UniqueIndex("by_trigger_binding_id", TriggerBindingIdField)]),
        Unit(RecurringTriggerScheduleDocumentKind, "runtime_recurring_trigger_schedule", [String(CollectionField, 128), String(ArtifactIdField, 128), String(RecurringTriggerScheduleActivationIdField, 128), String(RecurringTriggerScheduleIdField, 128), Boolean(RecurringTriggerScheduleIsActiveField), DateTime(RecurringTriggerScheduleNextOccurrenceField)], [IncludedIndex(RecurringScheduleByActiveNextOccurrenceAndScheduleIdIndex, RecurringTriggerScheduleIsActiveField, RecurringTriggerScheduleNextOccurrenceField, RecurringTriggerScheduleIdField), Index(ByArtifactIndex, ArtifactIdField), IncludedIndex(RecurringScheduleByArtifactAndScheduleIdIndex, ArtifactIdField, RecurringTriggerScheduleIdField), Index(ByCollectionIndex, CollectionField), Index("by_next_occurrence", RecurringTriggerScheduleNextOccurrenceField), Index(ByActivationIndex, RecurringTriggerScheduleActivationIdField), IncludedIndex(RecurringScheduleByActivationAndScheduleIdIndex, RecurringTriggerScheduleActivationIdField, RecurringTriggerScheduleIdField), Index(ByRecurringScheduleActiveIndex, RecurringTriggerScheduleIsActiveField), Index(ByRecurringScheduleIdIndex, RecurringTriggerScheduleIdField)]),
        Unit(WorkflowActivationSlotDocumentKind, "runtime_workflow_activation_slot", [String(WorkflowActivationSlotDefinitionIdField, RuntimeExecutionIdProjectionLength), String(WorkflowActivationSlotNameField, 128), String(WorkflowActivationSlotActiveActivationIdField, RuntimeExecutionIdProjectionLength)], [Index(WorkflowActivationSlotByDefinition, WorkflowActivationSlotDefinitionIdField), IncludedIndex(WorkflowActivationSlotByDefinitionAndSlotId, WorkflowActivationSlotDefinitionIdField, WorkflowActivationSlotNameField, IdField), UniqueIndex(WorkflowActivationSlotByActiveActivation, WorkflowActivationSlotActiveActivationIdField), IncludedExcludedIndex(WorkflowActivationSlotByActiveActivationAndSlotId, WorkflowActivationSlotActiveActivationIdField, IdField)]),
        Unit(PublicationProjectionStateDocumentKind, "runtime_publication_projection_state", [String(PublicationProjectionKindField, 128), String(PublicationProjectionArtifactIdField, RuntimeExecutionIdProjectionLength)], [IncludedIndex("by_projection_kind_and_artifact_id", PublicationProjectionKindField, PublicationProjectionArtifactIdField, IdField)])
    ];

    private static StorageUnit Unit(
        string id,
        string name,
        IReadOnlyList<ColumnSpec> columns,
        IReadOnlyList<IndexSpec> indexes,
        Action<StorageDeclarationBuilder>? configure = null)
    {
        var declaration = StorageUnit.Declare(id, name)
            .String(IdField, IdMaximumLength, column => column.Required())
            .String(SchemaVersionField, SchemaVersionMaximumLength, column => column.Required())
            .Json(ContentField, column => column.Required())
            .Key(IdField)
            .OptimisticConcurrency(VersionField)
            .Scoped();

        foreach (var column in columns)
            column.AddTo(declaration);
        foreach (var index in indexes)
            index.AddTo(declaration);
        configure?.Invoke(declaration);

        var built = declaration.Build();
        var unit = built with
        {
            SchemaVersion = StorageSchemaVersion,
            AggregationProfiles = built.AggregationProfiles
                .Select(profile => profile.Name switch
                {
                    WorkflowRunHealthStatusProfile => profile with
                    {
                        MaxInputRows = WorkflowRunHealthAggregationMaxInputRows,
                        MaxGroups = WorkflowRunHealthStatusMaxGroups
                    },
                    WorkflowRunHealthHourlyProfile or WorkflowRunHealthDailyProfile => profile with
                    {
                        MaxInputRows = WorkflowRunHealthAggregationMaxInputRows,
                        MaxGroups = WorkflowRunHealthBucketMaxGroups
                    },
                    WorkflowRunHealthRunningProfile => profile with
                    {
                        MaxInputRows = WorkflowRunHealthAggregationMaxInputRows,
                        MaxGroups = WorkflowRunHealthRunningMaxGroups
                    },
                    WorkflowRunHealthTopFailuresProfile => profile with
                    {
                        MaxInputRows = WorkflowRunHealthAggregationMaxInputRows,
                        MaxGroups = WorkflowRunHealthTopFailuresMaxGroups
                    },
                    _ => profile
                })
                .ToArray()
        };

        var portability = PortabilityValidator.Validate(unit);
        if (!portability.IsPortable)
        {
            throw new InvalidOperationException(
                $"Runtime unit '{unit.Id.Value}' has portability refusals: {string.Join("; ", portability.Refusals.Select(refusal => $"{refusal.Code} {refusal.Path}: {refusal.Message}"))}");
        }

        return unit;
    }

    private static void AddWorkflowRunHealthAggregations(StorageDeclarationBuilder declaration)
    {
        declaration.Aggregate(WorkflowRunHealthStatusProfile, aggregate => aggregate
            .GroupBy(WorkflowRunHealthStatusField)
            .Count("count")
            .Sum("incidentTotal", WorkflowRunHealthIncidentCountField)
            .Sum("incidentBearingTotal", WorkflowRunHealthIncidentBearingCountField));
        declaration.Aggregate(WorkflowRunHealthHourlyProfile, aggregate => aggregate
            .FixedUtcBucket(WorkflowRunHealthBucketField, WorkflowRunHealthStartedAtField, TimeSpan.FromHours(1))
            .GroupBy(new AggregationGroup.Column(WorkflowRunHealthStatusField))
            .Count("count")
            .Sum("incidentTotal", WorkflowRunHealthIncidentCountField)
            .Sum("incidentBearingTotal", WorkflowRunHealthIncidentBearingCountField));
        declaration.Aggregate(WorkflowRunHealthDailyProfile, aggregate => aggregate
            .LocalCalendarDayBucket(WorkflowRunHealthBucketField, WorkflowRunHealthStartedAtField)
            .GroupBy(new AggregationGroup.Column(WorkflowRunHealthStatusField))
            .Count("count")
            .Sum("incidentTotal", WorkflowRunHealthIncidentCountField)
            .Sum("incidentBearingTotal", WorkflowRunHealthIncidentBearingCountField));
        declaration.Aggregate(WorkflowRunHealthRunningProfile, aggregate => aggregate
            .GroupBy(WorkflowRunHealthStatusField)
            .Count("count"));
        declaration.Aggregate(WorkflowRunHealthTopFailuresProfile, aggregate => aggregate
            .GroupBy(WorkflowRunHealthDefinitionIdField)
            .Count("failedCount"));
    }

    private static ColumnSpec String(string name, int maxLength = IdMaximumLength, bool required = false) =>
        new(name, PortableType.String, maxLength, required);

    private static ColumnSpec Int64(string name, bool required = false) => new(name, PortableType.Int64, Required: required);

    private static ColumnSpec Int32(string name, bool required = false) => new(name, PortableType.Int32, Required: required);

    private static ColumnSpec DateTime(string name, bool required = false) => new(name, PortableType.DateTimeOffset, Required: required);

    private static ColumnSpec Boolean(string name, bool required = false) => new(name, PortableType.Boolean, Required: required);

    private static IndexSpec Index(string name, params string[] columns) => new(name, columns, false);

    private static IndexSpec IncludedIndex(string name, params string[] columns) =>
        new(name, columns, false, MissingValueBehavior.Included);

    private static IndexSpec IncludedExcludedIndex(string name, params string[] columns) =>
        new(name, columns, false, MissingValueBehavior.Excluded);

    private static IndexSpec UniqueIndex(string name, params string[] columns) => new(name, columns, true);

    private sealed record ColumnSpec(string Name, PortableType Type, int? MaxLength = null, bool Required = false)
    {
        public void AddTo(StorageDeclarationBuilder declaration)
        {
            switch (Type)
            {
                case PortableType.String:
                    declaration.String(Name, MaxLength ?? IdMaximumLength, column =>
                    {
                        if (Required)
                            column.Required();
                    });
                    break;
                case PortableType.Int64:
                    declaration.Int64(Name, column =>
                    {
                        if (Required)
                            column.Required();
                    });
                    break;
                case PortableType.DateTimeOffset:
                    declaration.Timestamp(Name, column =>
                    {
                        if (Required)
                            column.Required();
                    });
                    break;
                case PortableType.Boolean:
                    declaration.Boolean(Name, column =>
                    {
                        if (Required)
                            column.Required();
                    });
                    break;
                case PortableType.Int32:
                    declaration.Int32(Name, column =>
                    {
                        if (Required)
                            column.Required();
                    });
                    break;
                default:
                    throw new InvalidOperationException($"Runtime column '{Name}' has unsupported type '{Type}'.");
            }
        }
    }

    private sealed record IndexSpec(
        string Name,
        IReadOnlyList<string> Columns,
        bool Unique,
        MissingValueBehavior MissingValues = MissingValueBehavior.Excluded)
    {
        public void AddTo(StorageDeclarationBuilder declaration)
        {
            Action<IndexBuilder> configure = index =>
            {
                foreach (var column in Columns)
                    index.Column(column);
                if (MissingValues == MissingValueBehavior.Excluded)
                    index.ExcludeMissingValues();
            };

            if (Unique)
                declaration.UniqueIndex(Name, configure);
            else
                declaration.Index(Name, configure);
        }
    }
}
