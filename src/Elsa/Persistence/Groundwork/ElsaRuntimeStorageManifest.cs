using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Persistence.Groundwork;

/// <summary>
/// Provider-neutral Groundwork storage manifest describing the runtime persistence document kinds
/// backed by the Groundwork bridge. The manifest is shared by every provider; a host selects the
/// concrete provider (SQLite, SQL Server, PostgreSQL, MongoDB) without changing this description.
/// </summary>
public static class ElsaRuntimeStorageManifest
{
    // Composite-index components use explicit provider-neutral bounds. Stimulus hashes retain the legacy
    // 450-character contract while stimulus types are narrower so scoped composites fit SQL Server.
    // The trigger-binding cursor route uses a slightly tighter type bound because its certified index
    // also includes the fixed-width identity lookup tie-break.
    // These values are guarded by manifest and SQL Server route-admission tests.
    public const int RuntimeStatusProjectionLength = 32;
    public const int RuntimeExecutionIdProjectionLength = 128;
    public const int RuntimeCollectionProjectionLength = 128;
    public const int DurableTimerClaimOrderKeyProjectionLength = 84;
    public const int SchedulerWorkOrderKeyProjectionLength = 170;
    public const int BookmarkStimulusLookupKeyProjectionLength = 64;
    public const int WorkflowDispatchIdProjectionLength = 76;
    public const int PostCommitOutboxItemIdProjectionLength =
        RuntimePostCommitOutboxIdentity.MaximumProjectionLength;
    public const int StimulusHashProjectionLength = LegacyGroundworkStorageManifestPhysicalizer.LegacyStringProjectionLength;
    public const int StimulusTypeProjectionLength = 256;
    public const int WorkflowTriggerBindingStimulusTypeProjectionLength = 240;

    // FROZEN storage-manifest version. This is NOT a document migration knob: per-kind document schema versions
    // live separately in ElsaRuntimeDocumentVersions.Current, and the document parser accepts only their positive
    // integer stamps. Adding an index (like #514's by-parent-activity-execution, or the
    // pre-existing bookmarkState by-stimulus) does NOT bump this: Groundwork's added-index backfill (Condition 7)
    // triggers on the physicalized index-set change, not on this manifest version. See docs/serialization.md.
    public const string SchemaVersion = "1.0.0";

    // Shared index identities and field names. Index identities only need to be unique within a unit,
    // so the same strings are reused across units that expose the same logical access pattern.
    public const string ByWorkflowExecutionIndex = "by-workflow-execution";
    public const string ByCollectionIndex = "by-collection";
    public const string ByStimulusIndex = "by-stimulus";
    public const string ByStimulusTypeIndex = "by-stimulus-type";
    public const string ByArtifactIndex = "by-artifact";
    public const string ByTemplateHashIndex = "by-template-hash";
    public const string ByTemplateIdIndex = "by-template-id";
    public const string BySourceReferenceIdIndex = "by-source-reference-id";
    public const string ByExecutionScopeIndex = "by-execution-scope";
    public const string ByPublicationIndex = "by-publication";
    public const string ByParentActivityExecutionIndex = "by-parent-activity-execution";
    public const string ByParentWorkflowExecutionIndex = "by-parent-workflow-execution";
    public const string ByChildWorkflowExecutionIndex = "by-child-workflow-execution";
    public const string ByStatusIndex = "by-status";
    public const string ByTestScopeIndex = "by-test-scope";
    public const string ByScopeIdIndex = "by-scope-id";
    public const string ByExpiresAtIndex = "by-expires-at";
    public const string ByStateAndScopeIdIndex = "by-state-and-scope-id";
    public const string ByStateAndExpiresAtIndex = "by-state-and-expires-at";
    public const string ByParentWorkflowExecutionAndStatusIndex = "by-parent-workflow-execution-and-status";
    public const string ByChildWorkflowExecutionAndStatusIndex = "by-child-workflow-execution-and-status";
    public const string ByCreatedAtIndex = "by-created-at";
    public const string ByDispatchIdIndex = "by-dispatch-id";
    public const string ByOutboxStatusIndex = "by-outbox-status";
    public const string ByOutboxDeliverableAtIndex = "by-outbox-deliverable-at";
    public const string ByOutboxClaimableAtIndex = "by-outbox-claimable-at";
    public const string ByOutboxRecordedAtIndex = "by-outbox-recorded-at";
    public const string ByOutboxItemIdIndex = "by-outbox-item-id";
    public const string ByOutboxIntentKindIndex = "by-outbox-intent-kind";
    public const string BySchedulerWorkOrderIndex = "by-scheduler-work-order";
    public const string SchedulerWorkPendingByWorkflowOrderIndex = "by-workflow-execution-and-scheduler-work-order";
    public const string ByTimerIdIndex = "by-timer-id";
    public const string ByRecurringScheduleIdIndex = "by-recurring-schedule-id";
    public const string ByRecurringScheduleActiveIndex = "by-recurring-schedule-active";
    public const string ByStimulusAndTypeIndex = "by-stimulus-and-type";
    public const string WorkflowTriggerBindingByActive = "by-active";
    public const string ByScopeIndex = "by-scope";
    public const string ByRetiredIndex = "by-retired";
    public const string WorkflowExecutionIdField = "workflowExecutionId";
    public const string IncidentIdField = "incidentId";
    public const string CreatedAtField = "createdAt";
    public const string CollectionField = "collection";
    public const string StimulusHashField = "stimulusHash";
    public const string StimulusTypeField = "stimulusType";
    public const string ArtifactIdField = "artifactId";
    public const string TemplateHashField = "templateHash";
    public const string WorkflowExecutableArtifactIdField = "executable.identity.artifactId";
    public const string ExecutableActivityTemplateIdField = "template.templateId";
    public const string WorkflowExecutableSourceReferenceIdField = "reference.sourceReferenceId";
    public const string ExecutionScopeIdField = "executionScopeId";
    public const string PublicationIdField = "publicationId";
    public const string ListAllQuery = "list-all";
    public const string ListByWorkflowExecutionQuery = "list-by-workflow-execution";
    public const string ListPendingSchedulerWorkflowExecutionsQuery = "list-pending-scheduler-workflow-executions";
    public const string ListByArtifactQuery = "list-by-artifact";
    public const string ListByParentActivityExecutionQuery = "list-by-parent-activity-execution";
    // Nested dot-path into the persisted activity-execution document: the parent id already lives under
    // the document's "state" envelope, so indexing this path adds an index over an EXISTING serialized
    // field without changing the document shape. Groundwork index fields are dot-paths resolved by walking
    // nested JSON (relational RelationalPhysicalizationValues.TryGetPropertyPath, Mongo content.<path> BSON key).
    public const string ParentActivityExecutionIdField = "state.parentActivityExecutionId";
    public const string ActivityExecutionIdField = "state.execution.activityExecutionId";
    public const string BookmarkIdField = "bookmarkId";
    public const string BookmarkStimulusLookupKeyField = "stimulusLookupKey";
    public const string BookmarkStimulusTypeLookupKeyField = "stimulusTypeLookupKey";
    public const string WorkflowTriggerBindingStimulusLookupKeyField = "stimulusLookupKey";
    public const string WorkflowTriggerBindingStimulusTypeLookupKeyField = "stimulusTypeLookupKey";
    public const string DurableValueIdField = "durableValueId";
    public const string ParentWorkflowExecutionIdField = "parentWorkflowExecutionId";
    public const string ChildWorkflowExecutionIdField = "childWorkflowExecutionId";
    public const string StatusField = "status";
    public const string TestScopeIdField = "testScopeId";
    public const string ScopeIdField = "scopeId";
    public const string ScopeField = "scope";
    public const string ExpiresAtField = "expiresAt";
    public const string IsRetiredField = "isRetired";
    public const string StateField = "state";
    public const string WorkflowDispatchCreatedAtField = "record.createdAt";
    public const string WorkflowDispatchIdField = "record.dispatchId";
    public const string PostCommitOutboxStatusField = "item.status";
    public const string PostCommitOutboxDeliverableAtField = "deliverableAt";
    public const string PostCommitOutboxClaimableAtField = "claimableAt";
    // Retained only for test-double compatibility while older callers move to the flattened candidate fields.
    public const string PostCommitOutboxAvailableAtField = "item.availableAt";
    public const string PostCommitOutboxVisibleAfterField = "item.deliveryVisibleAfter";
    public const string PostCommitOutboxRecordedAtField = "item.recordedAt";
    public const string PostCommitOutboxItemIdField = "item.outboxItemId";
    public const string PostCommitOutboxIntentKindField = "item.intent.kind";
    public const string SchedulerWorkOrderKeyField = "orderKey";

    public const string BookmarkStateDocumentKind = "bookmarkState";

    /// <summary>Index used by <c>IBookmarkStateStore.ListAsync(workflowExecutionId)</c>.</summary>
    public const string BookmarkStateByWorkflowExecution = ByWorkflowExecutionIndex;

    /// <summary>
    /// Cross-execution index used by <c>IBookmarkStimulusIndex.ListByStimulusAsync</c> (W7, E3-5) so a
    /// single stimulus can fan in to bookmarks waiting in any workflow execution.
    /// </summary>
    public const string BookmarkStateByStimulus = ByStimulusIndex;

    /// <summary>Composite bounded route used by <c>IBookmarkStimulusIndex.ListByStimulusAsync</c>.</summary>
    public const string BookmarkStateByStimulusAndType = ByStimulusAndTypeIndex;
    public const string BookmarkStateByStimulusAndTypeAndIdentity =
        "by-stimulus-and-type-and-bookmark-identity";

    /// <summary>Bounded route used to rebuild stimulus-type bookmark indexes at startup.</summary>
    public const string BookmarkStateByStimulusType = ByStimulusTypeIndex;
    public const string BookmarkStateByStimulusTypeAndIdentity =
        "by-stimulus-type-and-bookmark-identity";

    public const string ListBookmarksByWorkflowExecutionQuery = ListByWorkflowExecutionQuery;
    public const string BookmarkStateByWorkflowAndBookmarkId =
        "by-workflow-execution-and-bookmark-id";
    public const string ListBookmarksByStimulusQuery = "list-by-stimulus";
    public const string ListBookmarksByStimulusAndTypeQuery = "list-by-stimulus-and-type";
    public const string ListBookmarksByStimulusTypeQuery = "list-by-stimulus-type";

    public const string WorkflowExecutableDocumentKind = "workflowExecutable";

    /// <summary>Index used by <c>IWorkflowExecutableStore.ListAsync()</c> to enumerate every executable.</summary>
    public const string WorkflowExecutableByCollection = ByCollectionIndex;

    /// <summary>
    /// Constant partition value stamped on every workflow executable document so the unfiltered
    /// <c>ListAsync()</c> can be served through the declared-index equality query contract that every
    /// provider supports, rather than relying on a provider-specific "scan all" capability.
    /// </summary>
    public const string WorkflowExecutableCollection = "workflowExecutable";
    public const string PageWorkflowExecutablesQuery = "page-all";
    public const string WorkflowExecutableByCollectionAndId =
        "by-collection-and-document-id";

    // Content-addressed reusable-activity execution material. The future Groundwork adapter stores a
    // thin envelope { collection, templateHash, template }; indexes therefore target the lifted flat
    // envelope fields, never the nested template payload.
    public const string ExecutableActivityTemplateDocumentKind = "executableActivityTemplate";
    public const string ExecutableActivityTemplateHashClaimDocumentKind = "executableActivityTemplateHashClaim";
    public const string ExecutableActivityTemplateCollection = "executableActivityTemplate";
    public const string ExecutableActivityTemplateByCollection = ByCollectionIndex;
    public const string ExecutableActivityTemplateByHash = ByTemplateHashIndex;
    public const string ListExecutableActivityTemplatesQuery = ListAllQuery;
    public const string FindExecutableActivityTemplateByHashQuery = "find-by-template-hash";
    public const string PageExecutableActivityTemplatesQuery = "page-all";
    public const string ExecutableActivityTemplateByCollectionAndId =
        "by-collection-and-document-id";

    // Per-publish source references into the content-addressed artifact store (ADR 0038/0039/0040). One document
    // per reference; carries source identity, scope/expiry, retirement facts and the embedded layout sidecar.
    public const string WorkflowExecutableSourceReferenceDocumentKind = "workflowExecutableSourceReference";

    /// <summary>Index used by <c>IWorkflowExecutableSourceReferenceStore.ListAsync()</c> to enumerate every reference.</summary>
    public const string WorkflowExecutableSourceReferenceByCollection = ByCollectionIndex;
    public const string ListWorkflowExecutableSourceReferencesQuery = ListAllQuery;

    /// <summary>Index used by <c>IWorkflowExecutableSourceReferenceStore.ListByArtifactAsync</c> and the GC unreferenced-artifact sweep.</summary>
    public const string WorkflowExecutableSourceReferenceByArtifact = ByArtifactIndex;
    public const string ListWorkflowExecutableSourceReferencesByArtifactQuery = ListByArtifactQuery;

    /// <summary>Constant partition value stamped on every source-reference document so the unfiltered list/expiry sweep can use a keyword equality index.</summary>
    public const string WorkflowExecutableSourceReferenceCollection = "workflowExecutableSourceReference";
    public const string WorkflowExecutableSourceReferenceByScope = ByScopeIndex;
    public const string WorkflowExecutableSourceReferenceByExpiresAt = ByExpiresAtIndex;
    public const string WorkflowExecutableSourceReferenceByRetired = ByRetiredIndex;
    public const string ListWorkflowExecutableSourceReferencesByScopeQuery = "list-by-scope";
    public const string ListExpiredWorkflowExecutableSourceReferencesQuery = "list-expired";
    public const string ListRetiredWorkflowExecutableSourceReferencesQuery = "list-retired";
    public const string PageWorkflowExecutableSourceReferencesQuery = "page-all";
    public const string PageWorkflowExecutableSourceReferencesByScopeQuery = "page-by-scope";
    public const string PageWorkflowExecutableSourceReferencesByArtifactQuery = "page-by-artifact";
    public const string FindLiveWorkflowExecutableSourceReferenceByArtifactQuery =
        "find-live-by-artifact";
    public const string PageLiveWorkflowExecutableSourceReferencesQuery = "page-live-all";
    public const string PageLiveWorkflowExecutableSourceReferencesByScopeQuery =
        "page-live-by-scope";
    public const string BatchExpiredWorkflowExecutableSourceReferencesQuery = "batch-expired";
    public const string BatchRetiredWorkflowExecutableSourceReferencesQuery = "batch-retired";
    public const string WorkflowExecutableSourceReferenceByCollectionAndId =
        "by-collection-and-document-id";
    public const string WorkflowExecutableSourceReferenceByScopeAndId =
        "by-scope-and-document-id";
    public const string WorkflowExecutableSourceReferenceByArtifactAndId =
        "by-artifact-and-document-id";
    public const string WorkflowExecutableSourceReferenceLiveByArtifactAndId =
        "by-artifact-retired-expiry-and-document-id";
    public const string WorkflowExecutableSourceReferenceLiveByCollectionAndId =
        "by-collection-retired-expiry-and-document-id";
    public const string WorkflowExecutableSourceReferenceLiveByScopeAndId =
        "by-scope-retired-expiry-and-document-id";
    public const string WorkflowExecutableSourceReferenceByExpiryAndId =
        "by-expiry-and-document-id";
    public const string WorkflowExecutableSourceReferenceByRetiredAndId =
        "by-retired-and-document-id";

    public const string ActivityExecutionStateDocumentKind = "activityExecutionState";

    /// <summary>
    /// Parent-scoped index used by <c>IActivityExecutionStateStore.ListByParentAsync</c> (#514/#413 item 3) so a
    /// composite (e.g. a Parallel fork/join) can read only the activity-execution states directly parented by it,
    /// instead of loading every activity-execution state in the workflow and filtering in memory. Keyed by the
    /// already-persisted nested <c>state.parentActivityExecutionId</c> field; the store post-filters the result by
    /// workflow execution id (parent ids are activity-execution ids, but the store honours the full (wf, parent)
    /// contract rather than relying on their global uniqueness).
    /// </summary>
    public const string ActivityExecutionStateByParent = ByParentActivityExecutionIndex;
    public const string ActivityExecutionStateByWorkflowAndActivityExecutionId =
        "by-workflow-execution-and-activity-execution-id";
    public const string ActivityExecutionStateByWorkflowParentAndActivityExecutionId =
        "by-workflow-parent-and-activity-execution-id";
    public const string PageActivityExecutionStatesByWorkflowExecutionQuery =
        "page-by-workflow-execution";
    public const string PageActivityExecutionStatesByParentQuery =
        "page-by-parent-activity-execution";
    public const string ActivityExecutionInspectionDocumentKind = "activityExecutionInspection";
    public const string ActivityExecutionInspectionOrderIndex =
        "by-workflow-execution-and-summary-order";
    public const string ActivityExecutionInspectionSummaryExecutionSequenceField =
        "summary.executionSequence";
    public const string ActivityExecutionInspectionSummaryScheduledAtField =
        "summary.scheduledAt";
    public const string ActivityExecutionInspectionSummaryActivityExecutionIdField =
        "summary.activityExecutionId";
    public const string PageActivityExecutionInspectionSummariesQuery =
        "page-summaries-by-workflow-execution";

    // Committed descendant relation projection. The future Groundwork adapter stores
    // { workflowExecutionId, executionScopeId, isScopeRoot, activityExecutionId, executionSequence, record } so
    // scope and workflow lookups stay on flat envelope fields and do not couple indexes to the read model.
    public const string ActivityExecutionHierarchyDocumentKind = "activityExecutionHierarchy";
    public const string ActivityExecutionHierarchyByWorkflowExecution = ByWorkflowExecutionIndex;
    public const string ActivityExecutionHierarchyByExecutionScope = ByExecutionScopeIndex;
    public const string ListActivityExecutionHierarchyByWorkflowExecutionQuery = ListByWorkflowExecutionQuery;
    public const string ActivityExecutionHierarchyLatestByWorkflowIndex =
        "by-workflow-execution-and-hierarchy-order";
    public const string ActivityExecutionHierarchyPageByWorkflowIndex =
        "by-workflow-execution-and-hierarchy-page";
    public const string ActivityExecutionHierarchyByScopeAndOrderIndex =
        "by-workflow-execution-scope-and-hierarchy-order";
    public const string ActivityExecutionHierarchyIsScopeRootField = "isScopeRoot";
    public const string ActivityExecutionHierarchyExecutionSequenceField = "executionSequence";
    public const string ActivityExecutionHierarchyActivityExecutionIdField = "activityExecutionId";
    public const string FindLatestActivityExecutionHierarchyByWorkflowQuery =
        "find-latest-by-workflow-execution";
    public const string PageActivityExecutionHierarchyByWorkflowQuery =
        "page-by-workflow-execution";
    public const string PageActivityExecutionHierarchyByScopeQuery =
        "page-by-workflow-execution-and-scope";
    public const string WorkflowExecutionStateDocumentKind = "workflowExecutionState";
    public const string WorkflowExecutionStateCollection = "workflowExecutionState";
    public const string ListWorkflowExecutionsQuery = ListAllQuery;
    public const string PageWorkflowExecutionsQuery = "page-history";
    public const string WorkflowExecutionHistoryOrderIndex = "by-history-order";
    public const string PageFaultedWorkflowExecutionsForAttentionQuery = "page-faulted-for-attention";
    public const string WorkflowExecutionFaultedAttentionOrderIndex = "by-attention-fault-history";
    public const string WorkflowExecutionHistorySortTicksField = "historySortTicks";
    public const string WorkflowExecutionHistoryWorkflowExecutionIdField = "historyWorkflowExecutionId";
    public const string WorkflowExecutionHistoryTenantIdField = "historyTenantId";
    public const string WorkflowExecutionHistoryDefinitionIdField = "historyDefinitionId";
    public const string WorkflowExecutionHistoryStatusField = "historyStatus";
    public const string WorkflowExecutionHistoryRunKindField = "historyRunKind";
    public const string WorkflowExecutionHistoryCorrelationIdField = "historyCorrelationId";
    public const string WorkflowExecutionHistoryArtifactIdField = "historyArtifactId";
    public const string WorkflowExecutionPinnedArtifactOrderIndex =
        "by-collection-and-pinned-artifact";
    public const string PagePinnedExecutableArtifactIdsQuery =
        "page-pinned-executable-artifact-ids";
    public const string WorkflowTestScopeDocumentKind = "workflowTestScope";
    public const string WorkflowTestScopeCollection = "workflowTestScope";
    public const string ListWorkflowTestScopesQuery = ListAllQuery;
    public const string ListWorkflowTestScopesByStateQuery = "list-by-state";
    public const string ListWorkflowTestScopesByStatePageQuery = "list-by-state-page";
    public const string ListExpiredOpenWorkflowTestScopesQuery = "list-open-by-expiry";
    public const string DurableValueStateDocumentKind = "durableValueState";
    public const string DurableValueStateByWorkflowAndValueId =
        "by-workflow-execution-and-durable-value-id";
    public const string SchedulerStateDocumentKind = "schedulerState";
    // Persisted wire identifiers — the string values predate the W14 type renames
    // (ExecutionLivenessState was OperationalState; WorkflowHoldState was ControlPlaneState).
    // Do not change the literal values: they are the durable Groundwork document-kind discriminators.
    public const string ExecutionLivenessStateDocumentKind = "operationalState";
    public const string ExecutionLivenessOperationalStateIdField = "state.operationalStateId";
    public const string ExecutionLivenessStateByWorkflowAndStateId =
        "by-workflow-execution-and-operational-state-id";
    public const string ExecutionLivenessStateByCollectionWorkflowAndStateId =
        "by-collection-workflow-execution-and-operational-state-id";
    public const string PageExecutionLivenessStatesByWorkflowExecutionQuery =
        "page-by-workflow-execution";
    public const string PageExecutionLivenessStatesQuery = "page-all";
    public const string RecoveryHasOperationalOwnerField = "hasOperationalOwner";
    public const string RecoveryInterruptionStatusField = "state.interruptedExecution.status";
    public const string RecoveryInterruptedAtField = "state.interruptedExecution.interruptedAt";
    public const string RecoveryLeaseOwnerIdField = "state.executionLease.ownerId";
    public const string RecoveryLeaseAcquiredAtField = "state.executionLease.acquiredAt";
    public const string RecoveryLeaseExpiresAtField = "state.executionLease.expiresAt";
    public const string RecoveryHeartbeatOwnerIdField = "state.heartbeat.ownerId";
    public const string RecoveryHeartbeatRecordedAtField = "state.heartbeat.recordedAt";

    public const string RecoveryDetectedIndex = "by-recovery-detected";
    public const string RecoveryDetectedLeaseOwnerIndex = "by-recovery-detected-lease-owner";
    public const string RecoveryDetectedHeartbeatOwnerIndex = "by-recovery-detected-heartbeat-owner";
    public const string RecoveryDetectedOwnerlessIndex = "by-recovery-detected-ownerless";
    public const string RecoveryLeaseExpiryIndex = "by-recovery-lease-expiry";
    public const string RecoveryLeaseExpiryOwnerIndex = "by-recovery-lease-expiry-owner";
    public const string RecoveryLeaseAcquisitionIndex = "by-recovery-lease-acquisition";
    public const string RecoveryLeaseAcquisitionOwnerIndex = "by-recovery-lease-acquisition-owner";
    public const string RecoveryHeartbeatIndex = "by-recovery-heartbeat";
    public const string RecoveryHeartbeatOwnerIndex = "by-recovery-heartbeat-owner";

    public const string ListRecoveryDetectedQuery = "list-recovery-detected";
    public const string ListRecoveryDetectedByLeaseOwnerQuery = "list-recovery-detected-by-lease-owner";
    public const string ListRecoveryDetectedByHeartbeatOwnerQuery = "list-recovery-detected-by-heartbeat-owner";
    public const string ListRecoveryDetectedOwnerlessQuery = "list-recovery-detected-ownerless";
    public const string ListRecoveryByLeaseExpiryQuery = "list-recovery-by-lease-expiry";
    public const string ListRecoveryByLeaseExpiryAndOwnerQuery = "list-recovery-by-lease-expiry-and-owner";
    public const string ListRecoveryByLeaseAcquisitionQuery = "list-recovery-by-lease-acquisition";
    public const string ListRecoveryByLeaseAcquisitionAndOwnerQuery = "list-recovery-by-lease-acquisition-and-owner";
    public const string ListRecoveryByHeartbeatQuery = "list-recovery-by-heartbeat";
    public const string ListRecoveryByHeartbeatAndOwnerQuery = "list-recovery-by-heartbeat-and-owner";
    public const string WorkflowHoldStateDocumentKind = "controlPlaneState";
    public const string IncidentStateDocumentKind = "incidentState";
    public const string IncidentAttentionStatusOrderIndex = "by-status-created-at-workflow-and-incident";
    public const string IncidentAttentionStatusIndex = "by-incident-attention-status";
    public const string IncidentAttentionCreatedAtIndex = "by-incident-attention-created-at";
    public const string IncidentAttentionIncidentIdIndex = "by-incident-id";
    public const string PageAttentionIncidentsByStatusQuery = "page-attention-by-status";

    // Durable idempotency ledger for the checkpoint writer. A marker document keyed by CommitId records
    // that a checkpoint commit has been fully applied, so an at-least-once redelivery of the same commit
    // is skipped. This survives process restarts, unlike the in-memory writer's in-process dedup set.
    public const string CheckpointCommitDocumentKind = "checkpointCommit";
    public const string CheckpointCommitByCollection = ByCollectionIndex;
    public const string CheckpointCommitCollection = "checkpointCommit";
    public const string ListCheckpointCommitsQuery = ListAllQuery;

    public const string PostCommitOutboxDocumentKind = "postCommitOutbox";
    public const string ListDeliverablePostCommitOutboxQuery = "list-deliverable";
    public const string ListDeliverablePostCommitOutboxByWorkflowQuery = "list-deliverable-by-workflow";
    public const string ListDeliverablePostCommitOutboxByIntentKindQuery = "list-deliverable-by-intent-kind";
    public const string ListDeliverablePostCommitOutboxByWorkflowAndIntentKindQuery = "list-deliverable-by-workflow-and-intent-kind";
    public const string ListClaimablePostCommitOutboxQuery = "list-claimable";
    public const string ListClaimablePostCommitOutboxByWorkflowQuery = "list-claimable-by-workflow";
    public const string ListClaimablePostCommitOutboxByIntentKindQuery = "list-claimable-by-intent-kind";
    public const string ListClaimablePostCommitOutboxByWorkflowAndIntentKindQuery = "list-claimable-by-workflow-and-intent-kind";
    // Retained only for test-double compatibility; production routes use the two provider-side candidate sets above.
    public const string ListImmediatePostCommitOutboxQuery = "list-immediate";
    public const string ListImmediatePostCommitOutboxByWorkflowQuery = "list-immediate-by-workflow";
    public const string ListImmediatePostCommitOutboxByIntentKindQuery = "list-immediate-by-intent-kind";
    public const string ListImmediatePostCommitOutboxByWorkflowAndIntentKindQuery = "list-immediate-by-workflow-and-intent-kind";
    public const string ListExpiredPostCommitOutboxClaimsQuery = "list-expired-claims";
    public const string ListExpiredPostCommitOutboxClaimsByWorkflowQuery = "list-expired-claims-by-workflow";
    public const string ListExpiredPostCommitOutboxClaimsByIntentKindQuery = "list-expired-claims-by-intent-kind";
    public const string ListExpiredPostCommitOutboxClaimsByWorkflowAndIntentKindQuery = "list-expired-claims-by-workflow-and-intent-kind";

    // Durable detached-dispatch lifecycle. The collection route supports bounded internal retention
    // enumeration; parent/child/status and their common intersections serve operational inspection.
    public const string WorkflowDispatchDocumentKind = "workflowDispatch";
    public const string WorkflowDispatchCollection = "workflowDispatch";
    public const string ListWorkflowDispatchesByParentQuery = "list-by-parent-workflow-execution";
    public const string ListWorkflowDispatchesByChildQuery = "list-by-child-workflow-execution";
    public const string ListWorkflowDispatchesByStatusQuery = "list-by-status";
    public const string ListWorkflowDispatchesByTestScopeQuery = "list-by-test-scope";
    public const string ListWorkflowDispatchesByParentAndStatusQuery = "list-by-parent-workflow-execution-and-status";
    public const string ListWorkflowDispatchesByChildAndStatusQuery = "list-by-child-workflow-execution-and-status";
    public const string ListWorkflowDispatchesByParentAndTestScopeQuery = "list-by-parent-workflow-execution-and-test-scope";
    public const string ListWorkflowDispatchesByStatusAndTestScopeQuery = "list-by-status-and-test-scope";
    public const string ListWorkflowDispatchesByParentStatusAndTestScopeQuery = "list-by-parent-workflow-execution-status-and-test-scope";
    public const string PageWorkflowDispatchesByParentQuery = "page-by-parent-workflow-execution";
    public const string PageWorkflowDispatchesByStatusQuery = "page-by-status";
    public const string PageWorkflowDispatchesByTestScopeQuery = "page-by-test-scope";
    public const string PageWorkflowDispatchesByParentAndStatusQuery = "page-by-parent-workflow-execution-and-status";
    public const string PageWorkflowDispatchesByParentAndTestScopeQuery = "page-by-parent-workflow-execution-and-test-scope";
    public const string PageWorkflowDispatchesByStatusAndTestScopeQuery = "page-by-status-and-test-scope";
    public const string PageWorkflowDispatchesByParentStatusAndTestScopeQuery = "page-by-parent-workflow-execution-status-and-test-scope";
    public const string ListWorkflowDispatchesQuery = ListAllQuery;

    // Durable scheduler work queue. Each queued work item is a document so the queue survives process
    // restarts; the by-collection partition supports the system-wide pending-executions sweep.
    public const string SchedulerWorkItemDocumentKind = "schedulerWorkItem";
    public const string SchedulerWorkByWorkflowOrderIndex = BySchedulerWorkOrderIndex;

    // Durable scheduler poison records. Each failed work item is keyed by workflow execution and work item id so
    // handler crashes survive process restarts and can be inspected/re-driven according to the runtime retry policy.
    public const string SchedulerPoisonDocumentKind = "schedulerPoison";

    // Durable timer store. Each pending timer is a document so timers survive process restarts; the
    // by-due-time route bounds the due-timer sweep by the persisted deadline.
    public const string DurableTimerDocumentKind = "durableTimer";
    public const string DurableTimerByWorkflowExecution = ByWorkflowExecutionIndex;
    public const string DurableTimerDueTimeField = "timer.dueTime";
    public const string DurableTimerIdField = "timer.timerId";
    public const string DurableTimerByDueTime = "by-due-time";
    public const string DurableTimerByDueTimeAndTimerId = "by-due-time-and-timer-id";
    public const string DurableTimerClaimOrderKeyField = "claimOrderKey";
    public const string DurableTimerByClaimOrder = "by-claim-order";
    public const string DurableTimerByWorkflowAndTimerId =
        "by-workflow-execution-and-timer-id";
    public const string ListDurableTimersByWorkflowExecutionQuery = ListByWorkflowExecutionQuery;
    public const string PageDurableTimersByWorkflowExecutionQuery = "page-by-workflow-execution";
    public const string ListDueDurableTimersQuery = "list-due";
    public const string ClaimDueDurableTimersQuery = "claim-due";

    // Durable trigger index over PUBLISHED artifacts (W7, E3-1). Each start-trigger activity in a
    // published executable becomes one document, so an external stimulus with no execution id can be
    // routed to start a new workflow instance. Indexed by stimulus hash (cross-artifact router lookup)
    // and by artifact id (replace-on-republish).
    public const string WorkflowTriggerBindingDocumentKind = "workflowTriggerBinding";
    public const string TriggerBindingIdField = "triggerBindingId";
    public const string WorkflowTriggerBindingById = "by-trigger-binding-id";
    public const string WorkflowTriggerBindingByArtifactAndId =
        "by-artifact-and-trigger-binding-id";
    public const string WorkflowTriggerBindingByPublicationAndId =
        "by-publication-and-trigger-binding-id";

    /// <summary>Durable prepared/active marker for a publication-owned Runtime serving projection.</summary>
    public const string PublicationProjectionStateDocumentKind = "publicationProjectionState";

    /// <summary>
    /// Cross-artifact index used by <c>IWorkflowTriggerBindingStore.ListByStimulusAsync</c> so a single
    /// stimulus can start instances of any workflow that triggers on it. Kept for backwards-compatible
    /// storage shape and broad hash-only diagnostics; normal routing uses the stimulus+type route below.
    /// </summary>
    public const string WorkflowTriggerBindingByStimulus = ByStimulusIndex;

    /// <summary>
    /// Composite bounded route used by stimulus dispatch to select only active, exact-type/hash bindings.
    /// </summary>
    public const string WorkflowTriggerBindingByStimulusAndType = ByStimulusAndTypeIndex;

    /// <summary>Type + active-state index used by the bounded route-table refresh traversal.</summary>
    public const string WorkflowTriggerBindingByStimulusTypeAndActive = "by-stimulus-type-and-active";

    /// <summary>Projected Boolean field used to exclude prepared or retired bindings at the persistence boundary.</summary>
    public const string WorkflowTriggerBindingIsActiveField = "isActive";

    /// <summary>Index used by <c>IWorkflowTriggerBindingStore.ListByArtifactAsync</c> and the republish replace path.</summary>
    public const string WorkflowTriggerBindingByArtifact = ByArtifactIndex;

    /// <summary>Index used by publication projection prepare, activate, and delete operations.</summary>
    public const string WorkflowTriggerBindingByPublication = ByPublicationIndex;

    public const string ListTriggerBindingsByStimulusQuery = "list-by-stimulus";
    public const string ListTriggerBindingsByStimulusAndTypeQuery = "list-by-stimulus-and-type";
    public const string ListTriggerBindingsByStimulusTypeQuery = "list-by-stimulus-type";
    public const string ListTriggerBindingsByArtifactQuery = ListByArtifactQuery;
    public const string ListTriggerBindingsByPublicationQuery = "list-by-publication";

    // Durable recurring-trigger schedule store (W16). Each Timer/Cron start trigger in a published artifact
    // becomes one schedule document with no execution id, so the recurring-trigger pump can start a NEW
    // instance on each occurrence across process restarts. The by-next-occurrence route bounds the due-schedule
    // sweep by the persisted cursor; the by-artifact index serves replace-on-republish and the by-publication
    // index serves publication projection prepare/activate/delete.
    public const string RecurringTriggerScheduleDocumentKind = "recurringTriggerSchedule";

    /// <summary>Index used by the recurring-trigger pump's due-schedule sweep (constant partition).</summary>
    public const string RecurringTriggerScheduleByCollection = ByCollectionIndex;

    /// <summary>Index used by the recurring-schedule replace-on-republish delete path.</summary>
    public const string RecurringTriggerScheduleByArtifact = ByArtifactIndex;

    /// <summary>Nested path to the publication id inside the persisted recurring-schedule envelope.</summary>
    public const string RecurringTriggerSchedulePublicationIdField = "schedule.publicationId";

    /// <summary>Index used by publication projection prepare, activate, and delete operations.</summary>
    public const string RecurringTriggerScheduleByPublication = ByPublicationIndex;

    /// <summary>Nested path to the mutable recurring fire cursor inside the persisted recurring-schedule envelope.</summary>
    public const string RecurringTriggerScheduleNextOccurrenceField = "schedule.nextOccurrence";
    public const string RecurringTriggerScheduleIdField = "schedule.scheduleId";
    public const string RecurringTriggerScheduleIsActiveField = "schedule.isActive";

    /// <summary>Date index used by the recurring-trigger pump's due-schedule sweep.</summary>
    public const string RecurringTriggerScheduleByNextOccurrence = "by-next-occurrence";
    public const string RecurringTriggerScheduleByActiveNextOccurrenceAndScheduleId =
        "by-active-next-occurrence-and-schedule-id";
    public const string RecurringTriggerScheduleByPublicationAndScheduleId =
        "by-publication-and-schedule-id";
    public const string RecurringTriggerScheduleByArtifactAndScheduleId =
        "by-artifact-and-schedule-id";

    public const string ListRecurringTriggerSchedulesByPublicationQuery = "list-by-publication";
    public const string PageRecurringTriggerSchedulesByPublicationQuery = "page-by-publication";
    public const string PageRecurringTriggerSchedulesByArtifactQuery = "page-by-artifact";
    public const string ListDueRecurringTriggerSchedulesQuery = "list-due";

    /// <summary>
    /// Creates the provider-facing runtime manifest, including every bounded composite route layered
    /// over the provider-neutral legacy declarations.
    /// </summary>
    public static StorageManifest CreatePhysicalized() =>
        ElsaGroundworkQueryRoutes.AddPhysicalRoutes(
            WorkflowExecutionHistoryStoragePhysicalizer.AddRoute(
            ExecutionLivenessStatePagingPhysicalizer.AddRoutes(
            ExecutionLivenessRecoveryStoragePhysicalizer.AddRoutes(
                PostCommitOutboxGroundworkStoragePhysicalizer.AddBoundedDeliveryRoutes(
                    TestScopeStoragePhysicalizer.AddCompositeRoutes(
                        WorkflowTriggerBindingGroundworkStoragePhysicalizer.AddCompositeRoutes(
                            BookmarkStateGroundworkStoragePhysicalizer.AddCompositeRoutes(
                                WorkflowDispatchGroundworkStoragePhysicalizer.AddCompositeRoutes(
                                    DueWorkStoragePhysicalizer.AddRoutes(
                                        SchedulerWorkStoragePhysicalizer.AddRoutes(
                                            EnableCompatibilityCollectionPaging(
                                                LegacyGroundworkStorageManifestPhysicalizer.Physicalize(Create())))))))))))));

    private static StorageManifest EnableCompatibilityCollectionPaging(StorageManifest manifest) =>
        manifest with
        {
            StorageUnits = manifest.StorageUnits.Select(unit =>
                unit.Identity.Value switch
                {
                    SchedulerStateDocumentKind => EnableCursorPaging(unit, ListAllQuery),
                    WorkflowHoldStateDocumentKind => EnableCursorPaging(
                        unit,
                        ListByWorkflowExecutionQuery,
                        ListAllQuery),
                    IncidentStateDocumentKind => EnableCursorPaging(unit, ListByWorkflowExecutionQuery),
                    SchedulerPoisonDocumentKind => EnableCursorPaging(unit, ListByWorkflowExecutionQuery),
                    WorkflowDispatchDocumentKind => EnableCursorPaging(
                        unit,
                        ListWorkflowDispatchesByParentQuery,
                        ListWorkflowDispatchesQuery),
                    _ => unit
                }).ToArray()
        };

    private static StorageUnit EnableCursorPaging(StorageUnit unit, params string[] queryIdentities)
    {
        var storage = unit.PhysicalStorage ?? throw new InvalidOperationException(
            $"Compatibility collection routes for '{unit.Identity.Value}' require physical storage.");
        var identities = queryIdentities.ToHashSet(StringComparer.Ordinal);
        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                storage.ProvisioningMode,
                storage.Policy,
                storage.LogicalIndexes,
                storage.BoundedQueries
                    .Select(query => identities.Contains(query.Identity)
                        ? WithCursorPaging(query)
                        : query)
                    .ToArray(),
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }

    private static BoundedQueryDeclaration WithCursorPaging(BoundedQueryDeclaration query)
    {
        // These cursor-paged collection reads are also counted (e.g. the workflow-instances list projects a
        // per-instance incident count via IIncidentStateStore.CountAsync over 'list-by-workflow-execution').
        // A bounded query carries only Documents by default, so BoundedStore.CountAsync would be rejected with
        // "does not declare result operation 'Count'". Declare Count alongside the existing operations, and set
        // SupportsTotalCount to keep the declaration consistent (GW-PHYSICAL-029 requires Count and total-count
        // to agree); the count resolves against the same index route the Documents read already uses.
        var resultOperations = query.ResultOperations is { Count: > 0 }
            ? new HashSet<BoundedQueryResultOperation>(query.ResultOperations)
            : new HashSet<BoundedQueryResultOperation> { BoundedQueryResultOperation.Documents };
        resultOperations.Add(BoundedQueryResultOperation.Count);

        return new(
            query.Identity,
            query.IndexIdentity,
            query.Operations,
            query.SortSupport,
            QueryPagingSupport.Cursor,
            query.ExecutionClass,
            query.SupportsDisjunction,
            supportsTotalCount: true,
            query.SortFields,
            query.PredicateFields,
            resultOperations,
            query.LatestPerKeyPath,
            query.ResidualPredicateFields);
    }

    public static StorageManifest Create() => new(
        new StorageManifestIdentity("elsa-workflows-runtime"),
        new StorageManifestOwner("elsa.workflows.runtime"),
        new StorageManifestVersion(SchemaVersion),
        [
            Unit(
                BookmarkStateDocumentKind,
                "Bookmark state",
                [
                    Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField),
                    Keyword(ByStimulusIndex, StimulusHashField),
                    Keyword(ByStimulusTypeIndex, StimulusTypeField)
                ],
                [
                    Query(ListBookmarksByWorkflowExecutionQuery, ByWorkflowExecutionIndex),
                    Query(ListBookmarksByStimulusQuery, ByStimulusIndex),
                    Query(ListBookmarksByStimulusTypeQuery, ByStimulusTypeIndex)
                ]),
            Unit(
                WorkflowExecutableDocumentKind,
                "Workflow executable",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query("list-all", ByCollectionIndex)]),
            Unit(
                ExecutableActivityTemplateDocumentKind,
                "Executable activity template",
                [
                    Keyword(ByCollectionIndex, CollectionField),
                    Keyword(ByTemplateHashIndex, TemplateHashField, isUnique: true)
                ],
                [
                    Query("list-all", ByCollectionIndex),
                    Query("find-by-template-hash", ByTemplateHashIndex)
                ]),
            Unit(
                ExecutableActivityTemplateHashClaimDocumentKind,
                "Executable activity template hash claim",
                [],
                []),
            Unit(
                WorkflowExecutableSourceReferenceDocumentKind,
                "Workflow executable source reference",
                [
                    Keyword(ByCollectionIndex, CollectionField),
                    Keyword(ByArtifactIndex, ArtifactIdField),
                    Keyword(WorkflowExecutableSourceReferenceByScope, ScopeField),
                    DateTime(WorkflowExecutableSourceReferenceByExpiresAt, ExpiresAtField),
                    Keyword(WorkflowExecutableSourceReferenceByRetired, IsRetiredField)
                ],
                [
                    Query("list-all", ByCollectionIndex),
                    Query("list-by-artifact", ByArtifactIndex),
                    Query(ListWorkflowExecutableSourceReferencesByScopeQuery, WorkflowExecutableSourceReferenceByScope),
                    Query(
                        ListExpiredWorkflowExecutableSourceReferencesQuery,
                        WorkflowExecutableSourceReferenceByExpiresAt,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual },
                        QuerySortSupport.Ascending),
                    Query(ListRetiredWorkflowExecutableSourceReferencesQuery, WorkflowExecutableSourceReferenceByRetired)
                ]),
            Unit(
                ActivityExecutionStateDocumentKind,
                "Activity execution state",
                [
                    Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField),
                    Keyword(ByParentActivityExecutionIndex, ParentActivityExecutionIdField)
                ],
                [
                    Query("list-by-workflow-execution", ByWorkflowExecutionIndex),
                    Query("list-by-parent-activity-execution", ByParentActivityExecutionIndex)
                ]),
            Unit(
                ActivityExecutionInspectionDocumentKind,
                "Activity execution inspection projection",
                [Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField)],
                [Query("list-by-workflow-execution", ByWorkflowExecutionIndex)]),
            Unit(
                ActivityExecutionHierarchyDocumentKind,
                "Activity execution hierarchy projection",
                [
                    Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField),
                    Keyword(ByExecutionScopeIndex, ExecutionScopeIdField)
                ],
                [
                    Query("list-by-workflow-execution", ByWorkflowExecutionIndex),
                    Query("list-by-execution-scope", ByExecutionScopeIndex)
                ]),
            Unit(
                WorkflowExecutionStateDocumentKind,
                "Workflow execution state",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query("list-all", ByCollectionIndex)]),
            Unit(
                WorkflowTestScopeDocumentKind,
                "Workflow test scope",
                [
                    Keyword(ByCollectionIndex, CollectionField),
                    Keyword(ByStatusIndex, StateField),
                    Keyword(ByScopeIdIndex, ScopeIdField),
                    DateTime(ByExpiresAtIndex, ExpiresAtField)
                ],
                [
                    Query(ListWorkflowTestScopesQuery, ByCollectionIndex),
                    Query(ListWorkflowTestScopesByStateQuery, ByStatusIndex)
                ]),
            Unit(
                DurableValueStateDocumentKind,
                "Durable value state",
                [Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField)],
                [Query("list-by-workflow-execution", ByWorkflowExecutionIndex)]),
            Unit(
                SchedulerStateDocumentKind,
                "Scheduler state",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query("list-all", ByCollectionIndex)]),
            Unit(
                ExecutionLivenessStateDocumentKind,
                "Operational state",
                [
                    Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField),
                    Keyword(ByCollectionIndex, CollectionField)
                ],
                [
                    Query("list-by-workflow-execution", ByWorkflowExecutionIndex),
                    Query("list-all", ByCollectionIndex)
                ]),
            Unit(
                WorkflowHoldStateDocumentKind,
                "Control plane state",
                [
                    Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField),
                    Keyword(ByCollectionIndex, CollectionField)
                ],
                [
                    Query("list-by-workflow-execution", ByWorkflowExecutionIndex),
                    Query("list-all", ByCollectionIndex)
                ]),
            Unit(
                IncidentStateDocumentKind,
                "Incident state",
                [Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField)],
                [Query("list-by-workflow-execution", ByWorkflowExecutionIndex)]),
            Unit(
                CheckpointCommitDocumentKind,
                "Checkpoint commit ledger",
                [Keyword(CheckpointCommitByCollection, CollectionField)],
                [Query(ListCheckpointCommitsQuery, CheckpointCommitByCollection)]),
            Unit(
                PostCommitOutboxDocumentKind,
                "Post-commit outbox",
                [
                    Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField),
                    Keyword(ByCollectionIndex, CollectionField),
                    Number(ByOutboxStatusIndex, PostCommitOutboxStatusField),
                    DateTime(ByOutboxDeliverableAtIndex, PostCommitOutboxDeliverableAtField),
                    DateTime(ByOutboxClaimableAtIndex, PostCommitOutboxClaimableAtField),
                    DateTime(ByOutboxRecordedAtIndex, PostCommitOutboxRecordedAtField),
                    Keyword(ByOutboxItemIdIndex, PostCommitOutboxItemIdField),
                    Keyword(ByOutboxIntentKindIndex, PostCommitOutboxIntentKindField)
                ],
                [
                    Query("list-by-workflow-execution", ByWorkflowExecutionIndex),
                    Query("list-all", ByCollectionIndex)
                ]),
            Unit(
                WorkflowDispatchDocumentKind,
                "Workflow dispatch",
                [
                    Keyword(ByCollectionIndex, CollectionField),
                    Keyword(ByParentWorkflowExecutionIndex, ParentWorkflowExecutionIdField),
                    Keyword(ByChildWorkflowExecutionIndex, ChildWorkflowExecutionIdField),
                    Keyword(ByStatusIndex, StatusField),
                    Keyword(ByTestScopeIndex, TestScopeIdField),
                    DateTime(ByCreatedAtIndex, WorkflowDispatchCreatedAtField),
                    Keyword(ByDispatchIdIndex, WorkflowDispatchIdField)
                ],
                [
                    Query(ListWorkflowDispatchesQuery, ByCollectionIndex),
                    Query(ListWorkflowDispatchesByParentQuery, ByParentWorkflowExecutionIndex),
                    Query(ListWorkflowDispatchesByChildQuery, ByChildWorkflowExecutionIndex),
                    Query(ListWorkflowDispatchesByStatusQuery, ByStatusIndex),
                    Query(ListWorkflowDispatchesByTestScopeQuery, ByTestScopeIndex)
                ]),
            Unit(
                SchedulerWorkItemDocumentKind,
                "Scheduler work queue item",
                [
                    new IndexDeclaration(
                        SchedulerWorkByWorkflowOrderIndex,
                        [new IndexField(SchedulerWorkOrderKeyField, IndexValueKind.Keyword)],
                        IndexValueKind.Keyword,
                        false,
                        true,
                        MissingValueBehavior.Excluded,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.StartsWith },
                        IndexPhysicalizationPolicy.Optimized),
                    new IndexDeclaration(
                        ByWorkflowExecutionIndex,
                        [new IndexField(WorkflowExecutionIdField, IndexValueKind.Keyword)],
                        IndexValueKind.Keyword,
                        false,
                        true,
                        MissingValueBehavior.Excluded,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.StartsWith },
                        IndexPhysicalizationPolicy.Optimized),
                    Keyword(ByCollectionIndex, CollectionField)
                ],
                [
                    Query(
                        ListByWorkflowExecutionQuery,
                        SchedulerWorkByWorkflowOrderIndex,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.StartsWith },
                        QuerySortSupport.Ascending,
                        QueryPagingSupport.Cursor),
                    Query(
                        ListPendingSchedulerWorkflowExecutionsQuery,
                        ByWorkflowExecutionIndex,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.StartsWith },
                        QuerySortSupport.Ascending,
                        QueryPagingSupport.Cursor),
                    Query("list-all", ByCollectionIndex)
                ]),
            Unit(
                SchedulerPoisonDocumentKind,
                "Scheduler poison record",
                [Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField)],
                [Query("list-by-workflow-execution", ByWorkflowExecutionIndex)]),
            Unit(
                DurableTimerDocumentKind,
                "Durable timer",
                [
                    Keyword(ByCollectionIndex, CollectionField),
                    Keyword(DurableTimerByWorkflowExecution, WorkflowExecutionIdField),
                    Keyword(ByTimerIdIndex, DurableTimerIdField),
                    DateTime(DurableTimerByDueTime, DurableTimerDueTimeField),
                    new IndexDeclaration(
                        DurableTimerByClaimOrder,
                        [new IndexField(DurableTimerClaimOrderKeyField, IndexValueKind.Keyword)],
                        IndexValueKind.Keyword,
                        false,
                        true,
                        MissingValueBehavior.Excluded,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual },
                        IndexPhysicalizationPolicy.Optimized)
                ],
                [
                    Query("list-all", ByCollectionIndex),
                    Query(ListDurableTimersByWorkflowExecutionQuery, DurableTimerByWorkflowExecution),
                    Query(
                        ListDueDurableTimersQuery,
                        DurableTimerByDueTime,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual },
                        QuerySortSupport.Ascending),
                    Query(
                        ClaimDueDurableTimersQuery,
                        DurableTimerByClaimOrder,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual },
                        QuerySortSupport.Ascending)
                ]),
            Unit(
                WorkflowTriggerBindingDocumentKind,
                "Workflow trigger binding",
                [
                    Keyword(ByStimulusIndex, StimulusHashField),
                    Keyword(ByStimulusTypeIndex, StimulusTypeField),
                    Boolean(WorkflowTriggerBindingByActive, WorkflowTriggerBindingIsActiveField),
                    Keyword(WorkflowTriggerBindingById, TriggerBindingIdField, isUnique: true),
                    Keyword(ByArtifactIndex, ArtifactIdField),
                    Keyword(ByPublicationIndex, PublicationIdField)
                ],
                [
                    Query(ListTriggerBindingsByStimulusQuery, ByStimulusIndex),
                    Query(ListTriggerBindingsByStimulusTypeQuery, ByStimulusTypeIndex),
                    Query(ListTriggerBindingsByArtifactQuery, ByArtifactIndex),
                    Query(ListTriggerBindingsByPublicationQuery, ByPublicationIndex)
                ]),
            Unit(
                RecurringTriggerScheduleDocumentKind,
                "Recurring trigger schedule",
                [
                    Keyword(ByCollectionIndex, CollectionField),
                    Keyword(ByArtifactIndex, ArtifactIdField),
                    Keyword(ByPublicationIndex, RecurringTriggerSchedulePublicationIdField),
                    Keyword(ByRecurringScheduleIdIndex, RecurringTriggerScheduleIdField),
                    Boolean(ByRecurringScheduleActiveIndex, RecurringTriggerScheduleIsActiveField),
                    DateTime(RecurringTriggerScheduleByNextOccurrence, RecurringTriggerScheduleNextOccurrenceField)
                ],
                [
                    Query("list-all", ByCollectionIndex),
                    Query("list-by-artifact", ByArtifactIndex),
                    Query(ListRecurringTriggerSchedulesByPublicationQuery, ByPublicationIndex),
                    Query(
                        ListDueRecurringTriggerSchedulesQuery,
                        RecurringTriggerScheduleByNextOccurrence,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual },
                        QuerySortSupport.Ascending)
                ]),
            Unit(
                PublicationProjectionStateDocumentKind,
                "Publication projection state",
                [],
                [])
        ],
        new HashSet<string> { "schema-history", "optimistic-concurrency" },
        []);

    internal static ProjectedColumnDefinition[] BoundStimulusProjectionColumns(
        IEnumerable<ProjectedColumnDefinition> columns,
        int stimulusTypeLength = StimulusTypeProjectionLength) =>
        columns.Select(column => column.LogicalName switch
        {
            ByStimulusIndex => column with { Length = StimulusHashProjectionLength },
            ByStimulusTypeIndex => column with { Length = stimulusTypeLength },
            WorkflowTriggerBindingById => column with { Length = WorkflowTriggerBinding.MaximumIdLength },
            _ => column
        }).ToArray();

    private static StorageUnit Unit(
        string documentKind,
        string label,
        IndexDeclaration[] indexes,
        PortableQueryDeclaration[] queries) => new(
        new StorageUnitIdentity(documentKind),
        label,
        StorageIntent.PortableDocument(),
        LifecyclePolicy.Mutable,
        IdentityPolicy.StringId(),
        TenancyPolicy.Scoped,
        ConcurrencyPolicy.Optimistic(),
        SerializationPolicy.Json(),
        indexes,
        queries,
        PhysicalizationPolicy.Portable);

    private static PortableQueryDeclaration Query(
        string name,
        string indexName,
        IReadOnlySet<PortableQueryOperation>? operations = null,
        QuerySortSupport sortSupport = QuerySortSupport.None,
        QueryPagingSupport pagingSupport = QueryPagingSupport.Offset) => new(
        name,
        indexName,
        operations ?? new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        sortSupport,
        pagingSupport);

    private static IndexDeclaration Keyword(string identity, string field, bool isUnique = false) => new(
        identity,
        [new IndexField(field)],
        IndexValueKind.Keyword,
        isUnique,
        true,
        MissingValueBehavior.Excluded,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        IndexPhysicalizationPolicy.Optimized);

    private static IndexDeclaration Number(string identity, string field) => new(
        identity,
        [new IndexField(field)],
        IndexValueKind.Number,
        false,
        true,
        MissingValueBehavior.Excluded,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        IndexPhysicalizationPolicy.Optimized);

    private static IndexDeclaration Boolean(string identity, string field) => new(
        identity,
        [new IndexField(field)],
        IndexValueKind.Boolean,
        false,
        true,
        MissingValueBehavior.Excluded,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        IndexPhysicalizationPolicy.Optimized);

    private static IndexDeclaration DateTime(string identity, string field) => new(
        identity,
        [new IndexField(field)],
        IndexValueKind.DateTime,
        false,
        true,
        MissingValueBehavior.Excluded,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal, PortableQueryOperation.LessThanOrEqual },
        IndexPhysicalizationPolicy.Optimized);

}
