using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class RuntimeNativePlanContractTests
{
    [Fact]
    public void Frozen_runtime_routes_bind_the_current_units_indexes_order_and_page_limits()
    {
        var bookmarkRoutes = RuntimeNativePlanContract.ForWorkload(RuntimeBookmarkLookupWorkload.WorkloadId);
        Assert.Equal(
            ["list-by-stimulus-and-type", "list-by-stimulus-type"],
            bookmarkRoutes.Select(route => route.RouteIdentity));
        Assert.All(bookmarkRoutes, route =>
        {
            Assert.Equal("runtime_bookmark_state", route.TableName);
            Assert.Equal(["workflowExecutionId", "bookmarkId"], route.OrderColumns);
            Assert.Equal(8192, route.PhysicalCardinality);
            Assert.Equal(25, route.FiniteLimit);
        });
        Assert.Equal("by_stimulus_and_type_and_bookmark_identity", bookmarkRoutes[0].IndexName);
        Assert.Equal("stimulusLookupKey", bookmarkRoutes[0].PredicateColumn);
        Assert.Equal("by_stimulus_type_and_bookmark_identity", bookmarkRoutes[1].IndexName);
        Assert.Equal("stimulusTypeLookupKey", bookmarkRoutes[1].PredicateColumn);

        var triggerRoutes = RuntimeNativePlanContract.ForWorkload(RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId);
        Assert.Equal(
            ["list-by-stimulus-and-type", "list-by-stimulus-type", "page-live-by-scope"],
            triggerRoutes.Select(route => route.RouteIdentity));
        Assert.Equal("runtime_workflow_trigger_binding", triggerRoutes[0].TableName);
        Assert.Equal("by_stimulus_and_type", triggerRoutes[0].IndexName);
        Assert.Equal(["triggerBindingId"], triggerRoutes[0].OrderColumns);
        Assert.Equal(4608, triggerRoutes[0].PhysicalCardinality);
        Assert.Equal(20, triggerRoutes[0].FiniteLimit);
        Assert.Equal(21, triggerRoutes[0].NativeFetchLimit);
        Assert.Equal("runtime_workflow_executable_source_reference", triggerRoutes[2].TableName);
        Assert.Equal("by_scope_retired_expiry_and_document_id", triggerRoutes[2].IndexName);
        Assert.Equal(["expiresAt", "sourceReferenceId"], triggerRoutes[2].OrderColumns);
        Assert.Equal(["list-owned-live-placements"],
            RuntimeNativePlanContract.ForWorkload(DistributedPlacementTakeoverWorkload.WorkloadId)
                .Select(route => route.RouteIdentity));

        var placement = RuntimeNativePlanContract.For(
            DistributedPlacementTakeoverWorkload.WorkloadId,
            "list-owned-live-placements");
        Assert.Equal("elsa_distributed_execution_placement", placement.TableName);
        Assert.Equal("elsa_distributed_placement_owner_expiry", placement.IndexName);
        Assert.Equal(["expiresAt", "workflowExecutionId"], placement.OrderColumns);
        Assert.Equal(256, placement.PhysicalCardinality);
        Assert.Equal(64, placement.FiniteLimit);

        var dueTimer = Assert.Single(RuntimeNativePlanContract.ForWorkload(RuntimeDueTimerSelectionWorkload.WorkloadId));
        Assert.Equal("list-due", dueTimer.RouteIdentity);
        Assert.Equal("runtime_durable_timer", dueTimer.TableName);
        Assert.Equal("by_due_time_and_timer_id", dueTimer.IndexName);
        Assert.Equal(["timerDueTime", "timerId"], dueTimer.OrderColumns);
        Assert.Equal(2048, dueTimer.PhysicalCardinality);
        Assert.Equal(50, dueTimer.FiniteLimit);

        var recurring = RuntimeNativePlanContract.ForWorkload(RuntimeRecurringScheduleSelectionWorkload.WorkloadId);
        Assert.Equal(["list-due", "page-by-publication"], recurring.Select(route => route.RouteIdentity));
        Assert.Equal("by_active_next_occurrence_and_schedule_id", recurring[0].IndexName);
        Assert.Equal(["scheduleNextOccurrence", "scheduleId"], recurring[0].OrderColumns);
        Assert.Equal("by_activation_and_schedule_id", recurring[1].IndexName);
        Assert.Equal(["scheduleId"], recurring[1].OrderColumns);

        var outbox = Assert.Single(RuntimeNativePlanContract.ForWorkload(RuntimeOutboxDrainWorkload.WorkloadId));
        Assert.Equal("list-claimable", outbox.RouteIdentity);
        Assert.Equal("runtime_post_commit_outbox", outbox.TableName);
        Assert.Equal("by_claimable_time_recorded_id", outbox.IndexName);
        Assert.Equal(["claimableAt", "outboxRecordedAt", "outboxItemId", "id"], outbox.OrderColumns);
        Assert.Equal(1024, outbox.PhysicalCardinality);
        Assert.Equal(32, outbox.FiniteLimit);
        Assert.Equal(33, outbox.NativeFetchLimit);

        var queue = RuntimeNativePlanContract.ForWorkload(RuntimeQueueDrainWorkload.WorkloadId);
        Assert.Equal(
            ["list-pending-scheduler-workflow-executions", "list-by-workflow-execution"],
            queue.Select(route => route.RouteIdentity));
        Assert.Equal([RuntimeNativePlanContract.WorkflowExecutionOrdinalKeyColumn], queue[0].OrderColumns);
        Assert.Equal(["workflowExecutionId", RuntimeNativePlanContract.WorkflowExecutionOrdinalKeyColumn], queue[0].DistinctProjectionColumns);
        Assert.Equal([new RuntimeNativePredicateSpec("collection", "=")], queue[0].Predicates);
        Assert.Equal([new RuntimeNativePredicateSpec("workflowExecutionId", "=")], queue[1].Predicates);
        Assert.Equal("workflowExecutionId", queue[1].PredicateColumn);

        var commands = RuntimeNativePlanContract.ForWorkload(DistributedCommandSendLeaseAckWorkload.WorkloadId);
        Assert.Equal(
            ["lease-visible-commands-by-execution", "list-visible-command-executions", "count-pending-commands-by-execution"],
            commands.Select(route => route.RouteIdentity));
        Assert.Equal(
            [new RuntimeNativePredicateSpec("workflowExecutionId", "=")],
            commands[0].Predicates);
        Assert.Equal("elsa_distributed_command_execution_sequence", commands[0].IndexName);
        Assert.Equal("elsa_distributed_command_stream_head", commands[1].TableName);
        Assert.Equal("elsa_distributed_command_pending_head_execution", commands[1].IndexName);
        Assert.Equal(["pendingVisibleAt", "workflowExecutionId"], commands[1].OrderColumns);
        Assert.Equal([new RuntimeNativePredicateSpec("pendingVisibleAt", "<=")], commands[1].Predicates);
        Assert.False(commands[1].UsesProjectedDistinct);
        Assert.Equal("elsa_distributed_command_stream_head", commands[2].TableName);
        Assert.Equal("elsa_distributed_command_head_count_execution", commands[2].IndexName);
        Assert.Equal(["workflowExecutionId"], commands[2].OrderColumns);
        Assert.Equal(
            [new RuntimeNativePredicateSpec("workflowExecutionId", "=")],
            commands[2].Predicates);
        Assert.Equal("workflowExecutionId", commands[2].PredicateColumn);
        Assert.Equal("pendingCount", commands[2].ScalarProjectionColumn);
        Assert.Equal(1, commands[2].NativeFetchLimit);
    }

    [Fact]
    public void Sqlite_envelope_requires_the_exact_route_index_and_scope_equality()
    {
        var specification = RuntimeNativePlanContract.For(
            RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId,
            "list-by-stimulus-and-type");
        var physicalIndex = RuntimeNativePlanContract.ExpectedPhysicalIndexName("sqlite", specification);
        var route = Route(specification, physicalIndex);
        var path = WriteArtifact(new RuntimeNativePlanArtifact(
            1,
            "sqlite",
            RuntimeNativePlanContract.GroundworkAdapter,
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            physicalIndex,
            "SELECT * FROM runtime_workflow_trigger_binding WHERE __groundwork_scope = @scope AND stimulusLookupKey = @stimulus AND isActive = 1 ORDER BY triggerBindingId ASC LIMIT 21",
            $"2\t0\tSEARCH runtime_workflow_trigger_binding USING INDEX {physicalIndex} (stimulusLookupKey=? AND isActive=?)"));

        try
        {
            RuntimeNativePlanContract.ValidateEnvelope(
                RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId,
                "sqlite",
                RuntimeNativePlanContract.GroundworkAdapter,
                route,
                path);

            var wrongRouteIndex = Assert.Throws<PerformanceContractException>(() =>
                RuntimeNativePlanContract.ValidateEnvelope(
                    RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId,
                    "sqlite",
                    RuntimeNativePlanContract.GroundworkAdapter,
                    route with { IndexName = "wrong-index" },
                    path));
            Assert.Contains("index", wrongRouteIndex.Message, StringComparison.OrdinalIgnoreCase);

            Assert.Throws<PerformanceContractException>(() =>
                RuntimeNativePlanContract.ValidateEnvelope(
                    RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId,
                    "sqlite",
                    RuntimeNativePlanContract.GroundworkAdapter,
                    route with { MaterializedCandidateCount = 1 },
                    path));

            Assert.Throws<PerformanceContractException>(() =>
                RuntimeNativePlanContract.ValidateEnvelope(
                    RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId,
                    "sqlite",
                    RuntimeNativePlanContract.GroundworkAdapter,
                    route with { NativeFetchLimit = specification.FiniteLimit },
                    path));

            Assert.Throws<PerformanceContractException>(() =>
                RuntimeNativePlanContract.ValidateEnvelope(
                    RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId,
                    "sqlite",
                    RuntimeNativePlanContract.GroundworkAdapter,
                    route with { PlanClassification = "bounded-index-seek" },
                    path));

            var wrongScopePath = WriteArtifact(new RuntimeNativePlanArtifact(
                1,
                "sqlite",
                RuntimeNativePlanContract.GroundworkAdapter,
                specification.RouteIdentity,
                specification.TableName,
                specification.IndexName,
                physicalIndex,
                "SELECT * FROM runtime_workflow_trigger_binding WHERE __groundwork_scope IS NOT NULL AND stimulusLookupKey = @stimulus AND isActive = 1 ORDER BY triggerBindingId ASC LIMIT 21",
                $"2\t0\tSEARCH runtime_workflow_trigger_binding USING INDEX {physicalIndex} (stimulusLookupKey=? AND isActive=?)"));
            try
            {
                Assert.Throws<PerformanceContractException>(() =>
                    RuntimeNativePlanContract.ValidateEnvelope(
                        RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId,
                        "sqlite",
                        RuntimeNativePlanContract.GroundworkAdapter,
                        route,
                        wrongScopePath));
            }
            finally
            {
                File.Delete(wrongScopePath);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PostgreSql_zero_spill_counter_is_allowed_but_positive_spill_is_blocked()
    {
        var specification = RuntimeNativePlanContract.For(
            RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId,
            "list-by-stimulus-and-type");
        var physicalIndex = RuntimeNativePlanContract.ExpectedPhysicalIndexName("postgresql", specification);
        var route = Route(specification, physicalIndex);
        var command = "SELECT * FROM runtime_workflow_trigger_binding WHERE __groundwork_scope = $1 AND stimulusLookupKey = $2 AND isActive = $3 ORDER BY triggerBindingId ASC LIMIT 21";
        var accepted = WriteArtifact(new RuntimeNativePlanArtifact(
            1,
            "postgresql",
            RuntimeNativePlanContract.GroundworkAdapter,
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            physicalIndex,
            command,
            $"[{{\"Plan\":{{\"Node Type\":\"Index Scan\",\"Relation Name\":\"runtime_workflow_trigger_binding\",\"Index Name\":\"{physicalIndex}\",\"spillCount\":0}}}}]"));
        try
        {
            RuntimeNativePlanContract.ValidateEnvelope(
                RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId,
                "postgresql",
                RuntimeNativePlanContract.GroundworkAdapter,
                route,
                accepted);

            var rejected = WriteArtifact(new RuntimeNativePlanArtifact(
                1,
                "postgresql",
                RuntimeNativePlanContract.GroundworkAdapter,
                specification.RouteIdentity,
                specification.TableName,
                specification.IndexName,
                physicalIndex,
                command,
                $"[{{\"Plan\":{{\"Node Type\":\"Index Scan\",\"Relation Name\":\"runtime_workflow_trigger_binding\",\"Index Name\":\"{physicalIndex}\",\"spillCount\":1}}}}]"));
            try
            {
                Assert.Throws<PerformanceContractException>(() =>
                    RuntimeNativePlanContract.ValidateEnvelope(
                        RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId,
                        "postgresql",
                        RuntimeNativePlanContract.GroundworkAdapter,
                        route,
                        rejected));
            }
            finally
            {
                File.Delete(rejected);
            }
        }
        finally
        {
            File.Delete(accepted);
        }
    }

    [Fact]
    public void Groundwork_sql_null_guards_collations_and_hidden_identity_order_remain_exactly_bounded()
    {
        var specification = RuntimeNativePlanContract.For(
            RuntimeRecurringScheduleSelectionWorkload.WorkloadId,
            "list-due");
        var physicalIndex = RuntimeNativePlanContract.ExpectedPhysicalIndexName("sqlite", specification);
        var route = Route(specification, physicalIndex);
        var command = """
            SELECT * FROM runtime_recurring_trigger_schedule
            WHERE ((__groundwork_scope COLLATE GROUNDWORK_UTF16_ORDINAL IS NOT NULL
            AND __groundwork_scope COLLATE GROUNDWORK_UTF16_ORDINAL = @p0)
            AND (scheduleIsActive IS NOT NULL AND scheduleIsActive = @p1)
            AND (scheduleNextOccurrence IS NOT NULL AND scheduleNextOccurrence <= @p2))
            ORDER BY CASE WHEN scheduleNextOccurrence IS NULL THEN 0 ELSE 1 END ASC,
            scheduleNextOccurrence COLLATE GROUNDWORK_UTF16_ORDINAL ASC,
            CASE WHEN scheduleId IS NULL THEN 0 ELSE 1 END ASC,
            scheduleId COLLATE GROUNDWORK_UTF16_ORDINAL ASC,
            id COLLATE GROUNDWORK_UTF16_ORDINAL ASC LIMIT @p3;
            """;
        var plan = $"2\t0\tSEARCH runtime_recurring_trigger_schedule USING INDEX {physicalIndex} (scheduleIsActive=? AND scheduleNextOccurrence<?)";
        var accepted = WriteArtifact(new RuntimeNativePlanArtifact(
            1,
            "sqlite",
            RuntimeNativePlanContract.GroundworkAdapter,
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            physicalIndex,
            command,
            plan));

        try
        {
            RuntimeNativePlanContract.ValidateEnvelope(
                RuntimeRecurringScheduleSelectionWorkload.WorkloadId,
                "sqlite",
                RuntimeNativePlanContract.GroundworkAdapter,
                route,
                accepted);

            foreach (var invalidCommand in new[]
                     {
                         command.Replace("AND (scheduleIsActive", "AND unexpected = @p9 AND (scheduleIsActive", StringComparison.Ordinal),
                         command.Replace("id COLLATE GROUNDWORK_UTF16_ORDINAL ASC", "unexpected COLLATE GROUNDWORK_UTF16_ORDINAL ASC", StringComparison.Ordinal),
                         command.Replace("scheduleId COLLATE GROUNDWORK_UTF16_ORDINAL ASC", "scheduleId COLLATE GROUNDWORK_UTF16_ORDINAL DESC", StringComparison.Ordinal),
                         command.Replace("CASE WHEN scheduleNextOccurrence IS NULL", "CASE WHEN scheduleId IS NULL", StringComparison.Ordinal),
                         command.Replace("LIMIT @p3", "OFFSET @p3", StringComparison.Ordinal)
                     })
            {
                var rejected = WriteArtifact(new RuntimeNativePlanArtifact(
                    1,
                    "sqlite",
                    RuntimeNativePlanContract.GroundworkAdapter,
                    specification.RouteIdentity,
                    specification.TableName,
                    specification.IndexName,
                    physicalIndex,
                    invalidCommand,
                    plan));
                try
                {
                    Assert.Throws<PerformanceContractException>(() =>
                        RuntimeNativePlanContract.ValidateEnvelope(
                            RuntimeRecurringScheduleSelectionWorkload.WorkloadId,
                            "sqlite",
                            RuntimeNativePlanContract.GroundworkAdapter,
                            route,
                            rejected));
                }
                finally
                {
                    File.Delete(rejected);
                }
            }

        }
        finally
        {
            File.Delete(accepted);
        }
    }

    [Fact]
    public void Mongo_schedule_due_route_requires_lte_and_the_exact_filter_shape()
    {
        var specification = RuntimeNativePlanContract.For(
            RuntimeDueTimerSelectionWorkload.WorkloadId,
            "list-due");
        var route = Route(specification, specification.IndexName);
        var command = "{\"find\":\"runtime_durable_timer\",\"filter\":{\"__groundwork_scope\":{\"$eq\":\"scope\"},\"timerDueTime\":{\"$lte\":\"asOf\"}},\"sort\":{\"timerDueTime\":1,\"timerId\":1},\"limit\":51}";
        var plan = "{\"queryPlanner\":{\"winningPlan\":{\"stage\":\"IXSCAN\",\"indexName\":\"by_due_time_and_timer_id\"}}}";
        var accepted = WriteArtifact(new RuntimeNativePlanArtifact(
            1,
            "mongodb",
            RuntimeNativePlanContract.GroundworkAdapter,
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            specification.IndexName,
            command,
            plan));

        try
        {
            RuntimeNativePlanContract.ValidateEnvelope(
                RuntimeDueTimerSelectionWorkload.WorkloadId,
                "mongodb",
                RuntimeNativePlanContract.GroundworkAdapter,
                route,
                accepted);

            foreach (var invalidCommand in new[]
                     {
                         command.Replace("$lte", "$eq", StringComparison.Ordinal),
                         command.Replace("\"filter\":{", "\"filter\":{\"unexpected\":1,", StringComparison.Ordinal),
                         command.Replace("$lte", "$gte", StringComparison.Ordinal)
                     })
            {
                var rejected = WriteArtifact(new RuntimeNativePlanArtifact(
                    1,
                    "mongodb",
                    RuntimeNativePlanContract.GroundworkAdapter,
                    specification.RouteIdentity,
                    specification.TableName,
                    specification.IndexName,
                    specification.IndexName,
                    invalidCommand,
                    plan));
                try
                {
                    Assert.Throws<PerformanceContractException>(() =>
                        RuntimeNativePlanContract.ValidateEnvelope(
                            RuntimeDueTimerSelectionWorkload.WorkloadId,
                            "mongodb",
                            RuntimeNativePlanContract.GroundworkAdapter,
                            route,
                            rejected));
                }
                finally
                {
                    File.Delete(rejected);
                }
            }

            foreach (var invalidPlan in new[]
                     {
                         "{\"queryPlanner\":{\"winningPlan\":{\"stage\":\"IXSCAN\",\"indexName\":\"by_due_time_and_timer_id\"}},\"stages\":[{\"$sort\":{\"timerDueTime\":1}}]}",
                         "{\"queryPlanner\":{\"winningPlan\":{\"stage\":\"IXSCAN\",\"indexName\":\"by_due_time_and_timer_id\"}},\"stages\":[{\"$group\":{\"_id\":\"$workflowExecutionId\"}}]}"
                     })
            {
                var rejected = WriteArtifact(new RuntimeNativePlanArtifact(
                    1,
                    "mongodb",
                    RuntimeNativePlanContract.GroundworkAdapter,
                    specification.RouteIdentity,
                    specification.TableName,
                    specification.IndexName,
                    specification.IndexName,
                    command,
                    invalidPlan));
                try
                {
                    Assert.Throws<PerformanceContractException>(() =>
                        RuntimeNativePlanContract.ValidateEnvelope(
                            RuntimeDueTimerSelectionWorkload.WorkloadId,
                            "mongodb",
                            RuntimeNativePlanContract.GroundworkAdapter,
                            route,
                            rejected));
                }
                finally
                {
                    File.Delete(rejected);
                }
            }
        }
        finally
        {
            File.Delete(accepted);
        }
    }

    [Fact]
    public void Mongo_command_rejects_extra_filter_fields_and_non_equality_scope()
    {
        var specification = RuntimeNativePlanContract.For(
            DistributedPlacementTakeoverWorkload.WorkloadId,
            "list-owned-live-placements");
        var route = Route(specification, specification.IndexName);
        var plan = "{\"queryPlanner\":{\"winningPlan\":{\"stage\":\"IXSCAN\",\"indexName\":\"elsa_distributed_placement_owner_expiry\"}}}";
        var command = "{\"collection\":\"elsa_distributed_execution_placement\",\"limit\":65,\"sort\":{\"expiresAt\":1,\"workflowExecutionId\":1},\"filter\":{\"__groundwork_scope\":{\"$eq\":\"scope\"},\"ownerId\":{\"$eq\":\"worker-alpha\"},\"expiresAt\":{\"$gt\":\"2026-07-20T10:00:00Z\"}}}";
        var accepted = WriteArtifact(new RuntimeNativePlanArtifact(
            1,
            "mongodb",
            RuntimeNativePlanContract.GroundworkAdapter,
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            specification.IndexName,
            command,
            plan));
        try
        {
            RuntimeNativePlanContract.ValidateEnvelope(
                DistributedPlacementTakeoverWorkload.WorkloadId,
                "mongodb",
                RuntimeNativePlanContract.GroundworkAdapter,
                route,
                accepted);

            var extraField = WriteArtifact(new RuntimeNativePlanArtifact(
                1,
                "mongodb",
                RuntimeNativePlanContract.GroundworkAdapter,
                specification.RouteIdentity,
                specification.TableName,
                specification.IndexName,
                specification.IndexName,
                command.Replace("\"filter\":{", "\"filter\":{\"unexpected\":1,", StringComparison.Ordinal),
                plan));
            try
            {
                Assert.Throws<PerformanceContractException>(() =>
                    RuntimeNativePlanContract.ValidateEnvelope(
                        DistributedPlacementTakeoverWorkload.WorkloadId,
                        "mongodb",
                        RuntimeNativePlanContract.GroundworkAdapter,
                        route,
                        extraField));
            }
            finally
            {
                File.Delete(extraField);
            }
        }
        finally
        {
            File.Delete(accepted);
        }
    }

    [Fact]
    public void Projected_distinct_queue_must_bind_declared_tuple_and_order_shape()
    {
        var specification = RuntimeNativePlanContract.For(
            RuntimeQueueDrainWorkload.WorkloadId,
            "list-pending-scheduler-workflow-executions");
        var physicalIndex = RuntimeNativePlanContract.ExpectedPhysicalIndexName("sqlite", specification);
        var route = Route(specification, physicalIndex);
        var plan = $"2\t0\tSEARCH {specification.TableName} USING COVERING INDEX {physicalIndex} (__groundwork_scope=?)";
        var ordinal = RuntimeNativePlanContract.WorkflowExecutionOrdinalKeyColumn;
        var command = $"SELECT DISTINCT workflowExecutionId, {ordinal} FROM runtime_scheduler_work_item WHERE __groundwork_scope = @p0 AND collection = @p1 ORDER BY {ordinal} ASC LIMIT @p2";
        var accepted = WriteArtifact(new RuntimeNativePlanArtifact(
            1,
            "sqlite",
            RuntimeNativePlanContract.GroundworkAdapter,
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            physicalIndex,
            command,
            plan));

        try
        {
            RuntimeNativePlanContract.ValidateEnvelope(
                RuntimeQueueDrainWorkload.WorkloadId,
                "sqlite",
                RuntimeNativePlanContract.GroundworkAdapter,
                route,
                accepted);

            foreach (var invalidCommand in new[]
                     {
                         command.Replace("SELECT DISTINCT", "SELECT", StringComparison.Ordinal),
                         command.Replace($"workflowExecutionId, {ordinal}", $"{ordinal}, workflowExecutionId", StringComparison.Ordinal),
                         command.Replace($"ORDER BY {ordinal} ASC", "ORDER BY workflowExecutionId ASC", StringComparison.Ordinal),
                         command.Replace(" ORDER BY", $" GROUP BY {ordinal}, workflowExecutionId ORDER BY", StringComparison.Ordinal)
                     })
            {
                var rejected = WriteArtifact(new RuntimeNativePlanArtifact(
                    1,
                    "sqlite",
                    RuntimeNativePlanContract.GroundworkAdapter,
                    specification.RouteIdentity,
                    specification.TableName,
                    specification.IndexName,
                    physicalIndex,
                    invalidCommand,
                    plan));
                try
                {
                    Assert.Throws<PerformanceContractException>(() =>
                        RuntimeNativePlanContract.ValidateEnvelope(
                            RuntimeQueueDrainWorkload.WorkloadId,
                            "sqlite",
                            RuntimeNativePlanContract.GroundworkAdapter,
                            route,
                            rejected));
                }
                finally
                {
                    File.Delete(rejected);
                }
            }

            foreach (var invalidPlan in new[]
                     {
                         plan + "\n20\t0\t0\tUSE TEMP B-TREE FOR ORDER BY",
                         plan + "\n20\t0\t0\tSCAN (subquery-1)"
                     })
            {
                var rejected = WriteArtifact(new RuntimeNativePlanArtifact(
                    1,
                    "sqlite",
                    RuntimeNativePlanContract.GroundworkAdapter,
                    specification.RouteIdentity,
                    specification.TableName,
                    specification.IndexName,
                    physicalIndex,
                    command,
                    invalidPlan));
                try
                {
                    Assert.Throws<PerformanceContractException>(() =>
                        RuntimeNativePlanContract.ValidateEnvelope(
                            RuntimeQueueDrainWorkload.WorkloadId,
                            "sqlite",
                            RuntimeNativePlanContract.GroundworkAdapter,
                            route,
                            rejected));
                }
                finally
                {
                    File.Delete(rejected);
                }
            }
        }
        finally
        {
            File.Delete(accepted);
        }
    }

    [Fact]
    public void Scalar_count_plan_requires_a_bounded_stream_head_projection_without_materialization()
    {
        var specification = RuntimeNativePlanContract.For(
            DistributedCommandSendLeaseAckWorkload.WorkloadId,
            "count-pending-commands-by-execution");
        var physicalIndex = RuntimeNativePlanContract.ExpectedPhysicalIndexName("sqlite", specification);
        var route = Route(specification, physicalIndex);
        var accepted = WriteArtifact(new RuntimeNativePlanArtifact(
            1,
            "sqlite",
            RuntimeNativePlanContract.GroundworkAdapter,
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            physicalIndex,
            $"SELECT pendingCount FROM {specification.TableName} WHERE __groundwork_scope = @p0 AND workflowExecutionId = @p1 ORDER BY workflowExecutionId ASC, streamHeadId ASC LIMIT 1",
            $"2\t0\t0\tSEARCH {specification.TableName} USING COVERING INDEX {physicalIndex} (__groundwork_scope=? AND workflowExecutionId=?)"));

        try
        {
            RuntimeNativePlanContract.ValidateEnvelope(
                DistributedCommandSendLeaseAckWorkload.WorkloadId,
                "sqlite",
                RuntimeNativePlanContract.GroundworkAdapter,
                route,
                accepted);

            foreach (var invalid in new[]
                     {
                         (
                             Command: $"SELECT COUNT(*) FROM {specification.TableName} WHERE __groundwork_scope = @p0 AND workflowExecutionId = @p1 ORDER BY workflowExecutionId ASC, streamHeadId ASC LIMIT 1",
                             Plan: $"2\t0\t0\tSEARCH {specification.TableName} USING COVERING INDEX {physicalIndex} (__groundwork_scope=? AND workflowExecutionId=?)"
                         ),
                         (
                             Command: $"SELECT pendingCount FROM {specification.TableName} WHERE __groundwork_scope = @p0 AND workflowExecutionId = @p1 ORDER BY workflowExecutionId ASC, streamHeadId ASC",
                             Plan: $"2\t0\t0\tSEARCH {specification.TableName} USING COVERING INDEX {physicalIndex} (__groundwork_scope=? AND workflowExecutionId=?)"
                         ),
                         (
                             Command: $"SELECT pendingCount FROM {specification.TableName} WHERE __groundwork_scope = @p0 AND workflowExecutionId = @p1 ORDER BY workflowExecutionId ASC, streamHeadId ASC LIMIT 1 OFFSET 1",
                             Plan: $"2\t0\t0\tSEARCH {specification.TableName} USING COVERING INDEX {physicalIndex} (__groundwork_scope=? AND workflowExecutionId=?)"
                         ),
                         (
                             Command: $"SELECT pendingCount FROM {specification.TableName} WHERE __groundwork_scope = @p0 AND workflowExecutionId = @p1 ORDER BY workflowExecutionId ASC, streamHeadId ASC LIMIT 1",
                             Plan: $"2\t0\t0\tSEARCH {specification.TableName} USING COVERING INDEX {physicalIndex} (__groundwork_scope=? AND workflowExecutionId=?)\n20\t0\t0\tMATERIALIZE __groundwork_total"
                         )
                     })
            {
                var rejected = WriteArtifact(new RuntimeNativePlanArtifact(
                    1,
                    "sqlite",
                    RuntimeNativePlanContract.GroundworkAdapter,
                    specification.RouteIdentity,
                    specification.TableName,
                    specification.IndexName,
                    physicalIndex,
                    invalid.Command,
                    invalid.Plan));
                try
                {
                    Assert.Throws<PerformanceContractException>(() =>
                        RuntimeNativePlanContract.ValidateEnvelope(
                            DistributedCommandSendLeaseAckWorkload.WorkloadId,
                            "sqlite",
                            RuntimeNativePlanContract.GroundworkAdapter,
                            route,
                            rejected));
                }
                finally
                {
                    File.Delete(rejected);
                }
            }
        }
        finally
        {
            File.Delete(accepted);
        }
    }

    private static NativeRouteEvidence Route(
        RuntimeNativeRouteSpec specification,
        string indexName,
        bool latestPerKey = false) =>
        new(
            specification.RouteIdentity,
            "runtime.sqlite.route.raw.json",
            new string('a', 64),
            "index-search",
            indexName,
            specification.PhysicalCardinality,
            specification.StorageScopeRequired,
            specification.PredicateColumn is not null,
            specification.FiniteLimit,
            specification.ResultShape == RuntimeNativeResultShape.ScalarCount ? 0 : specification.FiniteLimit,
            specification.ResultShape,
            specification.ScalarResultCount,
            latestPerKey)
        {
            NativeFetchLimit = specification.NativeFetchLimit
        };

    private static string WriteArtifact(RuntimeNativePlanArtifact artifact)
    {
        var path = Path.Combine(Path.GetTempPath(), $"runtime-native-plan-{Guid.NewGuid():N}.raw.json");
        File.WriteAllText(path, JsonSerializer.Serialize(artifact, ArtifactStore.JsonOptions));
        return path;
    }
}
