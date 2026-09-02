using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class DiagnosticsNativePlanAdmissionTests
{
    [Fact]
    public void Current_v3_route_contract_admits_only_declared_order_covering_indexes()
    {
        var resource = DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "resources-by-last-seen");
        var trace = DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "traces-by-last-seen");

        Assert.Equal(("elsa_otel_resources_v2", "elsa_otel_resources_last_seen"), (resource.TableName, resource.IndexName));
        Assert.Equal(("elsa_otel_trace_summaries_v3", "elsa_otel_trace_summaries_start"), (trace.TableName, trace.IndexName));
        Assert.Empty(DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "resources-by-status").IndexName);
        Assert.Empty(DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "resources-by-service").IndexName);
        Assert.Equal(2, DiagnosticsDurableHistoryWorkload.NativeRouteLimits.Keys.Count(route =>
            !string.IsNullOrWhiteSpace(DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, route).IndexName)));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("mongodb")]
    public void Provider_specific_native_route_envelopes_are_admitted(string provider)
    {
        using var fixture = Fixture.Create(provider, "resources-by-last-seen");

        DiagnosticsNativePlanContract.ValidateEnvelope(
            provider,
            fixture.Adapter,
            fixture.Route,
            fixture.Path);
    }

    [Theory]
    [InlineData("sqlite", "2 0 SEARCH elsa_otel_resources_v2 USING INDEX elsa_otel_resources_last_seen (__groundwork_scope=?)\n3 0 USE TEMP B-TREE FOR ORDER BY")]
    [InlineData("sqlite", "2 0 SEARCH elsa_otel_resources_v2 USING INDEX elsa_otel_resources_last_seen (__groundwork_scope=?)\n3 0 MATERIALIZE page")]
    [InlineData("postgresql", "[{\"Plan\":{\"Node Type\":\"Sort\",\"Plans\":[{\"Node Type\":\"Index Scan\",\"Relation Name\":\"elsa_otel_resources_v2\",\"Index Name\":\"elsa_otel_resources_last_seen\"}]}}]")]
    [InlineData("postgresql", "[{\"Plan\":{\"Node Type\":\"Materialize\",\"Plans\":[{\"Node Type\":\"Index Scan\",\"Relation Name\":\"elsa_otel_resources_v2\",\"Index Name\":\"elsa_otel_resources_last_seen\"}]}}]")]
    [InlineData("sqlserver", "<ShowPlanXML><RelOp PhysicalOp=\"Sort\"><RelOp PhysicalOp=\"Index Seek\"><IndexScan><Object Table=\"[elsa_otel_resources_v2]\" Index=\"[elsa_otel_resources_last_seen]\" /></IndexScan></RelOp></RelOp></ShowPlanXML>")]
    [InlineData("sqlserver", "<ShowPlanXML><RelOp PhysicalOp=\"Table Spool\"><RelOp PhysicalOp=\"Index Seek\"><IndexScan><Object Table=\"[elsa_otel_resources_v2]\" Index=\"[elsa_otel_resources_last_seen]\" /></IndexScan></RelOp></RelOp></ShowPlanXML>")]
    [InlineData("mongodb", "{\"winningPlan\":{\"stage\":\"SORT\",\"inputStage\":{\"stage\":\"IXSCAN\",\"indexName\":\"elsa_otel_resources_last_seen\"}}}")]
    [InlineData("mongodb", "{\"winningPlan\":{\"stage\":\"MATERIALIZE\",\"inputStage\":{\"stage\":\"IXSCAN\",\"indexName\":\"elsa_otel_resources_last_seen\"}}}")]
    public void Index_bounded_route_rejects_explicit_sort_or_materialization_operators(string provider, string nativePlan)
    {
        using var fixture = Fixture.Create(provider, "resources-by-last-seen", nativePlan: nativePlan);

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            provider, fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Theory]
    [InlineData("postgresql", "[{\"Plan\":{\"Node Type\":\"Index Scan\",\"Relation Name\":\"elsa_otel_resources_v2\",\"Index Name\":\"elsa_otel_resources_last_seen\",\"Sort Method\":\"external merge\",\"Sort Space Type\":\"Disk\"}}]")]
    [InlineData("sqlserver", "<ShowPlanXML><RelOp PhysicalOp=\"Index Seek\"><Warnings><SpillOccurred /></Warnings><IndexScan><Object Table=\"[elsa_otel_resources_v2]\" Index=\"[elsa_otel_resources_last_seen]\" /></IndexScan></RelOp></ShowPlanXML>")]
    [InlineData("mongodb", "{\"winningPlan\":{\"stage\":\"IXSCAN\",\"indexName\":\"elsa_otel_resources_last_seen\"},\"executionStats\":{\"usedDisk\":true}}")]
    public void Index_bounded_route_rejects_explicit_spill_metadata(string provider, string nativePlan)
    {
        using var fixture = Fixture.Create(provider, "resources-by-last-seen", nativePlan: nativePlan);

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            provider, fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Fact]
    public void Retained_plan_must_name_the_exact_route_index()
    {
        using var fixture = Fixture.Create(
            "sqlite",
            "resources-by-last-seen",
            nativePlan: "2 0 SEARCH elsa_otel_resources_v2 USING INDEX unrelated_index (__groundwork_scope=?)");

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "sqlite", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Fact]
    public void Direct_sql_route_predicates_reject_case_functions_and_tautologies()
    {
        foreach (var predicate in new[]
        {
            "CASE WHEN __groundwork_scope = @scope THEN 1 END = 1",
            "LOWER(__groundwork_scope) = @scope",
            "__groundwork_scope = @scope OR 1 = 1"
        })
        {
            using var fixture = Fixture.Create(
                "sqlite",
                "resources-by-last-seen",
                command: $"SELECT * FROM elsa_otel_resources_v2 WHERE {predicate} ORDER BY lastSeen DESC LIMIT 127");

            Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
                "sqlite", fixture.Adapter, fixture.Route, fixture.Path));
        }
    }

    [Fact]
    public void Groundwork_route_requires_the_storage_scope_equality_even_when_route_flags_claim_it()
    {
        using var fixture = Fixture.Create(
            "sqlite",
            "resources-by-last-seen",
            command: "SELECT * FROM elsa_otel_resources_v2 ORDER BY lastSeen DESC LIMIT 127");

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "sqlite", fixture.Adapter, fixture.Route, fixture.Path));
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(string provider, string routeIdentity, string commandText, string nativePlan)
        {
            Adapter = DiagnosticsNativePlanContract.GroundworkAdapter;
            Route = RouteFor(Adapter, routeIdentity);
            var artifact = new DiagnosticsNativePlanArtifact(
                1,
                provider,
                Adapter,
                routeIdentity,
                RouteSpec.TableName,
                RouteSpec.IndexName,
                commandText,
                nativePlan);
            Directory = System.IO.Directory.CreateTempSubdirectory("diagnostics-native-plan-");
            Path = System.IO.Path.Combine(Directory.FullName, "route.raw.json");
            File.WriteAllText(Path, JsonSerializer.Serialize(artifact));
        }

        public string Adapter { get; }
        public NativeRouteEvidence Route { get; }
        private DiagnosticsNativeRouteSpec RouteSpec => DiagnosticsNativePlanContract.For(Adapter, Route.RouteIdentity);
        private DirectoryInfo Directory { get; }
        public string Path { get; }

        public static Fixture Create(
            string provider,
            string routeIdentity,
            string? command = null,
            string? nativePlan = null)
        {
            var spec = DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, routeIdentity);
            command ??= provider switch
            {
                "mongodb" => $"{{\"collection\":\"{spec.TableName}\",\"filter\":{{\"__groundwork_scope\":{{\"$eq\":\"scope\"}}}},\"sort\":{{\"lastSeen\":-1}},\"limit\":127}}",
                _ => "SELECT * FROM elsa_otel_resources_v2 WHERE __groundwork_scope = @scope ORDER BY lastSeen DESC LIMIT 127"
            };
            nativePlan ??= provider switch
            {
                "sqlite" => "2 0 SEARCH elsa_otel_resources_v2 USING INDEX elsa_otel_resources_last_seen (__groundwork_scope=?)",
                "postgresql" => "[{\"Plan\":{\"Node Type\":\"Index Scan\",\"Relation Name\":\"elsa_otel_resources_v2\",\"Index Name\":\"elsa_otel_resources_last_seen\"}}]",
                "sqlserver" => "<ShowPlanXML><RelOp PhysicalOp=\"Index Seek\"><IndexScan><Object Table=\"[elsa_otel_resources_v2]\" Index=\"[elsa_otel_resources_last_seen]\" /></IndexScan></RelOp></ShowPlanXML>",
                "mongodb" => "{\"winningPlan\":{\"stage\":\"IXSCAN\",\"indexName\":\"elsa_otel_resources_last_seen\"}}",
                _ => throw new ArgumentOutOfRangeException(nameof(provider))
            };
            return new Fixture(provider, routeIdentity, command, nativePlan);
        }

        public void Dispose()
        {
            if (Directory.Exists)
                Directory.Delete(true);
        }

        private static NativeRouteEvidence RouteFor(string adapter, string routeIdentity)
        {
            var spec = DiagnosticsNativePlanContract.For(adapter, routeIdentity);
            return new NativeRouteEvidence(
                routeIdentity,
                "route.raw.json",
                new string('a', 64),
                "index-search",
                spec.IndexName,
                spec.PhysicalCardinality,
                spec.StorageScopeRequired,
                spec.PredicateColumn is not null,
                spec.FiniteLimit,
                spec.FiniteLimit);
        }
    }
}
