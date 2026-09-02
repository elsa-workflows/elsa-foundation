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
            "SELECT * FROM runtime_workflow_trigger_binding WHERE __groundwork_scope = @scope AND stimulusLookupKey = @stimulus AND isActive = 1 ORDER BY triggerBindingId ASC LIMIT 20",
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
                "SELECT * FROM runtime_workflow_trigger_binding WHERE __groundwork_scope IS NOT NULL AND stimulusLookupKey = @stimulus AND isActive = 1 ORDER BY triggerBindingId ASC LIMIT 20",
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
        var command = "SELECT * FROM runtime_workflow_trigger_binding WHERE __groundwork_scope = $1 AND stimulusLookupKey = $2 AND isActive = $3 ORDER BY triggerBindingId ASC LIMIT 20";
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
        var command = "{\"find\":\"runtime_durable_timer\",\"filter\":{\"__groundwork_scope\":{\"$eq\":\"scope\"},\"timerDueTime\":{\"$lte\":\"asOf\"}},\"sort\":{\"timerDueTime\":1,\"timerId\":1},\"limit\":50}";
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
        var command = "{\"collection\":\"elsa_distributed_execution_placement\",\"limit\":64,\"sort\":{\"expiresAt\":1,\"workflowExecutionId\":1},\"filter\":{\"__groundwork_scope\":{\"$eq\":\"scope\"},\"ownerId\":{\"$eq\":\"worker-alpha\"},\"expiresAt\":{\"$gt\":\"2026-07-20T10:00:00Z\"}}}";
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

    private static NativeRouteEvidence Route(RuntimeNativeRouteSpec specification, string indexName) =>
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
            specification.FiniteLimit);

    private static string WriteArtifact(RuntimeNativePlanArtifact artifact)
    {
        var path = Path.Combine(Path.GetTempPath(), $"runtime-native-plan-{Guid.NewGuid():N}.raw.json");
        File.WriteAllText(path, JsonSerializer.Serialize(artifact, ArtifactStore.JsonOptions));
        return path;
    }
}
