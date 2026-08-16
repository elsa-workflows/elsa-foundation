using Elsa.Persistence.Groundwork.Runtime;
using Groundwork.Kernel;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Tests;

public sealed class ElsaRuntimeV2StorageManifestTests
{
    [Fact]
    public void Fresh_catalog_preserves_runtime_unit_and_index_contract()
    {
        var units = ElsaRuntimeV2StorageManifest.CreateUnits();

        Assert.Equal(27, units.Count);
        Assert.Equal(
            [
                "activityExecutionHierarchy", "activityExecutionInspection", "activityExecutionState",
                "bookmarkState", "checkpointCommit", "controlPlaneState", "durableTimer", "durableValueState",
                "executableActivityTemplate", "executableActivityTemplateHashClaim", "incidentState", "operationalState",
                "postCommitOutbox", "publicationProjectionState", "recurringTriggerSchedule", "schedulerPoison",
                "schedulerState", "schedulerWorkItem", "workflowAlterationJob", "workflowAlterationPlan", "workflowDispatch",
                "workflowExecutable", "workflowExecutableCoordination", "workflowExecutableSourceReference",
                "workflowExecutionState", "workflowTestScope", "workflowTriggerBinding"
            ],
            units.Select(unit => unit.Id.Value).OrderBy(id => id, StringComparer.Ordinal));

        Assert.All(units, unit =>
        {
            Assert.Equal(ScopePolicy.Scoped, unit.Scope);
            Assert.True(unit.Concurrency.IsOptimistic);
            Assert.Equal(1, unit.SchemaVersion);
            Assert.Equal(
                [ElsaRuntimeV2StorageManifest.IdField],
                unit.Key.Columns);
            Assert.Equal(
                PortableType.String,
                unit.Columns.Single(column => column.Name == ElsaRuntimeV2StorageManifest.IdField).Type);
            Assert.Equal(
                PortableType.String,
                unit.Columns.Single(column => column.Name == ElsaRuntimeV2StorageManifest.SchemaVersionField).Type);
            Assert.Equal(
                PortableType.Json,
                unit.Columns.Single(column => column.Name == ElsaRuntimeV2StorageManifest.ContentField).Type);
        });

        var expectedIndexes = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["activityExecutionHierarchy"] = ["by-execution-scope", "by-workflow-execution", "by-workflow-execution-and-hierarchy-order", "by-workflow-execution-scope-and-hierarchy-order"],
            ["activityExecutionInspection"] = ["by-workflow-execution", "by-workflow-execution-and-summary-order"],
            ["activityExecutionState"] = ["by-parent-activity-execution", "by-workflow-execution", "by-workflow-execution-and-activity-execution-id", "by-workflow-parent-and-activity-execution-id"],
            ["bookmarkState"] = ["by-stimulus", "by-stimulus-and-type-and-bookmark-identity", "by-stimulus-type", "by-stimulus-type-and-bookmark-identity", "by-workflow-execution", "by-workflow-execution-and-bookmark-id"],
            ["checkpointCommit"] = ["by-collection"],
            ["controlPlaneState"] = ["by-collection", "by-workflow-execution"],
            ["durableTimer"] = ["by-claim-order", "by-collection", "by-due-time", "by-due-time-and-timer-id", "by-timer-id", "by-workflow-execution", "by-workflow-execution-and-timer-id"],
            ["durableValueState"] = ["by-workflow-execution", "by-workflow-execution-and-durable-value-id"],
            ["executableActivityTemplate"] = ["by-collection", "by-collection-and-document-id", "by-template-hash"],
            ["executableActivityTemplateHashClaim"] = [],
            ["incidentState"] = ["by-status-created-at-workflow-and-incident", "by-workflow-execution"],
            ["operationalState"] = ["by-collection", "by-collection-workflow-execution-and-operational-state-id", "by-recovery-detected", "by-recovery-detected-heartbeat-owner", "by-recovery-detected-lease-owner", "by-recovery-detected-ownerless", "by-recovery-heartbeat", "by-recovery-heartbeat-owner", "by-recovery-lease-acquisition", "by-recovery-lease-acquisition-owner", "by-recovery-lease-expiry", "by-recovery-lease-expiry-owner", "by-workflow-execution", "by-workflow-execution-and-operational-state-id"],
            ["postCommitOutbox"] = ["by-claimable-by-intent-kind-time-recorded-id", "by-claimable-by-workflow-and-intent-kind-time-recorded-id", "by-claimable-by-workflow-time-recorded-id", "by-claimable-time-recorded-id", "by-collection", "by-deliverable-by-intent-kind-time-recorded-id", "by-deliverable-by-workflow-and-intent-kind-time-recorded-id", "by-deliverable-by-workflow-time-recorded-id", "by-deliverable-time-recorded-id", "by-outbox-claimable-at", "by-outbox-deliverable-at", "by-outbox-intent-kind", "by-outbox-item-id", "by-outbox-recorded-at", "by-outbox-status", "by-workflow-execution"],
            ["publicationProjectionState"] = [],
            ["recurringTriggerSchedule"] = ["by-active-next-occurrence-and-schedule-id", "by-artifact", "by-artifact-and-schedule-id", "by-collection", "by-next-occurrence", "by-publication", "by-publication-and-schedule-id", "by-recurring-schedule-active", "by-recurring-schedule-id"],
            ["schedulerPoison"] = ["by-workflow-execution"],
            ["schedulerState"] = ["by-collection"],
            ["schedulerWorkItem"] = ["by-scheduler-work-order", "by-workflow-execution", "by-workflow-execution-and-scheduler-work-order"],
            ["workflowAlterationJob"] = ["alteration_job_checkpoint", "alteration_jobs_counts", "by-claimable-at", "by-plan-and-capture-ordinal", "by-plan-and-claimable-at", "by-plan-status-and-claimable-at", "checkpointCommitId", "status"],
            ["workflowAlterationPlan"] = ["by-collection", "by-tenant-and-idempotency-key", "by-tenant-and-status", "status", "unique-tenant-and-idempotency-key"],
            ["workflowDispatch"] = ["by-child-workflow-execution", "by-child-workflow-execution-and-status", "by-collection", "by-created-at", "by-dispatch-id", "by-parent-workflow-execution", "by-parent-workflow-execution-and-status", "by-parent-workflow-execution-created-at-dispatch-id", "by-parent-workflow-execution-status-created-at-dispatch-id", "by-parent-workflow-execution-status-test-scope-created-at-dispatch-id", "by-parent-workflow-execution-test-scope-created-at-dispatch-id", "by-status", "by-status-created-at-dispatch-id", "by-status-test-scope-created-at-dispatch-id", "by-test-scope", "by-test-scope-created-at-dispatch-id"],
            ["workflowExecutable"] = ["by-collection", "by-collection-and-document-id"],
            ["workflowExecutableCoordination"] = [],
            ["workflowExecutableSourceReference"] = ["by-artifact", "by-artifact-and-document-id", "by-artifact-retired-expiry-and-document-id", "by-collection", "by-collection-and-document-id", "by-collection-retired-expiry-and-document-id", "by-expires-at", "by-expiry-and-document-id", "by-retired", "by-retired-and-document-id", "by-scope", "by-scope-and-document-id", "by-scope-retired-expiry-and-document-id"],
            ["workflowExecutionState"] = ["by-alteration-capture-tenant-and-execution", "by-attention-fault-history", "by-collection-and-pinned-artifact", "by-collection-and-pinned-artifact-v2", "by-history-order"],
            ["workflowTestScope"] = ["by-collection", "by-expires-at", "by-scope-id", "by-state-and-expires-at", "by-state-and-scope-id", "by-status"],
            ["workflowTriggerBinding"] = ["by-active", "by-artifact", "by-artifact-and-trigger-binding-id", "by-publication", "by-publication-and-trigger-binding-id", "by-stimulus", "by-stimulus-and-type", "by-stimulus-type", "by-stimulus-type-and-active", "by-trigger-binding-id"]
        };
        var actualIndexes = units.ToDictionary(
            unit => unit.Id.Value,
            unit => unit.Indexes.Select(index => index.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        Assert.Equal(expectedIndexes.Count, actualIndexes.Count);
        foreach (var (unitId, expected) in expectedIndexes)
            Assert.Equal(expected, actualIndexes[unitId]);

        var timers = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind);
        AssertIndex(timers, "by-claim-order", ["claimOrderKey"], included: true);
        AssertIndex(timers, "by-due-time-and-timer-id", ["timer.dueTime", "timer.timerId"], included: true);
        AssertIndex(timers, "by-workflow-execution-and-timer-id", ["workflowExecutionId", "timer.timerId"], included: true);

        var triggerBindings = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingDocumentKind);
        AssertIndex(triggerBindings, "by-stimulus-and-type", ["stimulusLookupKey", "isActive", "triggerBindingId"], included: true);
        Assert.True(triggerBindings.Indexes.Single(index => index.Name == "by-trigger-binding-id").IsUnique);

        var executionState = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind);
        AssertIndex(executionState, "by-collection-and-pinned-artifact-v2", ["collection", "historyArtifactId", "historyWorkflowExecutionId"], unique: true);
        Assert.DoesNotContain(executionState.Indexes, index => index.Name == ElsaRuntimeV2StorageManifest.ByCollectionIndex);
        AssertColumn(executionState, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryArtifactIdField, PortableType.String, 128, nullable: true);
        AssertColumn(executionState, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField, PortableType.String, 128, nullable: true);
        AssertColumn(executionState, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryAuthorityPartitionField, PortableType.String, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryAuthorityPartitionProjectionLength, nullable: true);
        AssertColumn(executionState, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryDefinitionIdField, PortableType.String, ElsaRuntimeV2StorageManifest.IdMaximumLength, nullable: true);
        AssertColumn(executionState, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryRunKindField, PortableType.Int32, null, nullable: true);
        AssertColumn(executionState, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryCorrelationIdField, PortableType.String, ElsaRuntimeV2StorageManifest.IdMaximumLength, nullable: true);
        Assert.Equal(MissingValueBehavior.Excluded, executionState.Indexes.Single(index => index.Name == "by-collection-and-pinned-artifact-v2").MissingValues);

        AssertColumn(
            ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind),
            ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyField,
            PortableType.String,
            ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyProjectionLength);
        AssertColumn(
            ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind),
            ElsaRuntimeV2StorageManifest.DurableTimerClaimOrderKeyField,
            PortableType.String,
            ElsaRuntimeV2StorageManifest.DurableTimerClaimOrderKeyProjectionLength);
        var bookmark = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind);
        AssertColumn(bookmark, ElsaRuntimeV2StorageManifest.StimulusHashField, PortableType.String, ElsaRuntimeV2StorageManifest.StimulusHashProjectionLength);
        AssertColumn(bookmark, ElsaRuntimeV2StorageManifest.StimulusTypeField, PortableType.String, ElsaRuntimeV2StorageManifest.StimulusTypeProjectionLength);
        AssertColumn(bookmark, ElsaRuntimeV2StorageManifest.StimulusLookupKeyField, PortableType.String, ElsaRuntimeV2StorageManifest.BookmarkStimulusLookupKeyProjectionLength);
        AssertColumn(bookmark, ElsaRuntimeV2StorageManifest.StimulusTypeLookupKeyField, PortableType.String, ElsaRuntimeV2StorageManifest.BookmarkStimulusLookupKeyProjectionLength);
        AssertColumn(
            ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind),
            ElsaRuntimeV2StorageManifest.WorkflowDispatchIdField,
            PortableType.String,
            ElsaRuntimeV2StorageManifest.WorkflowDispatchIdProjectionLength);
        var triggerBindingColumns = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingDocumentKind);
        AssertColumn(triggerBindingColumns, ElsaRuntimeV2StorageManifest.StimulusLookupKeyField, PortableType.String, ElsaRuntimeV2StorageManifest.BookmarkStimulusLookupKeyProjectionLength);
        AssertColumn(triggerBindingColumns, ElsaRuntimeV2StorageManifest.StimulusTypeLookupKeyField, PortableType.String, ElsaRuntimeV2StorageManifest.BookmarkStimulusLookupKeyProjectionLength);
        AssertColumn(triggerBindingColumns, ElsaRuntimeV2StorageManifest.StimulusTypeField, PortableType.String, ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingStimulusTypeProjectionLength);
        var alterationPlan = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanDocumentKind);
        AssertColumn(alterationPlan, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanIdempotencyKeyHashField, PortableType.String, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanIdempotencyKeyHashProjectionLength);
        AssertColumn(alterationPlan, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanTenantIdempotencyKeyField, PortableType.String, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanTenantIdempotencyKeyProjectionLength);
        AssertColumn(alterationPlan, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanActiveOrderKeyField, PortableType.String, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanActiveOrderKeyProjectionLength);
        var operational = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind);
        AssertColumn(operational, ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField, PortableType.Int32, null);
        AssertColumn(operational, ElsaRuntimeV2StorageManifest.RecoveryLeaseOwnerIdField, PortableType.String, ElsaRuntimeV2StorageManifest.IdMaximumLength);
        AssertColumn(operational, ElsaRuntimeV2StorageManifest.RecoveryHeartbeatOwnerIdField, PortableType.String, ElsaRuntimeV2StorageManifest.IdMaximumLength);
        var outbox = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind);
        AssertColumn(outbox, ElsaRuntimeV2StorageManifest.PostCommitOutboxStatusField, PortableType.Int32, null);
        AssertColumn(outbox, ElsaRuntimeV2StorageManifest.PostCommitOutboxIntentKindField, PortableType.String, ElsaRuntimeV2StorageManifest.PostCommitOutboxIntentKindProjectionLength);

        foreach (var unit in units)
        {
            var portability = PortabilityValidator.Validate(unit);
            Assert.True(
                portability.IsPortable,
                $"{unit.Id.Value} has portability refusals: {string.Join("; ", portability.Refusals.Select(refusal => $"{refusal.Code} {refusal.Path}: {refusal.Message}"))}");
        }
    }

    [Fact]
    public void V2_assembly_has_no_v1_groundwork_dependency()
    {
        var references = typeof(ElsaRuntimeV2StorageManifest).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => reference.Name is "Groundwork.Core" or "Groundwork.Documents");
        Assert.Contains(references, reference => reference.Name == "Groundwork.Kernel");
        Assert.Contains(references, reference => reference.Name == "Groundwork.Query.Model");
        Assert.Contains(references, reference => reference.Name == "Groundwork.Store");
    }

    [Fact]
    public void Every_unit_has_unique_provider_index_signatures()
    {
        foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
        {
            var duplicateSignatures = unit.Indexes
                .GroupBy(index => new
                {
                    Columns = string.Join(
                        ",",
                        index.Columns.Select(column => $"{column.Column}:{column.Direction}")),
                    index.IsUnique,
                    index.MissingValues
                })
                .Where(group => group.Count() > 1)
                .ToArray();

            Assert.True(
                duplicateSignatures.Length == 0,
                $"Unit '{unit.Id.Value}' declares duplicate provider index signatures: {string.Join("; ", duplicateSignatures.Select(group => string.Join(", ", group.Select(index => index.Name))))}");
        }
    }

    [Fact]
    public void Legacy_physicalizer_projection_and_residual_fields_are_declared_in_v2_units()
    {
        var physicalizerFields = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind] =
            [
                ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField,
                ElsaRuntimeV2StorageManifest.StimulusHashField,
                ElsaRuntimeV2StorageManifest.StimulusTypeField,
                ElsaRuntimeV2StorageManifest.StimulusLookupKeyField,
                ElsaRuntimeV2StorageManifest.StimulusTypeLookupKeyField,
                ElsaRuntimeV2StorageManifest.BookmarkIdField
            ],
            [ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind] =
            [
                ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField,
                ElsaRuntimeV2StorageManifest.DurableTimerIdField,
                ElsaRuntimeV2StorageManifest.DurableTimerDueTimeField,
                ElsaRuntimeV2StorageManifest.DurableTimerClaimOrderKeyField
            ],
            [ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleDocumentKind] =
            [
                ElsaRuntimeV2StorageManifest.ArtifactIdField,
                ElsaRuntimeV2StorageManifest.RecurringTriggerSchedulePublicationIdField,
                ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleNextOccurrenceField,
                ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIdField,
                ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIsActiveField
            ],
            [ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind] =
            [
                ElsaRuntimeV2StorageManifest.CollectionField,
                ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField,
                ElsaRuntimeV2StorageManifest.ExecutionLivenessOperationalStateIdField,
                ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField,
                ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField,
                ElsaRuntimeV2StorageManifest.RecoveryLeaseOwnerIdField,
                ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField,
                ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField,
                ElsaRuntimeV2StorageManifest.RecoveryHeartbeatOwnerIdField,
                ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField,
                ElsaRuntimeV2StorageManifest.RecoveryHasOperationalOwnerField
            ],
            [ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind] =
            [
                ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField,
                ElsaRuntimeV2StorageManifest.CollectionField,
                ElsaRuntimeV2StorageManifest.PostCommitOutboxStatusField,
                ElsaRuntimeV2StorageManifest.PostCommitOutboxDeliverableAtField,
                ElsaRuntimeV2StorageManifest.PostCommitOutboxClaimableAtField,
                ElsaRuntimeV2StorageManifest.PostCommitOutboxRecordedAtField,
                ElsaRuntimeV2StorageManifest.PostCommitOutboxItemIdField,
                ElsaRuntimeV2StorageManifest.PostCommitOutboxIntentKindField
            ],
            [ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind] =
            [
                ElsaRuntimeV2StorageManifest.CollectionField,
                ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField,
                ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyField
            ],
            [ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanDocumentKind] =
            [
                ElsaRuntimeV2StorageManifest.CollectionField,
                ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanIdField,
                ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanTenantPartitionField,
                ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanIdempotencyKeyHashField,
                ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanTenantIdempotencyKeyField,
                ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanStatusField,
                ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanActiveOrderKeyField
            ],
            [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind] =
            [
                ElsaRuntimeV2StorageManifest.WorkflowAlterationJobIdField,
                ElsaRuntimeV2StorageManifest.WorkflowAlterationJobPlanIdField,
                ElsaRuntimeV2StorageManifest.WorkflowAlterationJobCaptureOrdinalField,
                ElsaRuntimeV2StorageManifest.WorkflowAlterationJobClaimableAtField,
                ElsaRuntimeV2StorageManifest.WorkflowAlterationJobStatusField,
                ElsaRuntimeV2StorageManifest.WorkflowAlterationJobCheckpointCommitIdField
            ],
            [ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind] =
            [
                ElsaRuntimeV2StorageManifest.CollectionField,
                ElsaRuntimeV2StorageManifest.StateField,
                ElsaRuntimeV2StorageManifest.ScopeIdField,
                ElsaRuntimeV2StorageManifest.ExpiresAtField
            ],
            [ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind] =
            [
                ElsaRuntimeV2StorageManifest.ParentWorkflowExecutionIdField,
                ElsaRuntimeV2StorageManifest.ChildWorkflowExecutionIdField,
                ElsaRuntimeV2StorageManifest.StatusField,
                ElsaRuntimeV2StorageManifest.TestScopeIdField,
                ElsaRuntimeV2StorageManifest.WorkflowDispatchCreatedAtField,
                ElsaRuntimeV2StorageManifest.WorkflowDispatchIdField
            ],
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind] =
            [
                ElsaRuntimeV2StorageManifest.CollectionField,
                ElsaRuntimeV2StorageManifest.WorkflowExecutionHistorySortTicksField,
                ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField,
                ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryTenantIdField,
                ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryAuthorityPartitionField,
                ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryDefinitionIdField,
                ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryStatusField,
                ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryRunKindField,
                ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryCorrelationIdField,
                ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryArtifactIdField
            ],
            [ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingDocumentKind] =
            [
                ElsaRuntimeV2StorageManifest.StimulusHashField,
                ElsaRuntimeV2StorageManifest.StimulusTypeField,
                ElsaRuntimeV2StorageManifest.StimulusLookupKeyField,
                ElsaRuntimeV2StorageManifest.StimulusTypeLookupKeyField,
                ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingIsActiveField,
                ElsaRuntimeV2StorageManifest.TriggerBindingIdField,
                ElsaRuntimeV2StorageManifest.ArtifactIdField,
                ElsaRuntimeV2StorageManifest.PublicationIdField
            ]
        };

        foreach (var (unitId, fields) in physicalizerFields)
        {
            var unit = ElsaRuntimeV2StorageManifest.Require(unitId);
            var declared = unit.Columns.Select(column => column.Name).ToHashSet(StringComparer.Ordinal);
            Assert.All(fields, field => Assert.Contains(field, declared));
        }
    }

    private static void AssertIndex(
        StorageUnit unit,
        string name,
        IReadOnlyList<string> columns,
        bool included = false,
        bool unique = false)
    {
        var index = Assert.Single(unit.Indexes, candidate => candidate.Name == name);
        Assert.Equal(columns, index.Columns.Select(column => column.Column));
        Assert.Equal(included ? MissingValueBehavior.Included : MissingValueBehavior.Excluded, index.MissingValues);
        Assert.Equal(unique, index.IsUnique);
    }

    private static void AssertColumn(
        StorageUnit unit,
        string name,
        PortableType type,
        int? maxLength,
        bool nullable = true)
    {
        var column = unit.Columns.Single(candidate => candidate.Name == name);
        Assert.Equal(type, column.Type);
        Assert.Equal(maxLength, column.MaxLength);
        Assert.Equal(nullable, column.IsNullable);
    }
}
