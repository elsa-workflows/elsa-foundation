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
    }

    [Fact]
    public void Sqlite_envelope_requires_the_exact_route_index_and_scope_equality()
    {
        var specification = RuntimeNativePlanContract.For(
            RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId,
            "list-by-stimulus-and-type");
        var route = Route(specification, specification.IndexName);
        var path = WriteArtifact(new RuntimeNativePlanArtifact(
            1,
            "sqlite",
            RuntimeNativePlanContract.GroundworkAdapter,
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            specification.IndexName,
            "SELECT * FROM runtime_workflow_trigger_binding WHERE __groundwork_scope = @scope AND stimulusLookupKey = @stimulus AND isActive = 1 ORDER BY triggerBindingId ASC LIMIT 20",
            "2\t0\tSEARCH runtime_workflow_trigger_binding USING INDEX by_stimulus_and_type (stimulusLookupKey=? AND isActive=?)"));

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
                specification.IndexName,
                "SELECT * FROM runtime_workflow_trigger_binding WHERE __groundwork_scope IS NOT NULL AND stimulusLookupKey = @stimulus AND isActive = 1 ORDER BY triggerBindingId ASC LIMIT 20",
                "2\t0\tSEARCH runtime_workflow_trigger_binding USING INDEX by_stimulus_and_type (stimulusLookupKey=? AND isActive=?)"));
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
        var route = Route(specification, specification.IndexName);
        var command = "SELECT * FROM runtime_workflow_trigger_binding WHERE __groundwork_scope = $1 AND stimulusLookupKey = $2 AND isActive = $3 ORDER BY triggerBindingId ASC LIMIT 20";
        var accepted = WriteArtifact(new RuntimeNativePlanArtifact(
            1,
            "postgresql",
            RuntimeNativePlanContract.GroundworkAdapter,
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            specification.IndexName,
            command,
            "[{\"Plan\":{\"Node Type\":\"Index Scan\",\"Relation Name\":\"runtime_workflow_trigger_binding\",\"Index Name\":\"by_stimulus_and_type\",\"spillCount\":0}}]"));
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
                specification.IndexName,
                command,
                "[{\"Plan\":{\"Node Type\":\"Index Scan\",\"Relation Name\":\"runtime_workflow_trigger_binding\",\"Index Name\":\"by_stimulus_and_type\",\"spillCount\":1}}]"));
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
