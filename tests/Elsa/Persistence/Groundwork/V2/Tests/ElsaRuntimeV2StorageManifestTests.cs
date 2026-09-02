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

        Assert.Equal(29, units.Count);
        Assert.Equal(
            [
                "activityExecutionHierarchy", "activityExecutionInspection", "activityExecutionState",
                "bookmarkState", "checkpointCommit", "controlPlaneState", "durableTimer", "durableValueState",
                "executableActivityTemplate", "executableActivityTemplateHashClaim", "incidentState", "operationalState",
                "postCommitOutbox", "publicationProjectionState", "recurringTriggerSchedule", "schedulerPoison",
                "schedulerState", "schedulerWorkItem", "workflowActivationSlot", "workflowAlterationJob", "workflowAlterationPlan", "workflowDispatch",
                "workflowExecutable", "workflowExecutableCoordination", "workflowExecutableSourceReference",
                "workflowExecutionState", "workflowRunHealthState", "workflowTestScope",
                "workflowTriggerBinding"
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
            ["activityExecutionHierarchy"] = ["by_execution_scope", "by_workflow_execution", "by_workflow_execution_and_hierarchy_order", "by_workflow_execution_scope_and_hierarchy_order"],
            ["activityExecutionInspection"] = ["by_workflow_execution", "by_workflow_execution_and_summary_order"],
            ["activityExecutionState"] = ["by_parent_activity_execution", "by_workflow_execution", "by_workflow_execution_and_activity_execution_id", "by_workflow_parent_and_activity_execution_id"],
            ["bookmarkState"] = ["by_stimulus", "by_stimulus_and_type_and_bookmark_identity", "by_stimulus_type", "by_stimulus_type_and_bookmark_identity", "by_workflow_execution", "by_workflow_execution_and_bookmark_id"],
            ["checkpointCommit"] = ["by_collection"],
            ["controlPlaneState"] = ["by_collection", "by_workflow_execution"],
            ["durableTimer"] = ["by_claim_order", "by_collection", "by_due_time_and_timer_id", "by_timer_id", "by_workflow_execution", "by_workflow_execution_and_timer_id"],
            ["durableValueState"] = ["by_workflow_execution", "by_workflow_execution_and_durable_value_id"],
            ["executableActivityTemplate"] = ["by_collection", "by_collection_and_document_id", "by_template_hash"],
            ["executableActivityTemplateHashClaim"] = [],
            ["incidentState"] = ["by_status_created_at_workflow_and_incident", "by_workflow_execution", "by_workflow_execution_and_incident_id", "by_workflow_execution_and_status_and_incident_id"],
            ["operationalState"] = ["by_collection", "by_collection_workflow_execution_and_operational_state_id", "by_recovery_detected", "by_recovery_detected_heartbeat_owner", "by_recovery_detected_lease_owner", "by_recovery_detected_ownerless", "by_recovery_heartbeat", "by_recovery_heartbeat_owner", "by_recovery_lease_acquisition", "by_recovery_lease_acquisition_owner", "by_recovery_lease_expiry", "by_recovery_lease_expiry_owner", "by_workflow_execution", "by_workflow_execution_and_operational_state_id"],
            ["postCommitOutbox"] = ["by_claimable_by_intent_kind_time_recorded_id", "by_claimable_by_workflow_and_intent_kind_time_recorded_id", "by_claimable_by_workflow_time_recorded_id", "by_claimable_time_recorded_id", "by_collection", "by_deliverable_by_intent_kind_time_recorded_id", "by_deliverable_by_workflow_and_intent_kind_time_recorded_id", "by_deliverable_by_workflow_time_recorded_id", "by_deliverable_time_recorded_id", "by_outbox_claimable_at", "by_outbox_deliverable_at", "by_outbox_intent_kind", "by_outbox_item_id", "by_outbox_recorded_at", "by_outbox_status", "by_workflow_execution"],
            ["publicationProjectionState"] = ["by_projection_kind_and_artifact_id"],
            ["recurringTriggerSchedule"] = ["by_activation_and_schedule_id", "by_active_next_occurrence_and_schedule_id", "by_artifact_and_schedule_id", "by_collection", "by_next_occurrence", "by_recurring_schedule_active", "by_recurring_schedule_id"],
            ["schedulerPoison"] = ["by_workflow_execution", "by_workflow_execution_and_failure_window"],
            ["schedulerState"] = ["by_collection"],
            ["schedulerWorkItem"] = ["by_scheduler_work_order", "by_workflow_execution", "by_workflow_execution_and_scheduler_recorded_at_and_order", "by_workflow_execution_and_scheduler_work_order"],
            ["workflowAlterationJob"] = ["alteration_job_checkpoint", "alteration_jobs_counts", "by_claimable_at", "by_plan_and_capture_ordinal", "by_plan_and_claimable_at", "by_plan_status_and_claimable_at", "checkpointCommitId", "status"],
            ["workflowAlterationPlan"] = ["by_collection", "by_tenant_and_idempotency_key", "by_tenant_and_status", "status", "unique_tenant_and_idempotency_key"],
            ["workflowDispatch"] = ["by_child_workflow_execution", "by_child_workflow_execution_and_status", "by_collection", "by_created_at", "by_dispatch_id", "by_parent_execution_status_scope_created_at_dispatch_id", "by_parent_workflow_execution", "by_parent_workflow_execution_and_status", "by_parent_workflow_execution_created_at_dispatch_id", "by_parent_workflow_execution_status_created_at_dispatch_id", "by_parent_workflow_execution_test_scope_created_at_dispatch_id", "by_status", "by_status_created_at_dispatch_id", "by_status_test_scope_created_at_dispatch_id", "by_test_scope", "by_test_scope_created_at_dispatch_id"],
            ["workflowExecutable"] = ["by_collection", "by_collection_and_document_id"],
            ["workflowExecutableCoordination"] = [],
            ["workflowExecutableSourceReference"] = ["by_artifact", "by_artifact_and_document_id", "by_artifact_retired_expiry_and_document_id", "by_collection", "by_collection_and_document_id", "by_collection_retired_expiry_and_document_id", "by_definition_version", "by_definition_version_and_document_id", "by_expires_at", "by_expiry_and_document_id", "by_retired", "by_retired_and_document_id", "by_scope", "by_scope_and_document_id", "by_scope_retired_expiry_and_document_id", "by_scope_retired_expiry_definition_and_document_id"],
            ["workflowExecutionState"] = ["by_alteration_capture_tenant_and_execution", "by_attention_fault_history", "by_collection_and_pinned_artifact", "by_collection_and_pinned_artifact_v2", "by_history_order"],
            ["workflowRunHealthState"] = ["by_definition_and_started_at", "by_run_kind_and_started_at", "by_run_kind_status_definition_started_at", "by_run_kind_status_started_at", "by_started_at", "by_status_and_started_at"],
            ["workflowTestScope"] = ["by_collection", "by_expires_at", "by_scope_id", "by_state_and_expires_at", "by_state_and_scope_id", "by_status"],
            ["workflowTriggerBinding"] = ["by_activation", "by_activation_and_trigger_binding_id", "by_active", "by_artifact", "by_artifact_and_trigger_binding_id", "by_stimulus", "by_stimulus_and_type", "by_stimulus_type", "by_stimulus_type_and_active", "by_trigger_binding_id"],
            ["workflowActivationSlot"] = ["by_active_activation", "by_active_activation_and_slot_id", "by_definition", "by_definition_and_slot_id"]
        };
        var actualIndexes = units.ToDictionary(
            unit => unit.Id.Value,
            unit => unit.Indexes.Select(index => index.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        Assert.Equal(expectedIndexes.Count, actualIndexes.Count);
        foreach (var (unitId, expected) in expectedIndexes)
            Assert.Equal(expected, actualIndexes[unitId]);

        var timers = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind);
        AssertColumn(timers, ElsaRuntimeV2StorageManifest.DurableTimerDueTimeField, PortableType.DateTimeOffset, null, nullable: false);
        AssertColumn(timers, ElsaRuntimeV2StorageManifest.DurableTimerIdField, PortableType.String, ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength, nullable: false);
        AssertIndex(timers, "by_claim_order", ["claimOrderKey"], included: true);
        AssertIndex(timers, "by_due_time_and_timer_id", [ElsaRuntimeV2StorageManifest.DurableTimerDueTimeField, ElsaRuntimeV2StorageManifest.DurableTimerIdField, ElsaRuntimeV2StorageManifest.IdField], included: true);
        AssertIndex(timers, "by_workflow_execution_and_timer_id", [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, ElsaRuntimeV2StorageManifest.DurableTimerIdField], included: true);

        var schedules = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleDocumentKind);
        AssertColumn(schedules, ElsaRuntimeV2StorageManifest.ArtifactIdField, PortableType.String, 128, nullable: false);
        AssertColumn(schedules, ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleActivationIdField, PortableType.String, 128, nullable: false);
        AssertColumn(schedules, ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIdField, PortableType.String, 128, nullable: false);
        AssertColumn(schedules, ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIsActiveField, PortableType.Boolean, null, nullable: false);
        AssertColumn(schedules, ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleNextOccurrenceField, PortableType.DateTimeOffset, null, nullable: false);
        AssertIndex(schedules, ElsaRuntimeV2StorageManifest.RecurringScheduleByActiveNextOccurrenceAndScheduleIdIndex, [ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIsActiveField, ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleNextOccurrenceField, ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIdField, ElsaRuntimeV2StorageManifest.IdField], included: true);
        AssertIndex(schedules, ElsaRuntimeV2StorageManifest.RecurringScheduleByActivationAndScheduleIdIndex, [ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleActivationIdField, ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIdField, ElsaRuntimeV2StorageManifest.IdField], included: true);
        AssertIndex(schedules, ElsaRuntimeV2StorageManifest.RecurringScheduleByArtifactAndScheduleIdIndex, [ElsaRuntimeV2StorageManifest.ArtifactIdField, ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIdField, ElsaRuntimeV2StorageManifest.IdField], included: true);

        var incidents = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind);
        AssertIndex(incidents, "by_workflow_execution_and_incident_id", [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, ElsaRuntimeV2StorageManifest.IncidentIdField], included: true);
        AssertIndex(incidents, "by_workflow_execution_and_status_and_incident_id", [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, ElsaRuntimeV2StorageManifest.StatusField, ElsaRuntimeV2StorageManifest.IncidentIdField], included: true);

        var triggerBindings = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingDocumentKind);
        AssertIndex(triggerBindings, "by_stimulus_and_type", ["stimulusLookupKey", "isActive", "triggerBindingId"], included: true);

        var sourceReferences = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind);
        AssertColumn(
            sourceReferences,
            ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDefinitionVersionIdField,
            PortableType.String,
            128,
            nullable: false);
        AssertIndex(
            sourceReferences,
            ElsaRuntimeV2StorageManifest.ByDefinitionVersionIndex,
            [ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDefinitionVersionIdField]);
        AssertIndex(
            sourceReferences,
            "by_definition_version_and_document_id",
            [
                ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDefinitionVersionIdField,
                ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceIdField
            ],
            included: true);

        var publicationProjectionState = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind);
        AssertIndex(
            publicationProjectionState,
            "by_projection_kind_and_artifact_id",
            [
                ElsaRuntimeV2StorageManifest.PublicationProjectionKindField,
                ElsaRuntimeV2StorageManifest.PublicationProjectionArtifactIdField,
                ElsaRuntimeV2StorageManifest.IdField
            ],
            included: true);
        Assert.True(triggerBindings.Indexes.Single(index => index.Name == "by_trigger_binding_id").IsUnique);

        var activationSlots = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDocumentKind);
        AssertColumn(activationSlots, ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDefinitionIdField, PortableType.String, 128, nullable: true);
        AssertColumn(activationSlots, ElsaRuntimeV2StorageManifest.WorkflowActivationSlotNameField, PortableType.String, 128, nullable: true);
        AssertColumn(activationSlots, ElsaRuntimeV2StorageManifest.WorkflowActivationSlotActiveActivationIdField, PortableType.String, 128, nullable: true);
        AssertIndex(activationSlots, ElsaRuntimeV2StorageManifest.WorkflowActivationSlotByDefinition, [ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDefinitionIdField]);
        AssertIndex(activationSlots, ElsaRuntimeV2StorageManifest.WorkflowActivationSlotByDefinitionAndSlotId, [ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDefinitionIdField, ElsaRuntimeV2StorageManifest.WorkflowActivationSlotNameField, ElsaRuntimeV2StorageManifest.IdField], included: true);
        AssertIndex(activationSlots, ElsaRuntimeV2StorageManifest.WorkflowActivationSlotByActiveActivation, [ElsaRuntimeV2StorageManifest.WorkflowActivationSlotActiveActivationIdField], unique: true);
        AssertIndex(activationSlots, ElsaRuntimeV2StorageManifest.WorkflowActivationSlotByActiveActivationAndSlotId, [ElsaRuntimeV2StorageManifest.WorkflowActivationSlotActiveActivationIdField, ElsaRuntimeV2StorageManifest.IdField]);
        Assert.All(activationSlots.Indexes.Where(index => index.Name.Contains("active_activation", StringComparison.Ordinal)), index => Assert.Equal(MissingValueBehavior.Excluded, index.MissingValues));

        var executionState = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind);
        AssertIndex(executionState, "by_collection_and_pinned_artifact", ["collection", "historyArtifactId", "historyArtifactTimestamp", "historyWorkflowExecutionId"]);
        AssertIndex(executionState, "by_collection_and_pinned_artifact_v2", ["collection", "historyArtifactId", "historyWorkflowExecutionId"], unique: true);
        Assert.DoesNotContain(executionState.Indexes, index => index.Name == ElsaRuntimeV2StorageManifest.ByCollectionIndex);
        AssertColumn(executionState, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryArtifactIdField, PortableType.String, 128, nullable: true);
        AssertColumn(executionState, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField, PortableType.String, 128, nullable: true);
        AssertColumn(executionState, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryAuthorityPartitionField, PortableType.String, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryAuthorityPartitionProjectionLength, nullable: true);
        AssertColumn(executionState, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryDefinitionIdField, PortableType.String, ElsaRuntimeV2StorageManifest.IdMaximumLength, nullable: true);
        AssertColumn(executionState, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryRunKindField, PortableType.Int32, null, nullable: true);
        AssertColumn(executionState, ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryCorrelationIdField, PortableType.String, ElsaRuntimeV2StorageManifest.IdMaximumLength, nullable: true);
        Assert.Equal(MissingValueBehavior.Excluded, executionState.Indexes.Single(index => index.Name == "by_collection_and_pinned_artifact_v2").MissingValues);

        var runHealth = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind);
        AssertColumn(runHealth, ElsaRuntimeV2StorageManifest.WorkflowRunHealthDefinitionIdField, PortableType.String, ElsaRuntimeV2StorageManifest.IdMaximumLength, nullable: false);
        AssertColumn(runHealth, ElsaRuntimeV2StorageManifest.WorkflowRunHealthRunKindField, PortableType.Int32, null, nullable: false);
        AssertColumn(runHealth, ElsaRuntimeV2StorageManifest.WorkflowRunHealthStartedAtField, PortableType.DateTimeOffset, null, nullable: true);
        AssertColumn(runHealth, ElsaRuntimeV2StorageManifest.WorkflowRunHealthStatusField, PortableType.Int32, null, nullable: false);
        AssertColumn(runHealth, ElsaRuntimeV2StorageManifest.WorkflowRunHealthIncidentCountField, PortableType.Int64, null, nullable: false);
        AssertColumn(runHealth, ElsaRuntimeV2StorageManifest.WorkflowRunHealthIncidentBearingCountField, PortableType.Int64, null, nullable: false);
        Assert.Equal(
            [
                ElsaRuntimeV2StorageManifest.WorkflowRunHealthStatusProfile,
                ElsaRuntimeV2StorageManifest.WorkflowRunHealthHourlyProfile,
                ElsaRuntimeV2StorageManifest.WorkflowRunHealthDailyProfile,
                ElsaRuntimeV2StorageManifest.WorkflowRunHealthRunningProfile,
                ElsaRuntimeV2StorageManifest.WorkflowRunHealthTopFailuresProfile
            ],
            runHealth.AggregationProfiles.Select(profile => profile.Name));
        Assert.All(runHealth.AggregationProfiles, profile => Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowRunHealthAggregationMaxInputRows, profile.MaxInputRows));
        Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowRunHealthStatusMaxGroups, runHealth.AggregationProfiles.Single(profile => profile.Name == ElsaRuntimeV2StorageManifest.WorkflowRunHealthStatusProfile).MaxGroups);
        Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowRunHealthBucketMaxGroups, runHealth.AggregationProfiles.Single(profile => profile.Name == ElsaRuntimeV2StorageManifest.WorkflowRunHealthHourlyProfile).MaxGroups);
        Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowRunHealthBucketMaxGroups, runHealth.AggregationProfiles.Single(profile => profile.Name == ElsaRuntimeV2StorageManifest.WorkflowRunHealthDailyProfile).MaxGroups);
        Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowRunHealthRunningMaxGroups, runHealth.AggregationProfiles.Single(profile => profile.Name == ElsaRuntimeV2StorageManifest.WorkflowRunHealthRunningProfile).MaxGroups);
        Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowRunHealthTopFailuresMaxGroups, runHealth.AggregationProfiles.Single(profile => profile.Name == ElsaRuntimeV2StorageManifest.WorkflowRunHealthTopFailuresProfile).MaxGroups);

        var hourly = runHealth.AggregationProfiles.Single(profile => profile.Name == ElsaRuntimeV2StorageManifest.WorkflowRunHealthHourlyProfile);
        var hourlyBucket = Assert.IsType<AggregationGroup.TimeBucket>(hourly.GroupByExpressions[0]);
        Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowRunHealthBucketField, hourlyBucket.Alias);
        Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowRunHealthStartedAtField, hourlyBucket.SourceColumn);
        Assert.Equal(AggregationTimeBucketKind.FixedUtc, hourlyBucket.Kind);
        Assert.Equal(TimeSpan.FromHours(1), hourlyBucket.Width);
        Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowRunHealthStatusField, hourly.GroupByExpressions[1].Alias);

        var daily = runHealth.AggregationProfiles.Single(profile => profile.Name == ElsaRuntimeV2StorageManifest.WorkflowRunHealthDailyProfile);
        var dailyBucket = Assert.IsType<AggregationGroup.TimeBucket>(daily.GroupByExpressions[0]);
        Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowRunHealthBucketField, dailyBucket.Alias);
        Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowRunHealthStartedAtField, dailyBucket.SourceColumn);
        Assert.Equal(AggregationTimeBucketKind.LocalCalendarDay, dailyBucket.Kind);
        Assert.Equal(TimeSpan.Zero, dailyBucket.Width);
        Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowRunHealthStatusField, daily.GroupByExpressions[1].Alias);

        AssertColumn(
            ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind),
            ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyField,
            PortableType.String,
            ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyProjectionLength,
            nullable: false);
        AssertColumn(
            ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind),
            ElsaRuntimeV2StorageManifest.SchedulerWorkRecordedAtField,
            PortableType.DateTimeOffset,
            null,
            nullable: false);
        AssertColumn(
            ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind),
            ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField,
            PortableType.String,
            ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength,
            nullable: false);
        var schedulerWork = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind);
        AssertIndex(schedulerWork, ElsaRuntimeV2StorageManifest.BySchedulerWorkOrderIndex, [
            ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField,
            ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyField,
            ElsaRuntimeV2StorageManifest.IdField], included: true);
        var pendingExecutions = Assert.Single(schedulerWork.Indexes, index =>
            index.Name == ElsaRuntimeV2StorageManifest.SchedulerWorkByExecutionRecordedAtAndOrderIndex);
        Assert.Equal(
            [
                new IndexColumn(ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, SortDirection.Ascending),
                new IndexColumn(ElsaRuntimeV2StorageManifest.SchedulerWorkRecordedAtField, SortDirection.Descending),
                new IndexColumn(ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyField, SortDirection.Ascending),
                new IndexColumn(ElsaRuntimeV2StorageManifest.IdField, SortDirection.Ascending)
            ],
            pendingExecutions.Columns);
        var schedulerPoison = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerPoisonDocumentKind);
        AssertColumn(schedulerPoison, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, PortableType.String, ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength);
        AssertColumn(schedulerPoison, ElsaRuntimeV2StorageManifest.SchedulerPoisonWorkItemIdField, PortableType.String, ElsaRuntimeV2StorageManifest.SchedulerPoisonWorkItemProjectionLength);
        AssertColumn(schedulerPoison, ElsaRuntimeV2StorageManifest.SchedulerPoisonFirstFailedAtField, PortableType.DateTimeOffset, null);
        AssertColumn(schedulerPoison, ElsaRuntimeV2StorageManifest.SchedulerPoisonLastFailedAtField, PortableType.DateTimeOffset, null);
        AssertIndex(schedulerPoison, "by_workflow_execution_and_failure_window", [
            ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField,
            ElsaRuntimeV2StorageManifest.SchedulerPoisonFirstFailedAtField,
            ElsaRuntimeV2StorageManifest.SchedulerPoisonLastFailedAtField,
            ElsaRuntimeV2StorageManifest.SchedulerPoisonWorkItemIdField], included: true);
        AssertColumn(
            ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind),
            ElsaRuntimeV2StorageManifest.DurableTimerClaimOrderKeyField,
            PortableType.String,
            ElsaRuntimeV2StorageManifest.DurableTimerClaimOrderKeyProjectionLength);
        var bookmark = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind);
        AssertColumn(bookmark, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, PortableType.String, ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength, nullable: false);
        AssertColumn(bookmark, ElsaRuntimeV2StorageManifest.BookmarkIdField, PortableType.String, ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength, nullable: false);
        AssertColumn(bookmark, ElsaRuntimeV2StorageManifest.StimulusHashField, PortableType.String, ElsaRuntimeV2StorageManifest.StimulusHashProjectionLength, nullable: false);
        AssertColumn(bookmark, ElsaRuntimeV2StorageManifest.StimulusTypeField, PortableType.String, ElsaRuntimeV2StorageManifest.StimulusTypeProjectionLength, nullable: false);
        AssertColumn(bookmark, ElsaRuntimeV2StorageManifest.StimulusLookupKeyField, PortableType.String, ElsaRuntimeV2StorageManifest.BookmarkStimulusLookupKeyProjectionLength, nullable: false);
        AssertColumn(bookmark, ElsaRuntimeV2StorageManifest.StimulusTypeLookupKeyField, PortableType.String, ElsaRuntimeV2StorageManifest.BookmarkStimulusLookupKeyProjectionLength, nullable: false);
        AssertIndex(bookmark, ElsaRuntimeV2StorageManifest.BookmarkByStimulusAndTypeAndIdentityIndex, [ElsaRuntimeV2StorageManifest.StimulusLookupKeyField, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, ElsaRuntimeV2StorageManifest.BookmarkIdField, ElsaRuntimeV2StorageManifest.IdField], included: true);
        AssertIndex(bookmark, ElsaRuntimeV2StorageManifest.BookmarkByStimulusTypeAndIdentityIndex, [ElsaRuntimeV2StorageManifest.StimulusTypeLookupKeyField, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, ElsaRuntimeV2StorageManifest.BookmarkIdField, ElsaRuntimeV2StorageManifest.IdField], included: true);
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
    public void Every_runtime_v2_column_key_and_index_name_is_flat_and_portable()
    {
        foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
        {
            Assert.All(unit.Columns, column => AssertFlatPhysicalName(unit.Id.Value, "column", column.Name));
            Assert.All(unit.Key.Columns, column => AssertFlatPhysicalName(unit.Id.Value, "key", column));
            Assert.All(
                unit.Indexes.SelectMany(index => index.Columns.Select(column => (Index: index.Name, Column: column.Column))),
                item => AssertFlatPhysicalName(unit.Id.Value, $"index '{item.Index}' column", item.Column));
        }
    }

    [Fact]
    public void Legacy_physicalizer_projection_and_residual_fields_are_declared_in_v2_units()
    {
        var physicalizerFields = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind] =
            [
                ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField,
                ElsaRuntimeV2StorageManifest.ParentActivityExecutionIdField,
                ElsaRuntimeV2StorageManifest.ActivityExecutionIdField,
                ElsaRuntimeV2StorageManifest.ExecutionScopeIdField,
                ElsaRuntimeV2StorageManifest.StatusField
            ],
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
                ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleActivationIdField,
                ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleNextOccurrenceField,
                ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIdField,
                ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIsActiveField
            ],
            [ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind] =
            [
                ElsaRuntimeV2StorageManifest.PublicationProjectionKindField,
                ElsaRuntimeV2StorageManifest.PublicationProjectionArtifactIdField
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
                ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyField,
                ElsaRuntimeV2StorageManifest.SchedulerWorkRecordedAtField
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
                ElsaRuntimeV2StorageManifest.ActivationIdField
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

    private static void AssertFlatPhysicalName(string unitId, string kind, string name)
    {
        Assert.DoesNotContain('.', name);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain(' ', name);
        Assert.False(string.IsNullOrWhiteSpace(name), $"{unitId} {kind} must have a physical name.");
    }
}
