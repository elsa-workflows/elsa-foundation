using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class DiagnosticsNativePlanAdmissionTests
{
    [Fact]
    public void Current_route_contract_admits_only_declared_order_covering_indexes()
    {
        var resource = DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "resources-by-last-seen");
        var trace = DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "traces-by-last-seen");

        Assert.Equal(("elsa_otel_resources_v2", "elsa_otel_resources_last_seen"), (resource.TableName, resource.IndexName));
        Assert.Equal(("elsa_otel_trace_summaries_v3", "elsa_otel_trace_summaries_start"), (trace.TableName, trace.IndexName));
        Assert.Equal("elsa_otel_resources_status_last_seen", DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "resources-by-status").IndexName);
        Assert.Equal("elsa_otel_resources_service_last_seen", DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "resources-by-service").IndexName);
        Assert.Equal("elsa_otel_metric_points_timestamp", DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "metrics-by-last-seen").IndexName);
        Assert.Equal("elsa_otel_logs_timestamp", DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "logs-by-last-seen").IndexName);
        Assert.Equal("elsa_structured_logs_sequence_order", DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "structured-log-recent").IndexName);
        Assert.Equal("elsa_structured_logs_sequence_order", DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "structured-log-replay").IndexName);
        Assert.Equal(8, DiagnosticsDurableHistoryWorkload.NativeRouteLimits.Keys.Count(route =>
            !string.IsNullOrWhiteSpace(DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, route).IndexName)));
    }

    [Fact]
    public void Groundwork_indexes_bind_logical_names_to_provider_physical_names()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");

        Assert.Equal(specification.IndexName,
            DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("mongodb", specification));
        Assert.StartsWith("__groundwork_ix_", DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("sqlite", specification));
        Assert.NotEqual(specification.IndexName,
            DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("postgresql", specification));
        Assert.NotEqual(specification.IndexName,
            DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("sqlserver", specification));
    }

    [Fact]
    public void Fanout_unindexed_and_order_materializing_routes_are_explicitly_blocked()
    {
        var blocked = new[]
        {
            "trace-detail"
        };

        Assert.All(blocked, route =>
            Assert.Empty(DiagnosticsNativePlanContract.For(
                DiagnosticsNativePlanContract.GroundworkAdapter,
                route).IndexName));
    }

    [Fact]
    public void Trace_detail_has_independent_bounded_constituents_including_primary_key_fanout()
    {
        var constituents = DiagnosticsNativePlanContract.TraceDetailConstituents(
            DiagnosticsNativePlanContract.GroundworkAdapter);

        Assert.Equal(
            [
                "trace-detail/summary-by-trace-key",
                "trace-detail/spans-by-trace-key-start-id",
                "trace-detail/logs-by-trace-key-timestamp-id",
                "trace-detail/resources-by-id"
            ],
            constituents.Select(constituent => constituent.RouteIdentity));

        Assert.Equal(DiagnosticsTraceDetailOperationKind.PrimaryKeyRead, constituents[0].OperationKind);
        Assert.Equal("elsa_otel_trace_summaries_v3", constituents[0].TableName);
        Assert.Empty(constituents[0].IndexName);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, constituents[0].PhysicalCardinality);
        Assert.Equal(1, constituents[0].FiniteLimit);
        Assert.Equal(1, constituents[0].PublicRowBound);

        Assert.Equal(DiagnosticsTraceDetailOperationKind.BoundedOrderedQuery, constituents[1].OperationKind);
        Assert.Equal("elsa_otel_spans_trace_detail", constituents[1].IndexName);
        Assert.Equal(
            [
                new RuntimeNativeOrderTerm("startTime", RuntimeNativeOrderDirection.Ascending),
                new RuntimeNativeOrderTerm("spanId", RuntimeNativeOrderDirection.Ascending),
                new RuntimeNativeOrderTerm("sequence", RuntimeNativeOrderDirection.Ascending)
            ],
            constituents[1].Ordering);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, constituents[1].PublicRowBound);
        Assert.Equal((DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream + DiagnosticsDurableHistoryWorkload.QueryLimit - 1) / DiagnosticsDurableHistoryWorkload.QueryLimit, constituents[1].MaxInvocationCount);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, constituents[1].PhysicalCardinality);

        Assert.Equal(DiagnosticsTraceDetailOperationKind.BoundedOrderedQuery, constituents[2].OperationKind);
        Assert.Equal("elsa_otel_logs_trace_detail", constituents[2].IndexName);
        Assert.Equal(
            [
                new RuntimeNativeOrderTerm("timestamp", RuntimeNativeOrderDirection.Ascending),
                new RuntimeNativeOrderTerm("id", RuntimeNativeOrderDirection.Ascending),
                new RuntimeNativeOrderTerm("sequence", RuntimeNativeOrderDirection.Ascending)
            ],
            constituents[2].Ordering);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, constituents[2].PublicRowBound);
        Assert.Equal((DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream + DiagnosticsDurableHistoryWorkload.QueryLimit - 1) / DiagnosticsDurableHistoryWorkload.QueryLimit, constituents[2].MaxInvocationCount);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, constituents[2].PhysicalCardinality);

        Assert.Equal(DiagnosticsTraceDetailOperationKind.PrimaryKeyRead, constituents[3].OperationKind);
        Assert.Empty(constituents[3].IndexName);
        Assert.Equal(1, constituents[3].FiniteLimit);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.ResourceCount, constituents[3].PhysicalCardinality);
        Assert.Equal(Math.Min(5_000, DiagnosticsDurableHistoryWorkload.ResourceCount), constituents[3].MaxInvocationCount);
    }

    [Fact]
    public void Trace_detail_primary_key_evidence_does_not_claim_a_secondary_index()
    {
        var specification = DiagnosticsNativePlanContract.TraceDetailConstituents(
                DiagnosticsNativePlanContract.GroundworkAdapter)
            .Single(item => item.RouteIdentity == "trace-detail/summary-by-trace-key");
        var evidence = new DiagnosticsTraceDetailConstituentEvidence(
            specification.RouteIdentity,
            "",
            "",
            "primary-key-read",
            "",
            "SELECT * FROM elsa_otel_trace_summaries_v3 WHERE __groundwork_scope = @scope AND traceKey = @traceKey",
            specification.PhysicalCardinality,
            true,
            true,
            specification.FiniteLimit,
            1,
            1,
            1,
            1);

        DiagnosticsNativePlanContract.ValidateTraceDetailConstituent(
            "sqlite",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            evidence,
            null);
    }

    [Fact]
    public void Trace_detail_signal_evidence_requires_the_complete_bounded_ordered_index_plan()
    {
        var specification = DiagnosticsNativePlanContract.TraceDetailConstituents(
                DiagnosticsNativePlanContract.GroundworkAdapter)
            .Single(item => item.RouteIdentity == "trace-detail/spans-by-trace-key-start-id");
        var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(
            "sqlite",
            new DiagnosticsNativeRouteSpec(
                specification.RouteIdentity,
                specification.TableName,
                specification.IndexName,
                "startTime",
                specification.PredicateColumn,
                specification.PhysicalCardinality,
                specification.FiniteLimit,
                specification.StorageScopeRequired,
                false,
                specification.Ordering));
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.FullName, "spans.raw.json");
        var artifact = new DiagnosticsNativePlanArtifact(
            1,
            "sqlite",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            physicalIndex,
            "SELECT * FROM elsa_otel_spans_v2 WHERE __groundwork_scope = @scope AND traceKey = @traceKey ORDER BY startTime ASC, spanId ASC, sequence ASC LIMIT 127",
            $"2 0 SEARCH elsa_otel_spans_v2 USING INDEX {physicalIndex} (__groundwork_scope=? AND traceKey=?)");
        File.WriteAllText(path, JsonSerializer.Serialize(artifact));
        var evidence = new DiagnosticsTraceDetailConstituentEvidence(
            specification.RouteIdentity,
            "spans.raw.json",
            new string('a', 64),
            "index-search",
            physicalIndex,
            artifact.CommandText,
            specification.PhysicalCardinality,
            true,
            true,
            specification.FiniteLimit,
            specification.PublicRowBound,
            specification.PublicRowBound,
            specification.MaxInvocationCount,
            specification.MaxInvocationCount);

        DiagnosticsNativePlanContract.ValidateTraceDetailConstituent(
            "sqlite",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            evidence,
            path);
    }

    [Fact]
    public void Trace_detail_signal_evidence_rejects_a_scan_or_sort_plan()
    {
        var specification = DiagnosticsNativePlanContract.TraceDetailConstituents(
                DiagnosticsNativePlanContract.GroundworkAdapter)
            .Single(item => item.RouteIdentity == "trace-detail/logs-by-trace-key-timestamp-id");
        var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(
            "sqlite",
            new DiagnosticsNativeRouteSpec(
                specification.RouteIdentity,
                specification.TableName,
                specification.IndexName,
                "timestamp",
                specification.PredicateColumn,
                specification.PhysicalCardinality,
                specification.FiniteLimit,
                specification.StorageScopeRequired,
                false,
                specification.Ordering));
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.FullName, "logs.raw.json");
        var artifact = new DiagnosticsNativePlanArtifact(
            1,
            "sqlite",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            physicalIndex,
            "SELECT * FROM elsa_otel_logs_v2 WHERE __groundwork_scope = @scope AND traceKey = @traceKey ORDER BY timestamp ASC, id ASC, sequence ASC LIMIT 127",
            $"2 0 SCAN elsa_otel_logs_v2\n3 0 USE TEMP B-TREE FOR ORDER BY");
        File.WriteAllText(path, JsonSerializer.Serialize(artifact));
        var evidence = new DiagnosticsTraceDetailConstituentEvidence(
            specification.RouteIdentity,
            "logs.raw.json",
            new string('a', 64),
            "index-search",
            physicalIndex,
            artifact.CommandText,
            specification.PhysicalCardinality,
            true,
            true,
            specification.FiniteLimit,
            specification.PublicRowBound,
            specification.PublicRowBound,
            specification.MaxInvocationCount,
            specification.MaxInvocationCount);

        var exception = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateTraceDetailConstituent(
                "sqlite",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                evidence,
                path));
        Assert.True(DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception));
    }

    [Fact]
    public void Trace_detail_signal_evidence_accepts_the_keyset_continuation_shape()
    {
        var specification = DiagnosticsNativePlanContract.TraceDetailConstituents(
                DiagnosticsNativePlanContract.GroundworkAdapter)
            .Single(item => item.RouteIdentity == "trace-detail/spans-by-trace-key-start-id");
        var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(
            "sqlite",
            new DiagnosticsNativeRouteSpec(
                specification.RouteIdentity,
                specification.TableName,
                specification.IndexName,
                "startTime",
                specification.PredicateColumn,
                specification.PhysicalCardinality,
                specification.FiniteLimit,
                specification.StorageScopeRequired,
                false,
                specification.Ordering));
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.FullName, "continuation.raw.json");
        var command = "SELECT * FROM elsa_otel_spans_v2 WHERE __groundwork_scope = @scope AND traceKey = @traceKey AND ((startTime > @start) OR (startTime = @startEqual AND spanId > @span) OR (startTime = @startEqual2 AND spanId = @spanEqual AND sequence > @sequence)) ORDER BY startTime ASC, spanId ASC, sequence ASC LIMIT 127";
        var artifact = new DiagnosticsNativePlanArtifact(
            1,
            "sqlite",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            physicalIndex,
            command,
            $"2 0 SEARCH elsa_otel_spans_v2 USING INDEX {physicalIndex} (__groundwork_scope=? AND traceKey=?)");
        File.WriteAllText(path, JsonSerializer.Serialize(artifact));
        var evidence = new DiagnosticsTraceDetailConstituentEvidence(
            specification.RouteIdentity,
            "continuation.raw.json",
            new string('a', 64),
            "index-search",
            physicalIndex,
            command,
            specification.PhysicalCardinality,
            true,
            true,
            specification.FiniteLimit,
            specification.PublicRowBound,
            specification.PublicRowBound,
            specification.MaxInvocationCount,
            specification.MaxInvocationCount);

        DiagnosticsNativePlanContract.ValidateTraceDetailConstituent(
            "sqlite",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            evidence,
            path);
    }

    [Theory]
    [InlineData("resources-by-status", "status", true)]
    [InlineData("resources-by-service", "serviceNameKey", true)]
    [InlineData("metrics-by-last-seen", null, true)]
    [InlineData("logs-by-last-seen", null, true)]
    [InlineData("structured-log-recent", null, true)]
    [InlineData("structured-log-replay", null, false)]
    public void Groundwork_frozen_routes_bind_exact_order_and_predicate_shape(
        string route,
        string? predicate,
        bool descending)
    {
        var specification = DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, route);

        Assert.NotEqual(string.Empty, specification.IndexName);
        Assert.Equal(predicate, specification.PredicateColumn);
        Assert.Equal(descending, specification.Descending);
        Assert.True(specification.StorageScopeRequired);
    }

    [Fact]
    public void Unfiltered_route_has_no_route_predicate_but_still_requires_scope_binding()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");

        Assert.True(specification.StorageScopeRequired);
        Assert.Null(specification.PredicateColumn);
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
    [InlineData("sqlite", "2 0 SCAN elsa_otel_resources_v2")]
    [InlineData("postgresql", "[{\"Plan\":{\"Node Type\":\"Seq Scan\",\"Relation Name\":\"elsa_otel_resources_v2\"}}]")]
    [InlineData("sqlserver", "<ShowPlanXML><RelOp PhysicalOp=\"Table Scan\"><TableScan><Object Table=\"[elsa_otel_resources_v2]\" /></TableScan></RelOp></ShowPlanXML>")]
    [InlineData("mongodb", "{\"winningPlan\":{\"stage\":\"COLLSCAN\"}}")]
    public void Physical_scan_plans_are_classified_as_explicitly_blocked(string provider, string nativePlan)
    {
        using var fixture = Fixture.Create(provider, "resources-by-last-seen", nativePlan: nativePlan);

        var exception = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateEnvelope(
                provider,
                fixture.Adapter,
                fixture.Route,
                fixture.Path));

        Assert.True(DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception));
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

        var exception = Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "sqlite", fixture.Adapter, fixture.Route, fixture.Path));
        Assert.False(DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception));
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
                command: $"SELECT * FROM elsa_otel_resources_v2 WHERE {predicate} ORDER BY lastSeen DESC, idOrderKey ASC, id ASC LIMIT 127");

            Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
                "sqlite", fixture.Adapter, fixture.Route, fixture.Path));
        }
    }

    [Fact]
    public void Groundwork_sqlite_route_admits_exact_total_boolean_scope_and_identity_order()
    {
        using var fixture = Fixture.Create(
            "sqlite",
            "resources-by-last-seen",
            command:
                "SELECT * FROM \"elsa_otel_resources_v2\" " +
                "WHERE (\"__groundwork_scope\" COLLATE GROUNDWORK_UTF16_ORDINAL IS NOT NULL AND " +
                "\"__groundwork_scope\" COLLATE GROUNDWORK_UTF16_ORDINAL = @p0) " +
                "ORDER BY \"lastSeen\" DESC, \"idOrderKey\" COLLATE GROUNDWORK_UTF16_ORDINAL ASC, " +
                "CASE WHEN \"id\" COLLATE GROUNDWORK_UTF16_ORDINAL IS NULL THEN 0 ELSE 1 END ASC, " +
                "\"id\" COLLATE GROUNDWORK_UTF16_ORDINAL ASC LIMIT @p1;");

        DiagnosticsNativePlanContract.ValidateEnvelope(
            "sqlite",
            fixture.Adapter,
            fixture.Route,
            fixture.Path);
    }

    [Fact]
    public void Groundwork_route_requires_the_storage_scope_equality_even_when_route_flags_claim_it()
    {
        using var fixture = Fixture.Create(
            "sqlite",
            "resources-by-last-seen",
            command: "SELECT * FROM elsa_otel_resources_v2 ORDER BY lastSeen DESC, idOrderKey ASC, id ASC LIMIT 127");

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "sqlite", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Fact]
    public void Provider_owned_physical_index_mismatch_is_rejected()
    {
        using var fixture = Fixture.Create("postgresql", "resources-by-last-seen");
        var expected = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(
            "postgresql",
            DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "resources-by-last-seen"));
        File.WriteAllText(fixture.Path, File.ReadAllText(fixture.Path).Replace(expected, "wrong_physical_index", StringComparison.Ordinal));

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "postgresql", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Fact]
    public void Complete_ordered_terms_are_required_in_the_provider_command()
    {
        using var fixture = Fixture.Create(
            "sqlite",
            "resources-by-last-seen",
            command: "SELECT * FROM elsa_otel_resources_v2 WHERE __groundwork_scope = @scope ORDER BY lastSeen DESC LIMIT 127");

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "sqlite", fixture.Adapter, fixture.Route, fixture.Path));
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(string provider, string routeIdentity, string commandText, string nativePlan)
        {
            Adapter = DiagnosticsNativePlanContract.GroundworkAdapter;
            Route = RouteFor(Adapter, provider, routeIdentity);
            var artifact = new DiagnosticsNativePlanArtifact(
                1,
                provider,
                Adapter,
                routeIdentity,
                RouteSpec.TableName,
                RouteSpec.IndexName,
                DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(provider, RouteSpec),
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
                "mongodb" => $"{{\"collection\":\"{spec.TableName}\",\"filter\":{{\"__groundwork_scope\":{{\"$eq\":\"scope\"}}}},\"sort\":{{\"lastSeen\":-1,\"idOrderKey\":1,\"id\":1}},\"limit\":127}}",
                _ => "SELECT * FROM elsa_otel_resources_v2 WHERE __groundwork_scope = @scope ORDER BY lastSeen DESC, idOrderKey ASC, id ASC LIMIT 127"
            };
            var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(provider, spec);
            nativePlan ??= provider switch
            {
                "sqlite" => $"2 0 SEARCH elsa_otel_resources_v2 USING INDEX {physicalIndex} (__groundwork_scope=?)",
                "postgresql" => $"[{{\"Plan\":{{\"Node Type\":\"Index Scan\",\"Relation Name\":\"elsa_otel_resources_v2\",\"Index Name\":\"{physicalIndex}\"}}}}]",
                "sqlserver" => $"<ShowPlanXML><RelOp PhysicalOp=\"Index Seek\"><IndexScan><Object Table=\"[elsa_otel_resources_v2]\" Index=\"[{physicalIndex}]\" /></IndexScan></RelOp></ShowPlanXML>",
                "mongodb" => $"{{\"winningPlan\":{{\"stage\":\"IXSCAN\",\"indexName\":\"{physicalIndex}\"}}}}",
                _ => throw new ArgumentOutOfRangeException(nameof(provider))
            };
            return new Fixture(provider, routeIdentity, command, nativePlan);
        }

        public void Dispose()
        {
            if (Directory.Exists)
                Directory.Delete(true);
        }

        private static NativeRouteEvidence RouteFor(string adapter, string provider, string routeIdentity)
        {
            var spec = DiagnosticsNativePlanContract.For(adapter, routeIdentity);
            return new NativeRouteEvidence(
                routeIdentity,
                "route.raw.json",
                new string('a', 64),
                "index-search",
                DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(provider, spec),
                spec.PhysicalCardinality,
                spec.StorageScopeRequired,
                spec.PredicateColumn is not null,
                spec.FiniteLimit,
                spec.FiniteLimit);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("diagnostics-trace-detail-");

        public string FullName => directory.FullName;

        public void Dispose() => directory.Delete(true);
    }
}
