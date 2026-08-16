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
    public const string WorkflowTriggerBindingDocumentKind = "workflowTriggerBinding";
    public const string RecurringTriggerScheduleDocumentKind = "recurringTriggerSchedule";
    public const string PublicationProjectionStateDocumentKind = "publicationProjectionState";

    public const string WorkflowExecutionIdField = "workflowExecutionId";
    public const string CollectionField = "collection";
    public const string StimulusHashField = "stimulusHash";
    public const string StimulusTypeField = "stimulusType";
    public const string ArtifactIdField = "artifactId";
    public const string TemplateHashField = "templateHash";
    public const string ExecutionScopeIdField = "executionScopeId";
    public const string PublicationIdField = "publicationId";
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
    public const string DurableValueIdField = "durableValueId";
    public const string ExecutionLivenessOperationalStateIdField = "operationalStateId";
    public const string ActivityExecutionIdField = "activityExecutionId";
    public const string DurableTimerDueTimeField = "timerDueTime";
    public const string DurableTimerIdField = "timerId";
    public const string DurableTimerClaimOrderKeyField = "claimOrderKey";
    public const string TriggerBindingIdField = "triggerBindingId";
    public const string WorkflowTriggerBindingIsActiveField = "isActive";
    public const string RecurringTriggerSchedulePublicationIdField = "schedulePublicationId";
    public const string RecurringTriggerScheduleNextOccurrenceField = "scheduleNextOccurrence";
    public const string RecurringTriggerScheduleIdField = "scheduleId";
    public const string RecurringTriggerScheduleIsActiveField = "scheduleIsActive";
    public const string SchedulerWorkOrderKeyField = "orderKey";
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
    public const string WorkflowExecutionHistoryStatusField = "historyStatus";
    public const string RecoveryInterruptedStatusField = "interruptedExecutionStatus";
    public const string RecoveryInterruptedAtField = "interruptedExecutionAt";
    public const string RecoveryLeaseOwnerIdField = "executionLeaseOwnerId";
    public const string RecoveryLeaseAcquiredAtField = "executionLeaseAcquiredAt";
    public const string RecoveryLeaseExpiresAtField = "executionLeaseExpiresAt";
    public const string RecoveryHeartbeatOwnerIdField = "heartbeatOwnerId";
    public const string RecoveryHeartbeatRecordedAtField = "heartbeatRecordedAt";
    public const string RecoveryHasOperationalOwnerField = "hasOperationalOwner";

    public const string ByWorkflowExecutionIndex = "by-workflow-execution";
    public const string ByCollectionIndex = "by-collection";
    public const string ByStimulusIndex = "by-stimulus";
    public const string ByStimulusTypeIndex = "by-stimulus-type";
    public const string ByArtifactIndex = "by-artifact";
    public const string ByTemplateHashIndex = "by-template-hash";
    public const string ByExecutionScopeIndex = "by-execution-scope";
    public const string ByPublicationIndex = "by-publication";
    public const string ByParentActivityExecutionIndex = "by-parent-activity-execution";
    public const string ByParentWorkflowExecutionIndex = "by-parent-workflow-execution";
    public const string ByChildWorkflowExecutionIndex = "by-child-workflow-execution";
    public const string ByStatusIndex = "by-status";
    public const string ByTestScopeIndex = "by-test-scope";
    public const string ByScopeIdIndex = "by-scope-id";
    public const string ByExpiresAtIndex = "by-expires-at";
    public const string ByCreatedAtIndex = "by-created-at";
    public const string ByDispatchIdIndex = "by-dispatch-id";
    public const string ByOutboxStatusIndex = "by-outbox-status";
    public const string ByOutboxDeliverableAtIndex = "by-outbox-deliverable-at";
    public const string ByOutboxClaimableAtIndex = "by-outbox-claimable-at";
    public const string ByOutboxRecordedAtIndex = "by-outbox-recorded-at";
    public const string ByOutboxItemIdIndex = "by-outbox-item-id";
    public const string ByOutboxIntentKindIndex = "by-outbox-intent-kind";
    public const string BySchedulerWorkOrderIndex = "by-scheduler-work-order";
    public const string ByTimerIdIndex = "by-timer-id";
    public const string ByRecurringScheduleIdIndex = "by-recurring-schedule-id";
    public const string ByRecurringScheduleActiveIndex = "by-recurring-schedule-active";
    public const string ByScopeIndex = "by-scope";
    public const string ByRetiredIndex = "by-retired";
    public const string WorkflowTriggerBindingByActive = "by-active";

    private static readonly IReadOnlyList<StorageUnit> units = CreateAll();

    public static IReadOnlyList<StorageUnit> CreateUnits() => units;

    public static StorageUnit Require(string unitId) =>
        units.Single(unit => StringComparer.Ordinal.Equals(unit.Id.Value, unitId));

    private static IReadOnlyList<StorageUnit> CreateAll() =>
    [
        Unit(BookmarkStateDocumentKind, "runtime_bookmark_state", [
            String(WorkflowExecutionIdField, RuntimeExecutionIdProjectionLength), String(StimulusHashField, StimulusHashProjectionLength), String(StimulusTypeField, StimulusTypeProjectionLength), String(StimulusLookupKeyField, BookmarkStimulusLookupKeyProjectionLength), String(StimulusTypeLookupKeyField, BookmarkStimulusLookupKeyProjectionLength), String(BookmarkIdField, RuntimeExecutionIdProjectionLength)], [
            Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), Index(ByStimulusIndex, StimulusHashField), Index(ByStimulusTypeIndex, StimulusTypeField),
            IncludedIndex("by-stimulus-and-type-and-bookmark-identity", StimulusLookupKeyField, WorkflowExecutionIdField, BookmarkIdField),
            IncludedIndex("by-stimulus-type-and-bookmark-identity", StimulusTypeLookupKeyField, WorkflowExecutionIdField, BookmarkIdField),
            IncludedIndex("by-workflow-execution-and-bookmark-id", WorkflowExecutionIdField, BookmarkIdField)]),
        Unit(WorkflowExecutableDocumentKind, "runtime_workflow_executable", [String(CollectionField, 128), String(WorkflowExecutableArtifactIdField, 128)], [Index(ByCollectionIndex, CollectionField), IncludedIndex("by-collection-and-document-id", CollectionField, WorkflowExecutableArtifactIdField)]),
        Unit(WorkflowExecutableCoordinationDocumentKind, "runtime_workflow_executable_coordination", [], []),
        Unit(ExecutableActivityTemplateDocumentKind, "runtime_executable_activity_template", [String(CollectionField, 128), String(TemplateHashField), String(ExecutableActivityTemplateIdField, 128)], [Index(ByCollectionIndex, CollectionField), IncludedIndex("by-collection-and-document-id", CollectionField, ExecutableActivityTemplateIdField), Index(ByTemplateHashIndex, TemplateHashField)]),
        Unit(ExecutableActivityTemplateHashClaimDocumentKind, "runtime_executable_activity_template_hash_claim", [], []),
        Unit(WorkflowExecutableSourceReferenceDocumentKind, "runtime_workflow_executable_source_reference", [String(CollectionField, 128), String(ArtifactIdField, 128), String(ScopeField), DateTime(ExpiresAtField), Boolean(IsRetiredField), String(WorkflowExecutableSourceReferenceIdField, 128)], [Index(ByCollectionIndex, CollectionField), Index(ByArtifactIndex, ArtifactIdField), Index(ByScopeIndex, ScopeField), Index(ByExpiresAtIndex, ExpiresAtField), Index(ByRetiredIndex, IsRetiredField), IncludedIndex("by-artifact-and-document-id", ArtifactIdField, WorkflowExecutableSourceReferenceIdField), IncludedIndex("by-artifact-retired-expiry-and-document-id", ArtifactIdField, IsRetiredField, ExpiresAtField, WorkflowExecutableSourceReferenceIdField), IncludedIndex("by-collection-and-document-id", CollectionField, WorkflowExecutableSourceReferenceIdField), IncludedIndex("by-collection-retired-expiry-and-document-id", CollectionField, IsRetiredField, ExpiresAtField, WorkflowExecutableSourceReferenceIdField), IncludedIndex("by-expiry-and-document-id", ExpiresAtField, WorkflowExecutableSourceReferenceIdField), IncludedIndex("by-retired-and-document-id", IsRetiredField, ExpiresAtField, WorkflowExecutableSourceReferenceIdField), IncludedIndex("by-scope-and-document-id", ScopeField, WorkflowExecutableSourceReferenceIdField), IncludedIndex("by-scope-retired-expiry-and-document-id", ScopeField, IsRetiredField, ExpiresAtField, WorkflowExecutableSourceReferenceIdField)]),
        Unit(ActivityExecutionStateDocumentKind, "runtime_activity_execution_state", [String(WorkflowExecutionIdField, 128), String(ParentActivityExecutionIdField, 128), String(ActivityExecutionIdField, 128)], [Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), Index(ByParentActivityExecutionIndex, ParentActivityExecutionIdField), IncludedIndex("by-workflow-execution-and-activity-execution-id", WorkflowExecutionIdField, ActivityExecutionIdField), IncludedIndex("by-workflow-parent-and-activity-execution-id", WorkflowExecutionIdField, ParentActivityExecutionIdField, ActivityExecutionIdField)]),
        Unit(ActivityExecutionInspectionDocumentKind, "runtime_activity_execution_inspection", [String(WorkflowExecutionIdField, 128), Int64(ActivityExecutionInspectionSummaryExecutionSequenceField), DateTime(ActivityExecutionInspectionSummaryScheduledAtField), String(ActivityExecutionInspectionSummaryActivityExecutionIdField, 128)], [Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), IncludedIndex("by-workflow-execution-and-summary-order", WorkflowExecutionIdField, ActivityExecutionInspectionSummaryExecutionSequenceField, ActivityExecutionInspectionSummaryScheduledAtField, ActivityExecutionInspectionSummaryActivityExecutionIdField)]),
        Unit(ActivityExecutionHierarchyDocumentKind, "runtime_activity_execution_hierarchy", [String(WorkflowExecutionIdField, 128), String(ExecutionScopeIdField, 128), Boolean(ActivityExecutionHierarchyIsScopeRootField), Int64(ActivityExecutionHierarchyExecutionSequenceField), String(ActivityExecutionHierarchyActivityExecutionIdField, 128)], [Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), Index(ByExecutionScopeIndex, ExecutionScopeIdField), IncludedIndex("by-workflow-execution-and-hierarchy-order", WorkflowExecutionIdField, ActivityExecutionHierarchyExecutionSequenceField, ActivityExecutionHierarchyActivityExecutionIdField), IncludedIndex("by-workflow-execution-scope-and-hierarchy-order", WorkflowExecutionIdField, ExecutionScopeIdField, ActivityExecutionHierarchyIsScopeRootField, ActivityExecutionHierarchyExecutionSequenceField, ActivityExecutionHierarchyActivityExecutionIdField)]),
        Unit(WorkflowExecutionStateDocumentKind, "runtime_workflow_execution_state", [String(CollectionField, RuntimeCollectionProjectionLength), Int64(WorkflowExecutionHistorySortTicksField), String(WorkflowExecutionHistoryWorkflowExecutionIdField, RuntimeExecutionIdProjectionLength), String(WorkflowExecutionHistoryTenantIdField, RuntimeTenantProjectionLength), String(WorkflowExecutionHistoryAuthorityPartitionField, WorkflowExecutionHistoryAuthorityPartitionProjectionLength), String(WorkflowExecutionHistoryDefinitionIdField), Int32(WorkflowExecutionHistoryStatusField), Int32(WorkflowExecutionHistoryRunKindField), String(WorkflowExecutionHistoryCorrelationIdField), String(WorkflowExecutionHistoryArtifactIdField, RuntimeExecutionIdProjectionLength)], [IncludedIndex("by-history-order", WorkflowExecutionHistorySortTicksField, WorkflowExecutionHistoryWorkflowExecutionIdField), IncludedIndex("by-alteration-capture-tenant-and-execution", WorkflowExecutionHistoryTenantIdField, WorkflowExecutionHistoryAuthorityPartitionField, WorkflowExecutionHistoryWorkflowExecutionIdField), Index("by-collection-and-pinned-artifact", CollectionField, WorkflowExecutionHistoryArtifactIdField, WorkflowExecutionHistoryWorkflowExecutionIdField), UniqueIndex("by-collection-and-pinned-artifact-v2", CollectionField, WorkflowExecutionHistoryArtifactIdField, WorkflowExecutionHistoryWorkflowExecutionIdField), IncludedIndex("by-attention-fault-history", WorkflowExecutionHistoryStatusField, WorkflowExecutionHistorySortTicksField, WorkflowExecutionHistoryWorkflowExecutionIdField)]),
        Unit(WorkflowAlterationPlanDocumentKind, "runtime_workflow_alteration_plan", [String(CollectionField, 128), String(WorkflowAlterationPlanIdField, 128), String(WorkflowAlterationPlanTenantPartitionField, 256), String(WorkflowAlterationPlanIdempotencyKeyHashField, WorkflowAlterationPlanIdempotencyKeyHashProjectionLength), String(WorkflowAlterationPlanTenantIdempotencyKeyField, WorkflowAlterationPlanTenantIdempotencyKeyProjectionLength), String(WorkflowAlterationPlanStatusField, 32), String(WorkflowAlterationPlanActiveOrderKeyField, WorkflowAlterationPlanActiveOrderKeyProjectionLength)], [IncludedIndex(ByCollectionIndex, CollectionField, WorkflowAlterationPlanIdField), IncludedIndex("by-tenant-and-idempotency-key", WorkflowAlterationPlanTenantPartitionField, WorkflowAlterationPlanIdempotencyKeyHashField, WorkflowAlterationPlanIdField), UniqueIndex("unique-tenant-and-idempotency-key", WorkflowAlterationPlanTenantIdempotencyKeyField), IncludedIndex(WorkflowAlterationPlanStatusField, WorkflowAlterationPlanStatusField, WorkflowAlterationPlanActiveOrderKeyField), IncludedIndex("by-tenant-and-status", WorkflowAlterationPlanTenantPartitionField, WorkflowAlterationPlanStatusField, WorkflowAlterationPlanActiveOrderKeyField)]),
        Unit(WorkflowAlterationJobDocumentKind, "runtime_workflow_alteration_job", [String(WorkflowAlterationJobIdField, 128), String(WorkflowAlterationJobPlanIdField, 128), Int64(WorkflowAlterationJobCaptureOrdinalField), DateTime(WorkflowAlterationJobClaimableAtField), String(WorkflowAlterationJobStatusField, 32), String(WorkflowAlterationJobCheckpointCommitIdField, 128)], [IncludedIndex("by-plan-and-capture-ordinal", WorkflowAlterationJobPlanIdField, WorkflowAlterationJobCaptureOrdinalField, WorkflowAlterationJobIdField), IncludedIndex("by-claimable-at", WorkflowAlterationJobClaimableAtField, WorkflowAlterationJobIdField), IncludedIndex("by-plan-and-claimable-at", WorkflowAlterationJobPlanIdField, WorkflowAlterationJobClaimableAtField, WorkflowAlterationJobIdField), IncludedIndex("by-plan-status-and-claimable-at", WorkflowAlterationJobPlanIdField, WorkflowAlterationJobStatusField, WorkflowAlterationJobClaimableAtField, WorkflowAlterationJobIdField), IncludedIndex("alteration_jobs_counts", WorkflowAlterationJobPlanIdField, WorkflowAlterationJobStatusField, WorkflowAlterationJobIdField), IncludedIndex("alteration_job_checkpoint", WorkflowAlterationJobCheckpointCommitIdField, WorkflowAlterationJobIdField), Index("checkpointCommitId", WorkflowAlterationJobCheckpointCommitIdField), Index("status", WorkflowAlterationJobStatusField)]),
        Unit(WorkflowTestScopeDocumentKind, "runtime_workflow_test_scope", [String(CollectionField, 128), String(StateField, 32), String(ScopeIdField, 128), DateTime(ExpiresAtField)], [Index(ByCollectionIndex, CollectionField), Index(ByExpiresAtIndex, ExpiresAtField), Index(ByScopeIdIndex, ScopeIdField), Index("by-state-and-expires-at", StateField, ScopeIdField, ExpiresAtField), Index("by-state-and-scope-id", StateField, ScopeIdField), Index(ByStatusIndex, StateField)]),
        Unit(DurableValueStateDocumentKind, "runtime_durable_value_state", [String(WorkflowExecutionIdField, 128), String(DurableValueIdField, 128)], [Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), IncludedIndex("by-workflow-execution-and-durable-value-id", WorkflowExecutionIdField, DurableValueIdField)]),
        Unit(SchedulerStateDocumentKind, "runtime_scheduler_state", [String(CollectionField, 128)], [Index(ByCollectionIndex, CollectionField)]),
        Unit(ExecutionLivenessStateDocumentKind, "runtime_execution_liveness_state", [String(WorkflowExecutionIdField, 128), String(CollectionField, 128), String(ExecutionLivenessOperationalStateIdField, 128), Int32(RecoveryInterruptedStatusField), DateTime(RecoveryInterruptedAtField), String(RecoveryLeaseOwnerIdField, IdMaximumLength), DateTime(RecoveryLeaseAcquiredAtField), DateTime(RecoveryLeaseExpiresAtField), String(RecoveryHeartbeatOwnerIdField, IdMaximumLength), DateTime(RecoveryHeartbeatRecordedAtField), Boolean(RecoveryHasOperationalOwnerField)], [Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), Index(ByCollectionIndex, CollectionField), IncludedIndex("by-collection-workflow-execution-and-operational-state-id", CollectionField, WorkflowExecutionIdField, ExecutionLivenessOperationalStateIdField), IncludedIndex("by-workflow-execution-and-operational-state-id", WorkflowExecutionIdField, ExecutionLivenessOperationalStateIdField), IncludedIndex("by-recovery-detected", RecoveryInterruptedStatusField, RecoveryInterruptedAtField), IncludedIndex("by-recovery-detected-heartbeat-owner", RecoveryInterruptedStatusField, RecoveryHeartbeatOwnerIdField, RecoveryInterruptedAtField), IncludedIndex("by-recovery-detected-lease-owner", RecoveryInterruptedStatusField, RecoveryLeaseOwnerIdField, RecoveryInterruptedAtField), IncludedIndex("by-recovery-detected-ownerless", RecoveryInterruptedStatusField, RecoveryHasOperationalOwnerField, RecoveryInterruptedAtField), IncludedIndex("by-recovery-heartbeat", RecoveryHeartbeatRecordedAtField), IncludedIndex("by-recovery-heartbeat-owner", RecoveryHeartbeatOwnerIdField, RecoveryHeartbeatRecordedAtField), IncludedIndex("by-recovery-lease-acquisition", RecoveryLeaseAcquiredAtField), IncludedIndex("by-recovery-lease-acquisition-owner", RecoveryLeaseOwnerIdField, RecoveryLeaseAcquiredAtField), IncludedIndex("by-recovery-lease-expiry", RecoveryLeaseExpiresAtField), IncludedIndex("by-recovery-lease-expiry-owner", RecoveryLeaseOwnerIdField, RecoveryLeaseExpiresAtField)]),
        Unit(WorkflowHoldStateDocumentKind, "runtime_workflow_hold_state", [String(WorkflowExecutionIdField, 128), String(CollectionField, 128)], [Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), Index(ByCollectionIndex, CollectionField)]),
        Unit(IncidentStateDocumentKind, "runtime_incident_state", [String(WorkflowExecutionIdField, 128), String(StatusField, 32), DateTime(CreatedAtField), String(IncidentIdField, 128)], [IncludedIndex("by-status-created-at-workflow-and-incident", StatusField, CreatedAtField, WorkflowExecutionIdField, IncidentIdField), Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField)]),
        Unit(CheckpointCommitDocumentKind, "runtime_checkpoint_commit", [String(CollectionField, 128)], [Index(ByCollectionIndex, CollectionField)]),
        Unit(PostCommitOutboxDocumentKind, "runtime_post_commit_outbox", [String(WorkflowExecutionIdField, 128), String(CollectionField, 128), Int32(PostCommitOutboxStatusField), DateTime(PostCommitOutboxDeliverableAtField), DateTime(PostCommitOutboxClaimableAtField), DateTime(PostCommitOutboxRecordedAtField), String(PostCommitOutboxItemIdField, PostCommitOutboxItemIdProjectionLength), String(PostCommitOutboxIntentKindField, PostCommitOutboxIntentKindProjectionLength)], [Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), Index(ByCollectionIndex, CollectionField), Index(ByOutboxStatusIndex, PostCommitOutboxStatusField), Index(ByOutboxDeliverableAtIndex, PostCommitOutboxDeliverableAtField), Index(ByOutboxClaimableAtIndex, PostCommitOutboxClaimableAtField), Index(ByOutboxRecordedAtIndex, PostCommitOutboxRecordedAtField), Index(ByOutboxItemIdIndex, PostCommitOutboxItemIdField), Index(ByOutboxIntentKindIndex, PostCommitOutboxIntentKindField), Index("by-claimable-time-recorded-id", PostCommitOutboxClaimableAtField, PostCommitOutboxRecordedAtField, PostCommitOutboxItemIdField), Index("by-claimable-by-workflow-time-recorded-id", WorkflowExecutionIdField, PostCommitOutboxClaimableAtField, PostCommitOutboxRecordedAtField, PostCommitOutboxItemIdField), Index("by-claimable-by-intent-kind-time-recorded-id", PostCommitOutboxIntentKindField, PostCommitOutboxClaimableAtField, PostCommitOutboxRecordedAtField, PostCommitOutboxItemIdField), Index("by-claimable-by-workflow-and-intent-kind-time-recorded-id", WorkflowExecutionIdField, PostCommitOutboxIntentKindField, PostCommitOutboxClaimableAtField, PostCommitOutboxRecordedAtField, PostCommitOutboxItemIdField), Index("by-deliverable-time-recorded-id", PostCommitOutboxDeliverableAtField, PostCommitOutboxRecordedAtField, PostCommitOutboxItemIdField), Index("by-deliverable-by-workflow-time-recorded-id", WorkflowExecutionIdField, PostCommitOutboxDeliverableAtField, PostCommitOutboxRecordedAtField, PostCommitOutboxItemIdField), Index("by-deliverable-by-intent-kind-time-recorded-id", PostCommitOutboxIntentKindField, PostCommitOutboxDeliverableAtField, PostCommitOutboxRecordedAtField, PostCommitOutboxItemIdField), Index("by-deliverable-by-workflow-and-intent-kind-time-recorded-id", WorkflowExecutionIdField, PostCommitOutboxIntentKindField, PostCommitOutboxDeliverableAtField, PostCommitOutboxRecordedAtField, PostCommitOutboxItemIdField)]),
        Unit(WorkflowDispatchDocumentKind, "runtime_workflow_dispatch", [String(CollectionField, 128), String(ParentWorkflowExecutionIdField, 128), String(ChildWorkflowExecutionIdField, 128), String(StatusField, 32), String(TestScopeIdField, 128), DateTime(WorkflowDispatchCreatedAtField), String(WorkflowDispatchIdField, WorkflowDispatchIdProjectionLength)], [Index(ByCollectionIndex, CollectionField), Index(ByParentWorkflowExecutionIndex, ParentWorkflowExecutionIdField), Index(ByChildWorkflowExecutionIndex, ChildWorkflowExecutionIdField), Index(ByStatusIndex, StatusField), Index(ByTestScopeIndex, TestScopeIdField), Index(ByCreatedAtIndex, WorkflowDispatchCreatedAtField), Index(ByDispatchIdIndex, WorkflowDispatchIdField), Index("by-child-workflow-execution-and-status", ChildWorkflowExecutionIdField, StatusField), Index("by-parent-workflow-execution-and-status", ParentWorkflowExecutionIdField, StatusField), Index("by-parent-workflow-execution-created-at-dispatch-id", ParentWorkflowExecutionIdField, WorkflowDispatchCreatedAtField, WorkflowDispatchIdField), Index("by-parent-workflow-execution-status-created-at-dispatch-id", ParentWorkflowExecutionIdField, StatusField, WorkflowDispatchCreatedAtField, WorkflowDispatchIdField), Index("by-parent-workflow-execution-status-test-scope-created-at-dispatch-id", ParentWorkflowExecutionIdField, StatusField, TestScopeIdField, WorkflowDispatchCreatedAtField, WorkflowDispatchIdField), Index("by-parent-workflow-execution-test-scope-created-at-dispatch-id", ParentWorkflowExecutionIdField, TestScopeIdField, WorkflowDispatchCreatedAtField, WorkflowDispatchIdField), Index("by-status-created-at-dispatch-id", StatusField, WorkflowDispatchCreatedAtField, WorkflowDispatchIdField), Index("by-status-test-scope-created-at-dispatch-id", StatusField, TestScopeIdField, WorkflowDispatchCreatedAtField, WorkflowDispatchIdField), Index("by-test-scope-created-at-dispatch-id", TestScopeIdField, WorkflowDispatchCreatedAtField, WorkflowDispatchIdField)]),
        Unit(SchedulerWorkItemDocumentKind, "runtime_scheduler_work_item", [String(SchedulerWorkOrderKeyField, SchedulerWorkOrderKeyProjectionLength), String(WorkflowExecutionIdField, RuntimeExecutionIdProjectionLength), String(CollectionField, RuntimeCollectionProjectionLength)], [IncludedIndex(BySchedulerWorkOrderIndex, WorkflowExecutionIdField, SchedulerWorkOrderKeyField), Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), IncludedIndex("by-workflow-execution-and-scheduler-work-order", CollectionField, WorkflowExecutionIdField, SchedulerWorkOrderKeyField)]),
        Unit(SchedulerPoisonDocumentKind, "runtime_scheduler_poison", [String(WorkflowExecutionIdField, 128)], [Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField)]),
        Unit(DurableTimerDocumentKind, "runtime_durable_timer", [String(CollectionField, RuntimeCollectionProjectionLength), String(WorkflowExecutionIdField, RuntimeExecutionIdProjectionLength), String(DurableTimerIdField, RuntimeExecutionIdProjectionLength), DateTime(DurableTimerDueTimeField), String(DurableTimerClaimOrderKeyField, DurableTimerClaimOrderKeyProjectionLength)], [Index(ByCollectionIndex, CollectionField), Index(ByWorkflowExecutionIndex, WorkflowExecutionIdField), Index("by-due-time", DurableTimerDueTimeField), IncludedIndex("by-due-time-and-timer-id", DurableTimerDueTimeField, DurableTimerIdField), Index(ByTimerIdIndex, DurableTimerIdField), IncludedIndex("by-workflow-execution-and-timer-id", WorkflowExecutionIdField, DurableTimerIdField), IncludedIndex("by-claim-order", DurableTimerClaimOrderKeyField)]),
        Unit(WorkflowTriggerBindingDocumentKind, "runtime_workflow_trigger_binding", [String(StimulusHashField, StimulusHashProjectionLength), String(StimulusTypeField, WorkflowTriggerBindingStimulusTypeProjectionLength), String(StimulusLookupKeyField, BookmarkStimulusLookupKeyProjectionLength), String(StimulusTypeLookupKeyField, BookmarkStimulusLookupKeyProjectionLength), Boolean(WorkflowTriggerBindingIsActiveField), String(TriggerBindingIdField, RuntimeExecutionIdProjectionLength), String(ArtifactIdField, RuntimeExecutionIdProjectionLength), String(PublicationIdField, RuntimeExecutionIdProjectionLength)], [Index(WorkflowTriggerBindingByActive, WorkflowTriggerBindingIsActiveField), Index(ByArtifactIndex, ArtifactIdField), IncludedIndex("by-artifact-and-trigger-binding-id", ArtifactIdField, TriggerBindingIdField), Index(ByPublicationIndex, PublicationIdField), IncludedIndex("by-publication-and-trigger-binding-id", PublicationIdField, TriggerBindingIdField), Index(ByStimulusIndex, StimulusHashField), IncludedIndex("by-stimulus-and-type", StimulusLookupKeyField, WorkflowTriggerBindingIsActiveField, TriggerBindingIdField), Index(ByStimulusTypeIndex, StimulusTypeField), IncludedIndex("by-stimulus-type-and-active", StimulusTypeLookupKeyField, WorkflowTriggerBindingIsActiveField, TriggerBindingIdField), UniqueIndex("by-trigger-binding-id", TriggerBindingIdField)]),
        Unit(RecurringTriggerScheduleDocumentKind, "runtime_recurring_trigger_schedule", [String(CollectionField, 128), String(ArtifactIdField, 128), String(RecurringTriggerSchedulePublicationIdField, 128), String(RecurringTriggerScheduleIdField, 128), Boolean(RecurringTriggerScheduleIsActiveField), DateTime(RecurringTriggerScheduleNextOccurrenceField)], [IncludedIndex("by-active-next-occurrence-and-schedule-id", RecurringTriggerScheduleIsActiveField, RecurringTriggerScheduleNextOccurrenceField, RecurringTriggerScheduleIdField), Index(ByArtifactIndex, ArtifactIdField), IncludedIndex("by-artifact-and-schedule-id", ArtifactIdField, RecurringTriggerScheduleIdField), Index(ByCollectionIndex, CollectionField), Index("by-next-occurrence", RecurringTriggerScheduleNextOccurrenceField), Index(ByPublicationIndex, RecurringTriggerSchedulePublicationIdField), IncludedIndex("by-publication-and-schedule-id", RecurringTriggerSchedulePublicationIdField, RecurringTriggerScheduleIdField), Index(ByRecurringScheduleActiveIndex, RecurringTriggerScheduleIsActiveField), Index(ByRecurringScheduleIdIndex, RecurringTriggerScheduleIdField)]),
        Unit(PublicationProjectionStateDocumentKind, "runtime_publication_projection_state", [], [])
    ];

    private static StorageUnit Unit(
        string id,
        string name,
        IReadOnlyList<ColumnSpec> columns,
        IReadOnlyList<IndexSpec> indexes)
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

        var built = declaration.Build();
        var indexPolicies = indexes.ToDictionary(index => index.Name, StringComparer.Ordinal);
        var unit = built with
        {
            SchemaVersion = StorageSchemaVersion,
            Indexes = built.Indexes
                .Select(index => indexPolicies.TryGetValue(index.Name, out var policy)
                    ? index with { IsUnique = policy.Unique, MissingValues = policy.MissingValues }
                    : index)
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

    private static ColumnSpec String(string name, int maxLength = IdMaximumLength) =>
        new(name, PortableType.String, maxLength);

    private static ColumnSpec Int64(string name) => new(name, PortableType.Int64);

    private static ColumnSpec Int32(string name) => new(name, PortableType.Int32);

    private static ColumnSpec DateTime(string name) => new(name, PortableType.DateTimeOffset);

    private static ColumnSpec Boolean(string name) => new(name, PortableType.Boolean);

    private static IndexSpec Index(string name, params string[] columns) => new(name, columns, false);

    private static IndexSpec IncludedIndex(string name, params string[] columns) =>
        new(name, columns, false, MissingValueBehavior.Included);

    private static IndexSpec UniqueIndex(string name, params string[] columns) => new(name, columns, true);

    private sealed record ColumnSpec(string Name, PortableType Type, int? MaxLength = null)
    {
        public void AddTo(StorageDeclarationBuilder declaration)
        {
            switch (Type)
            {
                case PortableType.String:
                    declaration.String(Name, MaxLength ?? IdMaximumLength);
                    break;
                case PortableType.Int64:
                    declaration.Int64(Name);
                    break;
                case PortableType.DateTimeOffset:
                    declaration.Timestamp(Name);
                    break;
                case PortableType.Boolean:
                    declaration.Boolean(Name);
                    break;
                case PortableType.Int32:
                    declaration.Int32(Name);
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
            // Build the declaration as an ordinary index first. Groundwork's builder defaults
            // unique indexes to Included missing values, while the runtime deliberately retains
            // sparse unique indexes (MissingValues.Excluded) from the legacy contract. The final
            // immutable declaration below applies the explicit uniqueness and missing-value policy
            // before running the public portability validator.
            declaration.Index(Name, Columns.ToArray());
        }
    }
}
