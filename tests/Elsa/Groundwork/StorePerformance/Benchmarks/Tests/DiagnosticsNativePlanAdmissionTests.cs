using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class DiagnosticsNativePlanAdmissionTests
{
    private const string MongoOrdinalKeyFunctionBody =
        "function(value) { if (value === null || value === undefined) return null; var key = ''; for (var i = 0; i < value.length; i++) { var unit = value.charCodeAt(i).toString(16); key += ('0000' + unit).slice(-4); } return key; }";

    [Fact]
    public void Current_route_contract_admits_only_declared_order_covering_indexes()
    {
        var resource = DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "resources-by-last-seen");
        var trace = DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "traces-by-last-seen");

        Assert.Equal(("elsa_otel_resources_v2", "elsa_otel_resources_last_seen"), (resource.TableName, resource.IndexName));
        Assert.NotNull(resource.NullableOrderingColumns);
        Assert.Empty(resource.NullableOrderingColumns!);
        Assert.False(resource.RequiresNullRank("lastSeen"));
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
            "SELECT traceKey, traceId, payload FROM elsa_otel_trace_summaries_v3 " +
            "WHERE traceKey = @key_traceKey AND __groundwork_scope = @__groundwork_scope;",
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
    public void Trace_detail_primary_key_fanout_accepts_the_actual_positive_subset_within_its_bound()
    {
        var specification = DiagnosticsNativePlanContract.TraceDetailConstituents(
                DiagnosticsNativePlanContract.GroundworkAdapter)
            .Single(item => item.RouteIdentity == "trace-detail/resources-by-id");
        var evidence = new DiagnosticsTraceDetailConstituentEvidence(
            specification.RouteIdentity,
            "",
            "",
            "primary-key-read",
            "",
            "SELECT id, payload FROM elsa_otel_resources_v2 " +
            "WHERE id = @key_id AND __groundwork_scope = @__groundwork_scope;",
            specification.PhysicalCardinality,
            true,
            true,
            specification.FiniteLimit,
            specification.PublicRowBound,
            1,
            1,
            specification.MaxInvocationCount);

        DiagnosticsNativePlanContract.ValidateTraceDetailConstituent(
            "sqlite",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            evidence,
            null);
    }

    [Fact]
    public void Mongo_trace_detail_primary_key_evidence_requires_the_scoped_collection_and_redacted_identity()
    {
        var specification = DiagnosticsNativePlanContract.TraceDetailConstituents(
                DiagnosticsNativePlanContract.GroundworkAdapter)
            .Single(item => item.RouteIdentity == "trace-detail/summary-by-trace-key");
        var command = JsonSerializer.Serialize(new
        {
            collection = specification.TableName + "__scope__" + new string('A', 64),
            filter = new Dictionary<string, object>
            {
                ["_id"] = new Dictionary<string, string> { ["$eq"] = "<redacted>" }
            },
            limit = 1
        });
        var evidence = new DiagnosticsTraceDetailConstituentEvidence(
            specification.RouteIdentity,
            "",
            "",
            "primary-key-read",
            "",
            command,
            specification.PhysicalCardinality,
            false,
            true,
            specification.FiniteLimit,
            1,
            1,
            1,
            1);

        DiagnosticsNativePlanContract.ValidateTraceDetailConstituent(
            "mongodb",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            evidence,
            null);
        Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateTraceDetailConstituent(
                "mongodb",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                evidence with { CommandText = "MongoDB.FindOne" },
                null));
    }

    [Fact]
    public void Mongo_trace_detail_primary_key_evidence_rejects_unbound_or_value_bearing_shapes()
    {
        var specification = DiagnosticsNativePlanContract.TraceDetailConstituents(
                DiagnosticsNativePlanContract.GroundworkAdapter)
            .Single(item => item.RouteIdentity == "trace-detail/summary-by-trace-key");
        var physicalCollection = specification.TableName + "__scope__" + new string('A', 64);
        var validFilter = new Dictionary<string, object>
        {
            ["_id"] = new Dictionary<string, string> { ["$eq"] = "<redacted>" }
        };
        var invalidCommands = new[]
        {
            JsonSerializer.Serialize(new { collection = specification.TableName, filter = validFilter, limit = 1 }),
            JsonSerializer.Serialize(new { collection = "wrong_table__scope__" + new string('A', 64), filter = validFilter, limit = 1 }),
            JsonSerializer.Serialize(new
            {
                collection = physicalCollection,
                filter = new Dictionary<string, object>
                {
                    ["_id"] = new Dictionary<string, string> { ["$eq"] = "secret-trace-key" }
                },
                limit = 1
            }),
            JsonSerializer.Serialize(new { collection = physicalCollection, filter = validFilter, limit = 2 }),
            JsonSerializer.Serialize(new { collection = physicalCollection, filter = validFilter, limit = 1, sort = new { _id = 1 } })
        };
        var evidence = new DiagnosticsTraceDetailConstituentEvidence(
            specification.RouteIdentity,
            "",
            "",
            "primary-key-read",
            "",
            "",
            specification.PhysicalCardinality,
            false,
            true,
            specification.FiniteLimit,
            specification.PublicRowBound,
            1,
            1,
            specification.MaxInvocationCount);

        Assert.All(invalidCommands, command => Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateTraceDetailConstituent(
                "mongodb",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                evidence with { CommandText = command },
                null)));
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
            "SELECT * FROM elsa_otel_spans_v2 WHERE __groundwork_scope = @scope AND traceKey = @traceKey ORDER BY startTime ASC, __groundwork_ordinal_spanId ASC, sequence ASC LIMIT 127",
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
            "SELECT * FROM elsa_otel_logs_v2 WHERE __groundwork_scope = @scope AND traceKey = @traceKey ORDER BY timestamp ASC, __groundwork_ordinal_id ASC, sequence ASC LIMIT 127",
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
        var command =
            "SELECT * FROM \"elsa_otel_spans_v2\" WHERE " +
            "((\"__groundwork_scope\" COLLATE GROUNDWORK_UTF16_ORDINAL IS NOT NULL AND \"__groundwork_scope\" COLLATE GROUNDWORK_UTF16_ORDINAL = @p0) " +
            "AND (\"traceKey\" COLLATE GROUNDWORK_UTF16_ORDINAL IS NOT NULL AND \"traceKey\" COLLATE GROUNDWORK_UTF16_ORDINAL = @p1)) " +
            "AND ((((\"startTime\" IS NOT NULL AND \"startTime\" > @p2) OR \"startTime\" IS NULL) " +
            "OR ((\"startTime\" IS NOT NULL AND \"startTime\" = @p3) AND ((\"__groundwork_ordinal_spanId\" COLLATE GROUNDWORK_UTF16_ORDINAL IS NOT NULL AND \"__groundwork_ordinal_spanId\" COLLATE GROUNDWORK_UTF16_ORDINAL > @p4) OR \"__groundwork_ordinal_spanId\" COLLATE GROUNDWORK_UTF16_ORDINAL IS NULL)) " +
            "OR ((\"startTime\" IS NOT NULL AND \"startTime\" = @p5) AND (\"__groundwork_ordinal_spanId\" COLLATE GROUNDWORK_UTF16_ORDINAL IS NOT NULL AND \"__groundwork_ordinal_spanId\" COLLATE GROUNDWORK_UTF16_ORDINAL = @p6) AND ((\"sequence\" IS NOT NULL AND \"sequence\" > @p7) OR \"sequence\" IS NULL))) " +
            "ORDER BY \"startTime\" ASC, \"__groundwork_ordinal_spanId\" COLLATE GROUNDWORK_UTF16_ORDINAL ASC, \"sequence\" ASC LIMIT @p8;";
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

    [Fact]
    public void SqlServer_trace_detail_continuation_keeps_raw_predicate_length_boundaries_but_orders_on_persisted_keys()
    {
        var specification = DiagnosticsNativePlanContract.TraceDetailConstituents(
                DiagnosticsNativePlanContract.GroundworkAdapter)
            .Single(item => item.RouteIdentity == "trace-detail/spans-by-trace-key-start-id");
        var route = new DiagnosticsNativeRouteSpec(
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            specification.Ordering.First().Column,
            specification.PredicateColumn,
            specification.PhysicalCardinality,
            specification.FiniteLimit,
            specification.StorageScopeRequired,
            false,
            specification.Ordering,
            []);
        var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("sqlserver", route);
        const string collation = " COLLATE Latin1_General_100_BIN2";
        var command =
            "SELECT * FROM [elsa_otel_spans_v2] WHERE " +
            $"(([__groundwork_scope]{collation} IS NOT NULL AND DATALENGTH([__groundwork_scope]{collation}) = DATALENGTH(@p0) AND [__groundwork_scope]{collation} = @p0) " +
            $"AND ([traceKey]{collation} IS NOT NULL AND DATALENGTH([traceKey]{collation}) = DATALENGTH(@p1) AND [traceKey]{collation} = @p1)) " +
            "AND (((([startTime] IS NOT NULL AND [startTime] > @p2) OR [startTime] IS NULL) " +
            $"OR (([startTime] IS NOT NULL AND [startTime] = @p3) AND ([__groundwork_ordinal_spanId]{collation} > @p4)) " +
            $"OR (([startTime] IS NOT NULL AND [startTime] = @p5) AND ([__groundwork_ordinal_spanId]{collation} = @p6) AND (([sequence] IS NOT NULL AND [sequence] > @p7) OR [sequence] IS NULL))) " +
            $"ORDER BY [startTime] ASC, [__groundwork_ordinal_spanId]{collation} ASC, [sequence] ASC OFFSET 0 ROWS FETCH NEXT @p8 ROWS ONLY;";
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.FullName, "sqlserver-continuation.raw.json");
        var artifact = new DiagnosticsNativePlanArtifact(
            1,
            "sqlserver",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            physicalIndex,
            command,
            SqlServerIndexSeekPlan(route));
        File.WriteAllText(path, JsonSerializer.Serialize(artifact));
        var evidence = new DiagnosticsTraceDetailConstituentEvidence(
            specification.RouteIdentity,
            "sqlserver-continuation.raw.json",
            new string('a', 64),
            DiagnosticsNativePlanContract.IndexSearchPlanClassification,
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
            "sqlserver",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            evidence,
            path);

        var invalidCommands = new[]
        {
            command.Replace("DATALENGTH(@p1)", "DATALENGTH(@wrong)", StringComparison.Ordinal),
            command.Replace(
                $"DATALENGTH([traceKey]{collation}) = DATALENGTH(@p1) AND ",
                string.Empty,
                StringComparison.Ordinal)
        };
        foreach (var invalidCommand in invalidCommands)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(artifact with { CommandText = invalidCommand }));
            Assert.Throws<PerformanceContractException>(() =>
                DiagnosticsNativePlanContract.ValidateTraceDetailConstituent(
                    "sqlserver",
                    DiagnosticsNativePlanContract.GroundworkAdapter,
                    evidence with { CommandText = invalidCommand },
                    path));
        }
    }

    [Fact]
    public void Groundwork_sqlserver_structured_log_recent_admits_the_actual_primary_key_access_path()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "structured-log-recent");
        using var fixture = Fixture.Create(
            "sqlserver",
            specification.RouteIdentity,
            command: SqlServerStructuredLogRecentCommand(),
            nativePlan: SqlServerStructuredLogPrimaryKeyPlan(),
            physicalIndexName: "__groundwork_pk_elsa_structured_logs");

        DiagnosticsNativePlanContract.ValidateEnvelope(
            "sqlserver",
            fixture.Adapter,
            fixture.Route,
            fixture.Path);
    }

    [Fact]
    public void Groundwork_sqlserver_structured_log_primary_key_equivalence_is_fail_closed()
    {
        var command = SqlServerStructuredLogRecentCommand();
        var validPlan = SqlServerStructuredLogPrimaryKeyPlan();
        var invalidPlans = new[]
        {
            validPlan.Replace("Table=\"[elsa_structured_logs]\"", "Table=\"[other_table]\"", StringComparison.Ordinal),
            validPlan.Replace("Index=\"[__groundwork_pk_elsa_structured_logs]\"", "Index=\"[other_index]\"", StringComparison.Ordinal),
            validPlan.Replace("ScanDirection=\"BACKWARD\"", "ScanDirection=\"FORWARD\"", StringComparison.Ordinal),
            validPlan.Replace("Column=\"__groundwork_scope\"", "Column=\"other_scope\"", StringComparison.Ordinal),
            validPlan.Replace("ForcedIndex=\"0\"", "ForcedIndex=\"1\"", StringComparison.Ordinal),
            validPlan.Replace("ParameterRuntimeValue=\"(128)\"", "ParameterRuntimeValue=\"(129)\"", StringComparison.Ordinal),
            validPlan.Replace("ParameterRuntimeValue=\"(128)\"", "ParameterRuntimeValue=\"128\"", StringComparison.Ordinal),
            validPlan.Replace("ActualRows=\"128\"", "ActualRows=\"0\"", StringComparison.Ordinal),
            validPlan.Replace("ActualRowsRead=\"128\"", "ActualRowsRead=\"129\"", StringComparison.Ordinal),
            validPlan.Replace("ActualExecutions=\"128\"", "ActualExecutions=\"129\"", StringComparison.Ordinal),
            validPlan.Replace(
                "Table=\"[elsa_structured_logs]\" TableReferenceId=\"-1\"",
                "Table=\"[other_table]\" TableReferenceId=\"-1\"",
                StringComparison.Ordinal),
            validPlan.Replace(
                "<Convert DataType=\"bigint\" Style=\"0\" Implicit=\"1\">",
                "<Convert DataType=\"bigint\" Style=\"0\" Implicit=\"1\"><Arithmetic Operation=\"ADD\" />",
                StringComparison.Ordinal),
            validPlan.Replace(
                "<Convert DataType=\"nvarchar\" Length=\"216\" Style=\"0\" Implicit=\"1\">",
                "<Convert DataType=\"nvarchar\" Length=\"216\" Style=\"0\" Implicit=\"1\"><Arithmetic Operation=\"ADD\" />",
                StringComparison.Ordinal),
            validPlan.Replace("</RelOp>", "<RelOp PhysicalOp=\"Aggregate\" /></RelOp>", StringComparison.Ordinal),
            validPlan.Replace("</ShowPlanXML>", "<RelOp PhysicalOp=\"Sort\" /></ShowPlanXML>", StringComparison.Ordinal),
            validPlan.Replace("</ShowPlanXML>", "<SpillOccurred /></ShowPlanXML>", StringComparison.Ordinal)
        };

        foreach (var invalidPlan in invalidPlans)
        {
            using var fixture = Fixture.Create(
                "sqlserver",
                "structured-log-recent",
                command: command,
                nativePlan: invalidPlan,
                physicalIndexName: "__groundwork_pk_elsa_structured_logs");

            Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
                "sqlserver",
                fixture.Adapter,
                fixture.Route,
                fixture.Path));
        }
    }

    [Fact]
    public void Groundwork_sqlserver_structured_log_replay_does_not_reuse_recent_primary_key_proof()
    {
        using var fixture = Fixture.Create(
            "sqlserver",
            "structured-log-replay",
            command:
                "SELECT * FROM [elsa_structured_logs] WHERE " +
                "([__groundwork_scope] IS NOT NULL AND [__groundwork_scope] = @p0) AND " +
                "([sequence] IS NOT NULL AND [sequence] > @p1 AND [sequence] <= @p2) " +
                "ORDER BY [sequence] ASC OFFSET 0 ROWS FETCH NEXT @p3 ROWS ONLY",
            nativePlan: SqlServerStructuredLogPrimaryKeyPlan(),
            physicalIndexName: "__groundwork_pk_elsa_structured_logs");

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "sqlserver",
            fixture.Adapter,
            fixture.Route,
            fixture.Path));
    }

    [Fact]
    public void Synthetic_sqlserver_structured_log_replay_primary_key_branch_requires_exact_dual_ranges()
    {
        // Synthetic structural proof only: no live SQL Server replay plan has been accepted yet.
        var command = SqlServerStructuredLogReplayCommand();
        var validPlan = SqlServerStructuredLogReplayPlan();
        var invalidPlans = new[]
        {
            validPlan.Replace("<StartRange ScanType=\"GT\">", string.Empty, StringComparison.Ordinal),
            validPlan.Replace(
                "<EndRange ScanType=\"LE\">",
                SqlServerStructuredLogReplayDuplicateStartRange(),
                StringComparison.Ordinal),
            validPlan.Replace("StartRange ScanType=\"GT\"", "StartRange ScanType=\"GE\"", StringComparison.Ordinal),
            validPlan.Replace("<ColumnReference Column=\"@p1\" />", "<ColumnReference Column=\"@wrong\" />", StringComparison.Ordinal)
        };

        using (var fixture = Fixture.Create(
                   "sqlserver",
                   "structured-log-replay",
                   command: command,
                   nativePlan: validPlan,
                   physicalIndexName: "__groundwork_pk_elsa_structured_logs"))
        {
            DiagnosticsNativePlanContract.ValidateEnvelope("sqlserver", fixture.Adapter, fixture.Route, fixture.Path);
        }

        foreach (var invalidPlan in invalidPlans)
        {
            using var fixture = Fixture.Create(
                "sqlserver",
                "structured-log-replay",
                command: command,
                nativePlan: invalidPlan,
                physicalIndexName: "__groundwork_pk_elsa_structured_logs");

            Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
                "sqlserver",
                fixture.Adapter,
                fixture.Route,
                fixture.Path));
        }
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
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("mongodb")]
    public void Trace_summary_native_route_binds_the_persisted_trace_key_order(string provider)
    {
        using var fixture = Fixture.Create(provider, "traces-by-last-seen");

        DiagnosticsNativePlanContract.ValidateEnvelope(
            provider,
            fixture.Adapter,
            fixture.Route,
            fixture.Path);
    }

    [Fact]
    public void PostgreSql_trace_summary_rejects_reencoding_the_persisted_ordinal_key()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "traces-by-last-seen");
        var command = ProviderRenderedRelationalCommand("postgresql", specification);
        const string physicalKey = "(\"__groundwork_ordinal_traceKey\" COLLATE \"C\")";
        Assert.Contains(physicalKey, command, StringComparison.Ordinal);
        Assert.DoesNotContain("string_agg", command, StringComparison.Ordinal);
        using var fixture = Fixture.Create(
            "postgresql",
            specification.RouteIdentity,
            command: command.Replace(physicalKey, PostgreSqlOrdinalKey(physicalKey), StringComparison.Ordinal));

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "postgresql",
            fixture.Adapter,
            fixture.Route,
            fixture.Path));
    }

    [Fact]
    public void Mongo_explain_command_binds_the_physical_scoped_collection_and_actual_aggregate_shape()
    {
        using var fixture = Fixture.Create("mongodb", "resources-by-status");

        DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path);
    }

    [Fact]
    public void Mongo_explain_command_rejects_stale_direct_id_order_key_optimization()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var command = JsonNode.Parse(Fixture.MongoAggregateCommand(specification))!.AsObject();
        var pipeline = command["pipeline"]!.AsArray();
        var match = pipeline[0]!.DeepClone();
        pipeline.Clear();
        pipeline.Add(match);
        pipeline.Add(MongoOrdinalKeyStage(2, "id"));
        pipeline.Add(JsonNode.Parse("""
            {"$sort":{"lastSeen":-1,"idOrderKey":1,"_groundwork_ordinal_key_2":1}}
            """));
        pipeline.Add(new JsonObject { ["$limit"] = specification.FiniteLimit + 1 });
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            command: command.ToJsonString(),
            nativePlan: """
                {
                  "queryPlanner": {
                    "winningPlan": {
                      "stage": "SORT",
                      "sortPattern": { "lastSeen": -1, "idOrderKey": 1, "_groundwork_ordinal_key_2": 1 },
                      "limitAmount": 128,
                      "inputStage": { "stage": "COLLSCAN", "direction": "forward" }
                    }
                  }
                }
                """,
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification);

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Fact]
    public void Mongo_explain_command_accepts_preview_11_non_null_index_with_rendered_ordinal_helpers()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var command = JsonNode.Parse(Fixture.MongoAggregateCommand(specification))!.AsObject();
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            command: command.ToJsonString(),
            nativePlan: MongoBoundedExplainPlan(specification),
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification);

        DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path);
    }

    [Fact]
    public void Mongo_bounded_plan_order_must_match_the_retained_command_order()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var nativePlan = JsonNode.Parse(MongoBoundedExplainPlan(specification))!.AsObject();
        var sort = nativePlan["stages"]!.AsArray()
            .Single(stage => stage!["$sort"] is not null)!["$sort"]!.AsObject();
        sort["sortKey"]!.AsObject()["lastSeen"] = 1;
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            nativePlan: nativePlan.ToJsonString(),
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification);

        var exception = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateEnvelope(
                "mongodb",
                fixture.Adapter,
                fixture.Route,
                fixture.Path));

        Assert.Contains("complete effective ordering", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mongo_order_helpers_must_precede_the_sort_stage()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var command = JsonNode.Parse(Fixture.MongoAggregateCommand(specification))!.AsObject();
        var pipeline = command["pipeline"]!.AsArray();
        var ordinalId = pipeline[2];
        pipeline.RemoveAt(2);
        pipeline.Insert(3, ordinalId);
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            command: command.ToJsonString(),
            nativePlan: MongoBoundedExplainPlan(specification),
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification);

        var exception = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateEnvelope(
                "mongodb",
                fixture.Adapter,
                fixture.Route,
                fixture.Path));

        Assert.Contains("complete effective ordering", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mongo_ordinal_helper_must_bind_the_exact_function_and_source_column()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var command = JsonNode.Parse(Fixture.MongoAggregateCommand(specification))!.AsObject();
        var pipeline = command["pipeline"]!.AsArray();
        pipeline[2] = MongoOrdinalKeyStage(2, "id", "function(value) { return value; }");
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            command: command.ToJsonString(),
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification);

        var exception = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateEnvelope(
                "mongodb",
                fixture.Adapter,
                fixture.Route,
                fixture.Path));

        Assert.Contains("complete effective ordering", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mongo_scoped_collection_evidence_passes_complete_artifact_admission_without_a_synthetic_scope_predicate()
    {
        using var fixture = Fixture.Create("mongodb", "resources-by-last-seen");
        using var output = new TemporaryDirectory();
        const string rawPlanReference = "resources-by-last-seen.raw.json";
        var rawPlanPath = Path.Combine(output.FullName, rawPlanReference);
        File.Copy(fixture.Path, rawPlanPath);
        var route = fixture.Route with
        {
            RawPlanReference = rawPlanReference,
            RawPlanSha256 = Sha256(File.ReadAllBytes(rawPlanPath))
        };
        var workload = WorkloadCatalog.Load(Repository.Root())
            .Workloads[ReproducibleWorkloadScenarioCatalog.DiagnosticsWorkloadId] with
        {
            RequiredNativeRoutes = [route.RouteIdentity]
        };
        var configuration = new Dictionary<string, string> { ["topology"] = "replica-set" };
        var topology = workload.RequiredProviderEvidence["mongodb"];
        const string identity = "diagnostics-mongo-plan";
        const string evidenceReference = "diagnostics-mongo-plan.json";
        var evidenceDocument = new NativePlanEvidenceDocument(
            2,
            "diagnostics-cohort",
            "diagnostics-set",
            workload.Id,
            workload.Version,
            "mongodb",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "ordinary-groundwork-diagnostics-units",
            "100k",
            new string('a', 40),
            new string('b', 64),
            new string('c', 64),
            new string('d', 64),
            "8.0",
            topology,
            configuration,
            workload.Input.Seed,
            workload.Input.FingerprintSha256,
            identity,
            [route]);
        var evidenceBytes = JsonSerializer.SerializeToUtf8Bytes(evidenceDocument, ArtifactStore.JsonOptions);
        File.WriteAllBytes(Path.Combine(output.FullName, evidenceReference), evidenceBytes);
        var request = new RunRequest(
            evidenceDocument.ComparisonCohortId,
            evidenceDocument.MeasurementSetId,
            workload.Id,
            workload.Version,
            evidenceDocument.Provider,
            evidenceDocument.Adapter,
            evidenceDocument.PhysicalForm,
            evidenceDocument.Scale,
            evidenceDocument.CommitSha,
            evidenceDocument.HarnessAssemblySha256,
            new Dictionary<string, string> { ["Groundwork.MongoDb"] = "0.4.0-preview.16" },
            evidenceDocument.CompositionFingerprint,
            evidenceDocument.HostFingerprintSha256,
            evidenceDocument.ProviderVersion,
            topology,
            configuration,
            workload.Input.Seed,
            workload.Input.FingerprintSha256,
            identity,
            evidenceReference,
            Sha256(evidenceBytes),
            ProcessKind.Measured,
            1);
        var evidence = new CorrectnessEvidence(
            workload.Correctness.ResultDigestSha256,
            request.ProviderVersion,
            topology,
            configuration,
            new NativePlanEvidence(identity, evidenceReference, request.NativePlanContentSha256, [route]));

        ArtifactAdmission.ValidateCorrectness(workload, request, evidence, output.FullName);
    }

    [Fact]
    public void Mongo_explain_command_rejects_the_logical_collection_name()
    {
        using var fixture = Fixture.Create(
            "mongodb",
            "resources-by-last-seen",
            command: Fixture.MongoAggregateCommand(
                DiagnosticsNativePlanContract.For(
                    DiagnosticsNativePlanContract.GroundworkAdapter,
                    "resources-by-last-seen"),
                collection: "elsa_otel_resources_v2"));

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Fact]
    public void Mongo_explain_command_rejects_a_synthetic_scope_predicate()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            command: Fixture.MongoAggregateCommand(
                specification,
                match: "{\"__groundwork_scope\":{\"$eq\":\"scope\"}}"));

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Fact]
    public void Mongo_explain_command_rejects_a_wrong_route_predicate()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-status");
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            command: Fixture.MongoAggregateCommand(
                specification,
                match: "{\"serviceNameKey\":\"wrong-route\"}"));

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Fact]
    public void Mongo_explain_command_rejects_a_missing_actual_command()
    {
        using var fixture = Fixture.Create(
            "mongodb",
            "resources-by-last-seen",
            attachCommandToNativePlan: false);

        var exception = Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb", fixture.Adapter, fixture.Route, fixture.Path));
        Assert.Contains("actual aggregate/find command", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mongo_explain_command_rejects_ambiguous_actual_commands()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var command = Fixture.MongoAggregateCommand(specification);
        var second = Fixture.MongoAggregateCommand(specification, collection: Fixture.MongoPhysicalCollection(specification) + "_other");
        var nativePlan = $$"""
            {
              "queryPlanner": { "winningPlan": { "stage": "IXSCAN", "indexName": "elsa_otel_resources_last_seen" } },
              "command": {{command}},
              "nested": { "command": {{second}} }
            }
            """;
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            command,
            nativePlan,
            attachCommandToNativePlan: false);

        var exception = Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb", fixture.Adapter, fixture.Route, fixture.Path));
        Assert.Contains("exactly one actual aggregate/find command", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mongo_explain_command_must_match_the_command_retained_in_the_explain_response()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        using var fixture = Fixture.Create("mongodb", specification.RouteIdentity);
        var envelope = JsonNode.Parse(File.ReadAllText(fixture.Path))!.AsObject();
        envelope["commandText"] = Fixture.MongoAggregateCommand(
            specification,
            match: "{\"serviceNameKey\":\"not-this-route\"}");
        File.WriteAllText(fixture.Path, envelope.ToJsonString());

        var exception = Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb", fixture.Adapter, fixture.Route, fixture.Path));
        Assert.Contains("does not match the actual command", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mongo_aggregate_command_requires_all_order_terms_and_the_frozen_limit()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var command = JsonNode.Parse(Fixture.MongoAggregateCommand(specification))!.AsObject();
        var pipeline = command["pipeline"]!.AsArray();
        pipeline[3]!["$sort"]!.AsObject().Remove("_groundwork_ordinal_key_2");
        pipeline[4]!["$limit"] = 1;
        using var fixture = Fixture.Create("mongodb", specification.RouteIdentity, command: command.ToJsonString());

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Fact]
    public void Mongo_aggregate_command_rejects_a_limit_before_its_sort()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var command = JsonNode.Parse(Fixture.MongoAggregateCommand(specification))!.AsObject();
        var pipeline = command["pipeline"]!.AsArray();
        var limit = pipeline[4];
        pipeline.RemoveAt(4);
        pipeline.Insert(3, limit);
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            command: command.ToJsonString());

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Fact]
    public void Mongo_aggregate_command_rejects_a_mutating_stage_before_its_sort()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var command = JsonNode.Parse(Fixture.MongoAggregateCommand(specification))!.AsObject();
        command["pipeline"]!.AsArray().Insert(3, JsonNode.Parse("""
            {"$set":{"lastSeen":0}}
            """));
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            command: command.ToJsonString());

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Fact]
    public void Mongo_aggregate_command_rejects_a_stage_after_its_limit()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var command = JsonNode.Parse(Fixture.MongoAggregateCommand(specification))!.AsObject();
        command["pipeline"]!.AsArray().Add(JsonNode.Parse("""
            {"$project":{"id":1}}
            """));
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            command: command.ToJsonString());

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Theory]
    [InlineData("resources-by-last-seen")]
    [InlineData("resources-by-status")]
    [InlineData("resources-by-service")]
    public void Mongo_frozen_resource_catalog_classifies_an_exact_collection_scan_sort(string route)
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            route);
        var nativePlan = MongoBoundedExplainPlan(specification);

        var classification = DiagnosticsNativePlanContract.ClassifyPlan(
            "mongodb",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            specification,
            nativePlan);

        Assert.Equal(DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification, classification);
    }

    [Theory]
    [InlineData("resources-by-last-seen")]
    [InlineData("resources-by-status")]
    [InlineData("resources-by-service")]
    public void Mongo_frozen_resource_catalog_admits_an_explicit_bounded_collection_scan_sort(string route)
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            route);
        using var fixture = Fixture.Create(
            "mongodb",
            route,
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            nativePlan: MongoBoundedExplainPlan(specification));

        DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path);
    }

    [Fact]
    public void Mongo_captured_aggregate_explain_fixture_survives_serialized_envelope_admission()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var nativePlan = ReadMongoExplainFixture("mongodb-resources-by-last-seen-explain.json");
        var command = JsonNode.Parse(nativePlan)!["command"]!.ToJsonString();
        Assert.Equal(
            DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            DiagnosticsNativePlanContract.ClassifyPlan(
                "mongodb",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                specification,
                nativePlan));
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            command,
            nativePlan,
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification);

        DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path);
    }

    [Theory]
    [InlineData("resources-by-last-seen")]
    [InlineData("resources-by-status")]
    [InlineData("resources-by-service")]
    public void Mongo_captured_ixscan_aggregate_sort_admits_each_bounded_resource_route(string route)
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            route);
        var nativePlan = MongoCapturedResourceIndexPlan(specification);
        var nativePlanDocument = JsonNode.Parse(nativePlan)!.AsObject();
        nativePlanDocument["stages"]!.AsArray()
            .Single(stage => stage!["$cursor"] is not null)!["$cursor"]!["queryPlanner"]!["rejectedPlans"] =
            new JsonArray { new JsonObject { ["stage"] = "COLLSCAN" } };
        nativePlan = nativePlanDocument.ToJsonString();
        var command = JsonNode.Parse(nativePlan)!["command"]!.ToJsonString();

        Assert.Equal(
            DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            DiagnosticsNativePlanContract.ClassifyPlan(
                "mongodb",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                specification,
                nativePlan));
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            command,
            nativePlan,
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification);

        DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path);
    }

    [Fact]
    public void Mongo_captured_ixscan_does_not_rescue_a_wrong_winner_with_a_rejected_expected_index()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-service");
        var nativePlan = JsonNode.Parse(ReadMongoExplainFixture(
            "mongodb-resources-by-service-explain.json"))!.AsObject();
        var queryPlanner = nativePlan["stages"]!.AsArray()
            .Single(stage => stage!["$cursor"] is not null)!["$cursor"]!["queryPlanner"]!.AsObject();
        queryPlanner["winningPlan"]!["inputStage"]!["indexName"] = "wrong_winning_index";
        queryPlanner["rejectedPlans"] = new JsonArray
        {
            new JsonObject
            {
                ["stage"] = "IXSCAN",
                ["indexName"] = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("mongodb", specification)
            }
        };
        var serializedPlan = nativePlan.ToJsonString();

        Assert.Equal(
            DiagnosticsNativePlanContract.IndexSearchPlanClassification,
            DiagnosticsNativePlanContract.ClassifyPlan(
                "mongodb",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                specification,
                serializedPlan));
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            JsonNode.Parse(serializedPlan)!["command"]!.ToJsonString(),
            serializedPlan);

        var exception = Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path));

        Assert.False(DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception));
        Assert.Contains("exact MongoDB index scan", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mongo_captured_ixscan_rejects_a_mutated_physical_key_pattern()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-service");
        var nativePlan = JsonNode.Parse(ReadMongoExplainFixture(
            "mongodb-resources-by-service-explain.json"))!.AsObject();
        var winningPlan = nativePlan["stages"]!.AsArray()
            .Single(stage => stage!["$cursor"] is not null)!["$cursor"]!["queryPlanner"]!["winningPlan"]!;
        winningPlan!["inputStage"]!["keyPattern"]!["lastSeen"] = 1;
        var serializedPlan = nativePlan.ToJsonString();

        Assert.Equal(
            DiagnosticsNativePlanContract.IndexSearchPlanClassification,
            DiagnosticsNativePlanContract.ClassifyPlan(
                "mongodb",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                specification,
                serializedPlan));
        AssertBoundedCatalogRejected("mongodb", serializedPlan, specification.RouteIdentity,
            nativePlan["command"]!.ToJsonString());
    }

    [Theory]
    [InlineData("direction")]
    [InlineData("isMultiKey")]
    [InlineData("isSparse")]
    [InlineData("isPartial")]
    [InlineData("spills")]
    public void Mongo_captured_ixscan_rejects_mutated_bounded_plan_metadata(string mutation)
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-service");
        var nativePlan = JsonNode.Parse(ReadMongoExplainFixture(
            "mongodb-resources-by-service-explain.json"))!.AsObject();
        var cursor = nativePlan["stages"]!.AsArray()
            .Single(stage => stage!["$cursor"] is not null)!["$cursor"]!.AsObject();
        var winningPlan = cursor["queryPlanner"]!["winningPlan"]!.AsObject();
        var inputStage = winningPlan["inputStage"]!.AsObject();
        if (mutation == "spills")
        {
            nativePlan["stages"]!.AsArray()
                .Single(stage => stage!["$sort"] is not null)![mutation] = 1;
        }
        else if (mutation == "direction")
            inputStage[mutation] = "backward";
        else
            inputStage[mutation] = true;

        Assert.Equal(
            DiagnosticsNativePlanContract.IndexSearchPlanClassification,
            DiagnosticsNativePlanContract.ClassifyPlan(
                "mongodb",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                specification,
                nativePlan.ToJsonString()));
        AssertBoundedCatalogRejected("mongodb", nativePlan.ToJsonString(), specification.RouteIdentity,
            nativePlan["command"]!.ToJsonString());
    }

    [Fact]
    public void Mongo_strict_index_admission_uses_only_the_winning_plan()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-service");
        var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("mongodb", specification);
        var nativePlan = new JsonObject
        {
            ["queryPlanner"] = new JsonObject
            {
                ["winningPlan"] = new JsonObject { ["stage"] = "IXSCAN", ["indexName"] = physicalIndex },
                ["rejectedPlans"] = new JsonArray
                {
                    new JsonObject { ["stage"] = "IXSCAN", ["indexName"] = "wrong_rejected_index" }
                }
            },
            ["executionStats"] = new JsonObject
            {
                ["executionStages"] = new JsonObject { ["stage"] = "IXSCAN", ["indexName"] = physicalIndex }
            }
        };
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            nativePlan: nativePlan.ToJsonString());

        DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path);
    }

    [Fact]
    public void Mongo_strict_admission_binds_the_expected_index_to_the_winning_ixscan()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-service");
        var nativePlan = new JsonObject
        {
            ["queryPlanner"] = new JsonObject
            {
                ["winningPlan"] = new JsonObject
                {
                    ["stage"] = "FETCH",
                    ["indexName"] = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("mongodb", specification),
                    ["inputStage"] = new JsonObject
                    {
                        ["stage"] = "IXSCAN",
                        ["indexName"] = "wrong_winning_index"
                    }
                }
            }
        };
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            nativePlan: nativePlan.ToJsonString());

        var exception = Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path));

        Assert.False(DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception));
        Assert.Contains("exact MongoDB index scan", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mongo_strict_admission_rejects_a_non_string_stage_as_a_contract_failure(bool nested)
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-service");
        var winningPlan = nested
            ? new JsonObject
            {
                ["stage"] = "IXSCAN",
                ["indexName"] = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("mongodb", specification),
                ["inputStage"] = new JsonObject { ["stage"] = 1 }
            }
            : new JsonObject { ["stage"] = 1 };
        var nativePlan = new JsonObject
        {
            ["queryPlanner"] = new JsonObject
            {
                ["winningPlan"] = winningPlan
            }
        };
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            nativePlan: nativePlan.ToJsonString());

        var exception = Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path));

        Assert.Contains("exact MongoDB index scan", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mongo_ixscan_with_an_aggregate_pipeline_sort_remains_expected_blocked()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-service");
        var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("mongodb", specification);
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            nativePlan: MongoBoundedExplainPlan(
                specification,
                new JsonObject
                {
                    ["stage"] = "FETCH",
                    ["inputStage"] = new JsonObject { ["stage"] = "IXSCAN", ["indexName"] = physicalIndex }
                }));

        var exception = Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path));

        Assert.True(DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception));
        Assert.Equal("native-plan.sort-or-materialization-spill",
            DiagnosticsNativePlanContract.BlockedPlanReasonCode(exception));
    }

    [Fact]
    public void Mongo_wrong_winning_index_is_not_rescued_by_a_rejected_plan()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-service");
        var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("mongodb", specification);
        var nativePlan = MongoBoundedExplainPlan(
            specification,
            new JsonObject
            {
                ["stage"] = "FETCH",
                ["inputStage"] = new JsonObject { ["stage"] = "IXSCAN", ["indexName"] = "wrong_winning_index" }
            },
            rejectedIndex: physicalIndex);
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            nativePlan: nativePlan);

        var exception = Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path));

        Assert.False(DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception));
        Assert.Contains("exact MongoDB index scan", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mongo_bounded_explain_rejects_an_unknown_winning_stage_wrapper()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var nativePlan = JsonNode.Parse(MongoBoundedExplainPlan(specification))!.AsObject();
        var winningPlan = nativePlan["stages"]!.AsArray()
            .Single(stage => stage!["$cursor"] is not null)!["$cursor"]!["queryPlanner"]!["winningPlan"]!
            .AsObject();
        winningPlan["inputStage"] = new JsonObject { ["stage"] = "MYSTERY" };
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            nativePlan: nativePlan.ToJsonString(),
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification);

        var exception = Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path));

        Assert.True(DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception));
    }

    [Fact]
    public void Mongo_bounded_explain_rejects_a_stage_between_sort_and_projection()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var nativePlan = JsonNode.Parse(MongoBoundedExplainPlan(specification))!.AsObject();
        var stages = nativePlan["stages"]!.AsArray();
        stages.Insert(stages.Count - 1, new JsonObject { ["$unwind"] = "$payload" });
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            nativePlan: nativePlan.ToJsonString(),
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification);

        var exception = Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path));

        Assert.True(DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception));
    }

    [Fact]
    public void Mongo_bounded_explain_rejects_a_non_object_root_containing_a_winning_plan()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var nativePlan = new JsonArray
        {
            new JsonObject
            {
                ["winningPlan"] = new JsonObject
                {
                    ["stage"] = "COLLSCAN",
                    ["direction"] = "forward"
                }
            }
        };
        Assert.Equal(
            DiagnosticsNativePlanContract.IndexSearchPlanClassification,
            DiagnosticsNativePlanContract.ClassifyPlan(
                "mongodb",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                specification,
                nativePlan.ToJsonString()));
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            nativePlan: nativePlan.ToJsonString(),
            attachCommandToNativePlan: false,
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification);

        var exception = Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path));

        Assert.False(DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception));
    }

    [Theory]
    [InlineData("\"128\"")]
    [InlineData("{}")]
    public void Mongo_bounded_explain_rejects_a_non_numeric_pipeline_limit(string limitJson)
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var nativePlan = JsonNode.Parse(MongoBoundedExplainPlan(specification))!.AsObject();
        var sortStage = nativePlan["stages"]!.AsArray()
            .Single(stage => stage!["$sort"] is not null)!["$sort"]!.AsObject();
        sortStage["limit"] = JsonNode.Parse(limitJson)!.DeepClone();
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            nativePlan: nativePlan.ToJsonString(),
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification);

        var exception = Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path));

        Assert.True(DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception));
    }

    [Fact]
    public void Mongo_bounded_explain_rejects_multiple_operators_in_one_pipeline_stage()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var nativePlan = JsonNode.Parse(MongoBoundedExplainPlan(specification))!.AsObject();
        var setStage = nativePlan["stages"]!.AsArray()
            .First(stage => stage!["$set"] is not null)!.AsObject();
        setStage["$unwind"] = "$payload";
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            nativePlan: nativePlan.ToJsonString(),
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification);

        var exception = Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path));

        Assert.True(DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception));
    }

    [Fact]
    public void Mongo_bounded_explain_rejects_duplicate_projection_helpers()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        const string validProjection =
            "\"_id\":true,\"_groundwork_ordinal_key_1\":false,\"_groundwork_ordinal_key_2\":false";
        const string duplicateProjection =
            "\"_id\":true,\"_groundwork_ordinal_key_1\":false,\"_groundwork_ordinal_key_1\":false";
        var nativePlan = MongoBoundedExplainPlan(specification);
        var malformedPlan = nativePlan.Replace(validProjection, duplicateProjection, StringComparison.Ordinal);
        Assert.NotEqual(nativePlan, malformedPlan);
        using var fixture = Fixture.Create(
            "mongodb",
            specification.RouteIdentity,
            nativePlan: malformedPlan,
            attachCommandToNativePlan: false,
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification);

        var exception = Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path));

        Assert.True(DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception));
    }

    [Theory]
    [InlineData("postgresql", "resources-by-last-seen")]
    [InlineData("postgresql", "resources-by-status")]
    [InlineData("postgresql", "resources-by-service")]
    [InlineData("sqlserver", "resources-by-last-seen")]
    [InlineData("sqlserver", "resources-by-status")]
    [InlineData("sqlserver", "resources-by-service")]
    public void Relational_frozen_resource_catalog_admits_its_exact_bounded_scan_sort(
        string provider,
        string route)
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            route);
        var nativePlan = BoundedCatalogPlan(provider);

        Assert.Equal(
            DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            DiagnosticsNativePlanContract.ClassifyPlan(
                provider,
                DiagnosticsNativePlanContract.GroundworkAdapter,
                specification,
                nativePlan));

        using var fixture = Fixture.Create(
            provider,
            route,
            command: ProviderRenderedRelationalCommand(provider, specification),
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            nativePlan: nativePlan);
        DiagnosticsNativePlanContract.ValidateEnvelope(
            provider,
            fixture.Adapter,
            fixture.Route,
            fixture.Path);
    }

    [Fact]
    public void SqlServer_renderer_scope_equality_requires_its_exact_length_companion()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var command = ProviderRenderedRelationalCommand("sqlserver", specification).Replace(
            "DATALENGTH([__groundwork_scope] COLLATE Latin1_General_100_BIN2) = DATALENGTH(@p0) AND ",
            string.Empty,
            StringComparison.Ordinal);
        using var fixture = Fixture.Create(
            "sqlserver",
            specification.RouteIdentity,
            command: command,
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            nativePlan: BoundedCatalogPlan("sqlserver"));

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "sqlserver", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Fact]
    public void SqlServer_renderer_scope_length_companion_must_bind_the_same_parameter()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var command = ProviderRenderedRelationalCommand("sqlserver", specification).Replace(
            "DATALENGTH(@p0)",
            "DATALENGTH(@wrong)",
            StringComparison.Ordinal);
        using var fixture = Fixture.Create(
            "sqlserver",
            specification.RouteIdentity,
            command: command,
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            nativePlan: BoundedCatalogPlan("sqlserver"));

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "sqlserver", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Theory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    public void Relational_bounded_catalog_scan_sort_rejects_spill_or_materialization(string provider)
    {
        var nativePlan = provider switch
        {
            "postgresql" => BoundedCatalogPlan(provider).Replace(
                "\"Node Type\":\"Sort\"",
                "\"Node Type\":\"Sort\",\"Sort Method\":\"external merge\",\"Sort Space Type\":\"Disk\"",
                StringComparison.Ordinal),
            "sqlserver" => BoundedCatalogPlan(provider).Replace(
                "<RelOp PhysicalOp=\"Sort\">",
                "<RelOp PhysicalOp=\"Sort\"><Warnings><SpillOccurred /></Warnings>",
                StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        AssertBoundedCatalogRejected(provider, nativePlan);
    }

    [Fact]
    public void Sqlite_resource_catalog_remains_a_strict_index_search_contract()
    {
        const string provider = "sqlite";
        const string route = "resources-by-last-seen";
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            route);
        var nativePlan = BoundedCatalogPlan(provider);

        Assert.Equal(
            DiagnosticsNativePlanContract.IndexSearchPlanClassification,
            DiagnosticsNativePlanContract.ClassifyPlan(
                provider,
                DiagnosticsNativePlanContract.GroundworkAdapter,
                specification,
                nativePlan));

        using var fixture = Fixture.Create(
            provider,
            route,
            command: RelationalCommand(provider, specification),
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            nativePlan: nativePlan);
        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            provider,
            fixture.Adapter,
            fixture.Route,
            fixture.Path));
    }

    [Theory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    public void Relational_bounded_catalog_scan_sort_rejects_incomplete_command_ordering(string provider)
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var command = RelationalCommand(provider, specification)
            .Replace(", idOrderKey ASC", string.Empty, StringComparison.Ordinal);
        using var fixture = Fixture.Create(
            provider,
            specification.RouteIdentity,
            command: command,
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            nativePlan: BoundedCatalogPlan(provider));

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            provider,
            fixture.Adapter,
            fixture.Route,
            fixture.Path));
    }

    [Fact]
    public void SqlServer_bounded_catalog_scan_sort_rejects_a_wrong_physical_sort_direction()
    {
        var nativePlan = BoundedCatalogPlan("sqlserver").Replace(
            "<OrderByColumn Ascending=\"0\"><ColumnReference Column=\"[lastSeen]\" /></OrderByColumn>",
            "<OrderByColumn Ascending=\"1\"><ColumnReference Column=\"[lastSeen]\" /></OrderByColumn>",
            StringComparison.Ordinal);

        AssertBoundedCatalogRejected("sqlserver", nativePlan);
    }

    [Fact]
    public void SqlServer_bounded_catalog_scan_sort_rejects_reordered_physical_sort_keys()
    {
        var document = System.Xml.Linq.XDocument.Parse(BoundedCatalogPlan("sqlserver"));
        var orderBy = document.Descendants().Single(element => element.Name.LocalName == "OrderBy");
        var first = orderBy.Elements().ElementAt(1).Descendants().Single(element => element.Name.LocalName == "ColumnReference");
        var second = orderBy.Elements().ElementAt(2).Descendants().Single(element => element.Name.LocalName == "ColumnReference");
        (first.Attribute("Column")!.Value, second.Attribute("Column")!.Value) =
            (second.Attribute("Column")!.Value, first.Attribute("Column")!.Value);

        AssertBoundedCatalogRejected("sqlserver", document.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
    }

    [Fact]
    public void SqlServer_bounded_catalog_scan_sort_rejects_a_noncanonical_value_expression()
    {
        var document = System.Xml.Linq.XDocument.Parse(BoundedCatalogPlan("sqlserver"));
        var definition = document.Descendants().Single(element =>
            element.Name.LocalName == "DefinedValue" &&
            element.Descendants().Any(child =>
                child.Name.LocalName == "ColumnReference" &&
                child.Attribute("Column")?.Value == "[Expr1005]"));
        definition.Descendants().Single(element => element.Name.LocalName == "ScalarOperator")
            .SetAttributeValue("ScalarString", "REVERSE([idOrderKey] COLLATE Latin1_General_100_BIN2)");

        AssertBoundedCatalogRejected("sqlserver", document.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
    }

    [Fact]
    public void SqlServer_bounded_catalog_scan_sort_rejects_a_direct_ordinal_value_reference()
    {
        var document = System.Xml.Linq.XDocument.Parse(BoundedCatalogPlan("sqlserver"));
        var orderBy = document.Descendants().Single(element => element.Name.LocalName == "OrderBy");
        var ordinalValue = orderBy.Elements().ElementAt(1).Descendants()
            .Single(element => element.Name.LocalName == "ColumnReference");
        ordinalValue.SetAttributeValue("Column", "[idOrderKey]");

        AssertBoundedCatalogRejected("sqlserver", document.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
    }

    [Fact]
    public void SqlServer_bounded_catalog_scan_sort_rejects_an_unexpected_null_rank_expression()
    {
        var document = System.Xml.Linq.XDocument.Parse(BoundedCatalogPlan("sqlserver"));
        var orderBy = document.Descendants().Single(element => element.Name.LocalName == "OrderBy");
        orderBy.AddFirst(System.Xml.Linq.XElement.Parse(
            "<OrderByColumn Ascending=\"1\"><ColumnReference Column=\"[Expr1010]\" /></OrderByColumn>"));
        var definitions = document.Descendants().Single(element => element.Name.LocalName == "DefinedValues");
        definitions.AddFirst(System.Xml.Linq.XElement.Parse(
            "<DefinedValue><ColumnReference Column=\"[Expr1010]\" /><ScalarOperator ScalarString=\"CASE WHEN [lastSeen] IS NULL THEN (1) ELSE (0) END\" /></DefinedValue>"));

        AssertBoundedCatalogRejected("sqlserver", document.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
    }

    [Fact]
    public void SqlServer_bounded_catalog_command_rejects_a_non_ordinal_datalength_companion()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var command = ProviderRenderedRelationalCommand("sqlserver", specification).Replace(
            "DATALENGTH([idOrderKey] COLLATE Latin1_General_100_BIN2)",
            "DATALENGTH([lastSeen])",
            StringComparison.Ordinal);
        using var fixture = Fixture.Create(
            "sqlserver",
            specification.RouteIdentity,
            command: command,
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            nativePlan: BoundedCatalogPlan("sqlserver"));

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "sqlserver",
            fixture.Adapter,
            fixture.Route,
            fixture.Path));
    }

    [Fact]
    public void SqlServer_command_rejects_missing_null_ranks_for_conservative_nullable_ordering()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.EfAdapter,
            "resources-by-last-seen");

        Assert.True(specification.RequiresNullRank("lastSeen"));
        using var fixture = Fixture.Create(
            "sqlserver",
            specification.RouteIdentity,
            command: SqlServerNullableOrderingCommand(specification, includeNullRanks: false),
            nativePlan: SqlServerIndexSeekPlan(specification),
            adapter: DiagnosticsNativePlanContract.EfAdapter);

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "sqlserver", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Fact]
    public void SqlServer_command_accepts_null_ranks_for_conservative_nullable_ordering()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.EfAdapter,
            "resources-by-last-seen");

        Assert.True(specification.RequiresNullRank("lastSeen"));
        using var fixture = Fixture.Create(
            "sqlserver",
            specification.RouteIdentity,
            command: SqlServerNullableOrderingCommand(specification, includeNullRanks: true),
            nativePlan: SqlServerIndexSeekPlan(specification),
            adapter: DiagnosticsNativePlanContract.EfAdapter);

        DiagnosticsNativePlanContract.ValidateEnvelope(
            "sqlserver", fixture.Adapter, fixture.Route, fixture.Path);
    }

    [Fact]
    public void PostgreSql_bounded_catalog_scan_sort_rejects_incomplete_native_sort_keys()
    {
        var plan = JsonNode.Parse(BoundedCatalogPlan("postgresql"))!.AsArray();
        var sortKeys = plan[0]!["Plan"]!["Plans"]![0]!["Sort Key"]!.AsArray();
        sortKeys.RemoveAt(2);

        AssertBoundedCatalogRejected("postgresql", plan.ToJsonString());
    }

    [Fact]
    public void PostgreSql_bounded_catalog_command_rejects_a_mutated_ordinal_expression()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var command = ProviderRenderedRelationalCommand("postgresql", specification).Replace(
            "ascii(chars.ch) <= 65535",
            "ascii(chars.ch) <= 65534",
            StringComparison.Ordinal);
        using var fixture = Fixture.Create(
            "postgresql",
            specification.RouteIdentity,
            command: command,
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            nativePlan: BoundedCatalogPlan("postgresql"));

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "postgresql", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Fact]
    public void PostgreSql_bounded_catalog_command_rejects_a_noncanonical_ordinal_collation()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var command = ProviderRenderedRelationalCommand("postgresql", specification).Replace(
            "COLLATE \"C\"",
            "COLLATE \"en_US\"",
            StringComparison.Ordinal);
        using var fixture = Fixture.Create(
            "postgresql",
            specification.RouteIdentity,
            command: command,
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            nativePlan: BoundedCatalogPlan("postgresql"));

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "postgresql", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Fact]
    public void PostgreSql_bounded_catalog_command_accepts_index_ddl_null_placement_for_non_nullable_ordering()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        using var fixture = Fixture.Create(
            "postgresql",
            specification.RouteIdentity,
            command: ProviderRenderedRelationalCommand("postgresql", specification),
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            nativePlan: BoundedCatalogPlan("postgresql"));

        DiagnosticsNativePlanContract.ValidateEnvelope(
            "postgresql", fixture.Adapter, fixture.Route, fixture.Path);
    }

    [Fact]
    public void PostgreSql_bounded_catalog_command_rejects_mismatched_non_nullable_null_placement()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var command = ProviderRenderedRelationalCommand("postgresql", specification).Replace(
            "DESC NULLS LAST",
            "DESC NULLS FIRST",
            StringComparison.Ordinal);
        using var fixture = Fixture.Create(
            "postgresql",
            specification.RouteIdentity,
            command: command,
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            nativePlan: BoundedCatalogPlan("postgresql"));

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "postgresql", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Theory]
    [InlineData("resources-by-last-seen", "postgresql-bounded-resources-by-last-seen.json")]
    [InlineData("resources-by-status", "postgresql-bounded-resources-by-status.json")]
    [InlineData("resources-by-service", "postgresql-bounded-resources-by-service.json")]
    public void PostgreSql_real_bounded_catalog_plans_survive_serialized_envelope_admission(
        string route,
        string fixtureName)
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            route);
        var nativePlan = ReadRealPostgreSqlBoundedCatalogPlan(fixtureName);
        using var fixture = Fixture.Create(
            "postgresql",
            route,
            command: ProviderRenderedRelationalCommand("postgresql", specification),
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            nativePlan: nativePlan);

        DiagnosticsNativePlanContract.ValidateEnvelope(
            "postgresql",
            fixture.Adapter,
            fixture.Route,
            fixture.Path);
    }

    [Theory]
    [InlineData("simple-null-placement")]
    [InlineData("simple-direction")]
    [InlineData("subplan-null-placement")]
    [InlineData("subplan-direction")]
    public void PostgreSql_real_bounded_catalog_plan_rejects_wrong_native_null_order_or_direction(
        string mutation)
    {
        var nativePlan = MutateRealPostgreSqlBoundedCatalogPlan(mutation);

        AssertBoundedCatalogRejected("postgresql", nativePlan);
    }

    [Theory]
    [InlineData("traces-by-last-seen")]
    [InlineData("metrics-by-last-seen")]
    [InlineData("logs-by-last-seen")]
    public void PostgreSql_real_bounded_catalog_sort_plan_remains_rejected_for_non_resource_routes(
        string route)
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            route);
        using var fixture = Fixture.Create(
            "postgresql",
            route,
            command: PostgreSqlRenderedRelationalCommandForRoute(specification),
            nativePlan: RealPostgreSqlBoundedCatalogPlanForRoute(specification));

        var exception = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateEnvelope(
                "postgresql",
                fixture.Adapter,
                fixture.Route,
                fixture.Path));
        Assert.True(
            DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception),
            exception.Message);
        Assert.Equal(
            "native-plan.sort-or-materialization-spill",
            DiagnosticsNativePlanContract.BlockedPlanReasonCode(exception));
    }

    [Fact]
    public void Non_PostgreSql_command_rejects_PostgreSql_null_placement_syntax()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "structured-log-replay");
        var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("sqlite", specification);
        using var fixture = Fixture.Create(
            "sqlite",
            specification.RouteIdentity,
            command:
                "SELECT * FROM elsa_structured_logs WHERE " +
                "(__groundwork_scope IS NOT NULL AND __groundwork_scope = @p0) AND " +
                "(sequence IS NOT NULL AND sequence > @p1 AND sequence <= @p2) " +
                "ORDER BY sequence ASC NULLS FIRST LIMIT @p3",
            nativePlan:
                $"2 0 SEARCH elsa_structured_logs USING INDEX {physicalIndex} " +
                "(__groundwork_scope=? AND sequence>? AND sequence<?)");

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "sqlite", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Fact]
    public void PostgreSql_bounded_catalog_scan_sort_rejects_an_unexpected_null_rank()
    {
        var plan = JsonNode.Parse(BoundedCatalogPlan("postgresql"))!.AsArray();
        var sortKeys = plan[0]!["Plan"]!["Plans"]![0]!["Sort Key"]!.AsArray();
        sortKeys.Insert(0, "(CASE WHEN (elsa_otel_resources_v2.\"lastSeen\" IS NULL) THEN 1 ELSE 0 END)");

        AssertBoundedCatalogRejected("postgresql", plan.ToJsonString());
    }

    [Theory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    public void Relational_bounded_catalog_scan_sort_requires_a_null_rank_for_declared_nullable_columns(
        string provider)
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen") with
        {
            NullableOrderingColumns = ["lastSeen"]
        };

        Assert.Equal(
            DiagnosticsNativePlanContract.IndexSearchPlanClassification,
            DiagnosticsNativePlanContract.ClassifyPlan(
                provider,
                DiagnosticsNativePlanContract.GroundworkAdapter,
                specification,
                BoundedCatalogPlan(provider)));
    }

    [Fact]
    public void PostgreSql_bounded_catalog_scan_sort_rejects_swapped_ordinal_subplan_sources()
    {
        var plan = JsonNode.Parse(BoundedCatalogPlan("postgresql"))!.AsArray();
        var subplans = plan[0]!["Plan"]!["Plans"]![0]!["Plans"]![0]!["Plans"]!.AsArray();
        var firstCall = subplans[0]!["Plans"]![0]!["Function Call"];
        var secondCall = subplans[1]!["Plans"]![0]!["Function Call"];
        subplans[0]!["Plans"]![0]!["Function Call"] = secondCall!.DeepClone();
        subplans[1]!["Plans"]![0]!["Function Call"] = firstCall!.DeepClone();

        AssertBoundedCatalogRejected("postgresql", plan.ToJsonString());
    }

    [Fact]
    public void PostgreSql_bounded_catalog_scan_sort_rejects_a_mutated_ordinal_subplan_expression()
    {
        var plan = JsonNode.Parse(BoundedCatalogPlan("postgresql"))!.AsArray();
        var aggregate = plan[0]!["Plan"]!["Plans"]![0]!["Plans"]![0]!["Plans"]![0]!;
        var output = aggregate["Output"]![0]!.GetValue<string>().Replace(
            "ascii(chars.ch) <= 65535",
            "ascii(chars.ch) <= 65534",
            StringComparison.Ordinal);
        aggregate["Output"]![0] = output;

        AssertBoundedCatalogRejected("postgresql", plan.ToJsonString());
    }

    [Theory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    public void Relational_bounded_catalog_scan_sort_rejects_an_extra_scan(string provider)
    {
        var nativePlan = provider switch
        {
            "postgresql" => BoundedCatalogPlan(provider).Replace(
                "\"Plans\":[{\"Node Type\":\"Sort\"",
                "\"Plans\":[{\"Node Type\":\"Seq Scan\",\"Relation Name\":\"other\"},{\"Node Type\":\"Sort\"",
                StringComparison.Ordinal),
            "sqlserver" => BoundedCatalogPlan(provider).Replace(
                "</ShowPlanXML>",
                "<RelOp PhysicalOp=\"Table Scan\"><TableScan><Object Table=\"[other]\" /></TableScan></RelOp></ShowPlanXML>",
                StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        AssertBoundedCatalogRejected(provider, nativePlan);
    }

    [Theory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    public void Relational_bounded_catalog_scan_sort_rejects_disconnected_topology(string provider)
    {
        var nativePlan = provider switch
        {
            "postgresql" => PostgreSqlPlanWithSwappedLimitAndSort(),
            "sqlserver" => SqlServerPlanWithDetachedSort(),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };

        AssertBoundedCatalogRejected(provider, nativePlan);
    }

    [Fact]
    public void SqlServer_bounded_catalog_scan_sort_rejects_an_unrecognized_intermediate_operator()
    {
        var nativePlan = BoundedCatalogPlan("sqlserver").Replace(
            "<RelOp PhysicalOp=\"Compute Scalar\"><ComputeScalar>",
            "<RelOp PhysicalOp=\"Hash Match\"><Hash><RelOp PhysicalOp=\"Compute Scalar\"><ComputeScalar>",
            StringComparison.Ordinal).Replace(
            "</ComputeScalar></RelOp></Sort>",
            "</ComputeScalar></RelOp></Hash></RelOp></Sort>",
            StringComparison.Ordinal);

        AssertBoundedCatalogRejected("sqlserver", nativePlan);
    }

    [Theory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    public void Relational_bounded_catalog_scan_sort_requires_its_limit_operator(string provider)
    {
        var nativePlan = provider switch
        {
            "postgresql" =>
                "[{\"Plan\":{\"Node Type\":\"Sort\",\"Sort Key\":[\"lastSeen DESC\",\"idOrderKey\",\"id\"],\"Plans\":[{\"Node Type\":\"Seq Scan\",\"Relation Name\":\"elsa_otel_resources_v2\"}]}}]",
            "sqlserver" => SqlServerPlanWithoutTop(),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        AssertBoundedCatalogRejected(provider, nativePlan);
    }

    [Theory]
    [InlineData(129, 127)]
    [InlineData(128, 126)]
    public void Mongo_bounded_collection_scan_sort_requires_the_frozen_resource_bounds(
        int physicalCardinality,
        int finiteLimit)
    {
        using var fixture = Fixture.Create(
            "mongodb",
            "resources-by-last-seen",
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            nativePlan: """
                {
                  "queryPlanner": {
                    "winningPlan": {
                      "stage": "SORT",
                      "limitAmount": 128,
                      "inputStage": { "stage": "COLLSCAN" }
                    }
                  }
                }
                """);

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route with
            {
                PhysicalCardinality = physicalCardinality,
                FiniteLimit = finiteLimit,
                MaterializedCandidateCount = finiteLimit
            },
            fixture.Path));
    }

    [Fact]
    public void Mongo_bounded_collection_scan_sort_requires_an_explicit_sort_pattern()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var nativePlan = JsonNode.Parse(MongoBoundedExplainPlan(specification))!.AsObject();
        nativePlan["stages"]!.AsArray()
            .Single(stage => stage!["$sort"] is not null)!["$sort"]!.AsObject()
            .Remove("sortKey");
        using var fixture = Fixture.Create(
            "mongodb",
            "resources-by-last-seen",
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            nativePlan: nativePlan.ToJsonString());

        var exception = Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path));

        Assert.True(DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception));
    }

    [Theory]
    [InlineData("usedDisk", "true")]
    [InlineData("materialized", "true")]
    public void Mongo_bounded_collection_scan_sort_rejects_spill_or_materialization(
        string metadataName,
        string metadataValue)
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var nativePlan = JsonNode.Parse(MongoBoundedExplainPlan(specification))!.AsObject();
        var sortStage = nativePlan["stages"]!.AsArray()
            .Single(stage => stage!["$sort"] is not null)!["$sort"]!.AsObject();
        if (metadataName == "usedDisk")
            sortStage[metadataName] = JsonValue.Create(bool.Parse(metadataValue));
        else
            nativePlan[metadataName] = JsonValue.Create(bool.Parse(metadataValue));
        using var fixture = Fixture.Create(
            "mongodb",
            "resources-by-last-seen",
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            nativePlan: nativePlan.ToJsonString());

        var exception = Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path));

        Assert.True(DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception));
    }

    [Theory]
    [InlineData("traces-by-last-seen")]
    [InlineData("resources-by-last-seen")]
    public void Mongo_bounded_collection_scan_sort_is_restricted_to_the_exact_resource_route(
        string route)
    {
        using var fixture = Fixture.Create(
            "mongodb",
            route,
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            nativePlan: """
                {
                  "queryPlanner": {
                    "winningPlan": {
                      "stage": "SORT",
                      "limitAmount": 128,
                      "inputStage": { "stage": "COLLSCAN" }
                    }
                  }
                }
                """);
        var routeEvidence = route == "resources-by-last-seen"
            ? fixture.Route with { PhysicalCardinality = 100_000 }
            : fixture.Route;

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            routeEvidence,
            fixture.Path));
    }

    [Fact]
    public void Mongo_diagnostics_rejects_unknown_plan_classification()
    {
        using var fixture = Fixture.Create(
            "mongodb",
            "resources-by-last-seen",
            planClassification: "scan-sort");

        Assert.Throws<PerformanceContractException>(() => DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path));
    }

    [Fact]
    public void Mongo_aggregate_command_accepts_a_bounded_keyset_continuation_shape()
    {
        var constituent = DiagnosticsNativePlanContract.TraceDetailConstituents(
                DiagnosticsNativePlanContract.GroundworkAdapter)
            .Single(item => item.RouteIdentity == "trace-detail/spans-by-trace-key-start-id");
        var routeSpecification = new DiagnosticsNativeRouteSpec(
            constituent.RouteIdentity,
            constituent.TableName,
            constituent.IndexName,
            constituent.Ordering[0].Column,
            constituent.PredicateColumn,
            constituent.PhysicalCardinality,
            constituent.FiniteLimit,
            constituent.StorageScopeRequired,
            false,
            constituent.Ordering);
        var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("mongodb", routeSpecification);
        var commandDocument = JsonNode.Parse(Fixture.MongoAggregateCommand(
            routeSpecification,
            match: "{\"traceKey\":\"trace\"}"))!.AsObject();
        commandDocument["pipeline"]!.AsArray().Insert(1, JsonNode.Parse("""
            {
              "$match": {
                "$or": [
                  { "startTime": { "$gt": 1 } },
                  { "$and": [
                    { "startTime": { "$eq": 1 } },
                    { "__groundwork_ordinal_spanId": { "$gt": "span" } }
                  ] },
                  { "$and": [
                    { "startTime": { "$eq": 1 } },
                    { "__groundwork_ordinal_spanId": { "$eq": "span" } },
                    { "sequence": { "$gt": 1 } }
                  ] }
                ]
              }
            }
            """));
        var command = commandDocument.ToJsonString();
        var nativePlan = new JsonObject
        {
            ["queryPlanner"] = new JsonObject
            {
                ["winningPlan"] = new JsonObject { ["stage"] = "IXSCAN", ["indexName"] = physicalIndex }
            },
            ["command"] = JsonNode.Parse(command)
        }.ToJsonString();
        using var directory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(directory.FullName, "spans-mongo.raw.json");
        var artifact = new DiagnosticsNativePlanArtifact(
            1,
            "mongodb",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            constituent.RouteIdentity,
            constituent.TableName,
            constituent.IndexName,
            physicalIndex,
            command,
            nativePlan);
        File.WriteAllText(path, JsonSerializer.Serialize(artifact));
        var evidence = new DiagnosticsTraceDetailConstituentEvidence(
            constituent.RouteIdentity,
            "spans-mongo.raw.json",
            new string('a', 64),
            "index-search",
            physicalIndex,
            command,
            constituent.PhysicalCardinality,
            false,
            true,
            constituent.FiniteLimit,
            constituent.PublicRowBound,
            constituent.PublicRowBound,
            constituent.MaxInvocationCount,
            constituent.MaxInvocationCount);

        DiagnosticsNativePlanContract.ValidateTraceDetailConstituent(
            "mongodb",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            evidence,
            path);
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
            "__groundwork_scope = @scope OR 1 = 1",
            "__groundwork_scope = @scope AND 1 = 1"
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
    public void Groundwork_sqlite_replay_admits_only_the_bounded_snapshot_window()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "structured-log-replay");
        var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("sqlite", specification);
        using var fixture = Fixture.Create(
            "sqlite",
            "structured-log-replay",
            command:
                "SELECT * FROM elsa_structured_logs WHERE " +
                "(__groundwork_scope IS NOT NULL AND __groundwork_scope = @p0) AND " +
                "(sequence IS NOT NULL AND sequence > @p1 AND sequence <= @p2) " +
                "ORDER BY sequence ASC LIMIT @p3",
            nativePlan: $"2 0 SEARCH elsa_structured_logs USING INDEX {physicalIndex} (__groundwork_scope=? AND sequence>? AND sequence<?)");

        DiagnosticsNativePlanContract.ValidateEnvelope("sqlite", fixture.Adapter, fixture.Route, fixture.Path);
    }

    [Fact]
    public void Groundwork_PostgreSql_replay_admits_the_renderer_parenthesized_collated_scope()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "structured-log-replay");
        var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("postgresql", specification);
        using var fixture = Fixture.Create(
            "postgresql",
            specification.RouteIdentity,
            command:
                "SELECT * FROM \"elsa_structured_logs\" WHERE " +
                "((\"sequence\" IS NOT NULL AND \"sequence\" > @p0 AND \"sequence\" <= @p1) AND " +
                "((\"__groundwork_scope\" COLLATE \"C\") IS NOT NULL AND " +
                "(\"__groundwork_scope\" COLLATE \"C\") = @p2)) " +
                "ORDER BY \"sequence\" ASC NULLS FIRST LIMIT @p3;",
            nativePlan:
                $"[{{\"Plan\":{{\"Node Type\":\"Index Scan\",\"Relation Name\":\"elsa_structured_logs\"," +
                $"\"Index Name\":\"{physicalIndex}\"}}}}]");

        DiagnosticsNativePlanContract.ValidateEnvelope(
            "postgresql", fixture.Adapter, fixture.Route, fixture.Path);
    }

    [Theory]
    [InlineData("sequence > @p1")]
    [InlineData("sequence > @p1 AND sequence <= @p2 AND category = @p3")]
    public void Groundwork_sqlite_replay_rejects_an_unbounded_or_extra_predicate(string sequencePredicate)
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "structured-log-replay");
        var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("sqlite", specification);
        using var fixture = Fixture.Create(
            "sqlite",
            "structured-log-replay",
            command:
                $"SELECT * FROM elsa_structured_logs WHERE __groundwork_scope = @p0 AND {sequencePredicate} " +
                "ORDER BY sequence ASC LIMIT @p4",
            nativePlan: $"2 0 SEARCH elsa_structured_logs USING INDEX {physicalIndex} (__groundwork_scope=? AND sequence>?)");

        Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateEnvelope("sqlite", fixture.Adapter, fixture.Route, fixture.Path));
    }

    [Fact]
    public void Groundwork_mongodb_replay_admits_the_exact_bounded_snapshot_window()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "structured-log-replay");
        using var fixture = Fixture.Create(
            "mongodb",
            "structured-log-replay",
            command: Fixture.MongoAggregateCommand(
                specification,
                match: "{\"$and\":[{\"sequence\":{\"$gt\":0,\"$lte\":100000}}]}"));

        DiagnosticsNativePlanContract.ValidateEnvelope("mongodb", fixture.Adapter, fixture.Route, fixture.Path);
    }

    [Fact]
    public void Groundwork_mongodb_replay_rejects_an_unbounded_snapshot_window()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "structured-log-replay");
        using var fixture = Fixture.Create(
            "mongodb",
            "structured-log-replay",
            command: Fixture.MongoAggregateCommand(
                specification,
                match: "{\"$and\":[{\"sequence\":{\"$gt\":0}}]}"));

        Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateEnvelope("mongodb", fixture.Adapter, fixture.Route, fixture.Path));
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

    private static JsonObject MongoOrdinalKeyStage(int index, string column, string? body = null) =>
        new()
        {
            ["$set"] = new JsonObject
            {
                [$"_groundwork_ordinal_key_{index}"] = new JsonObject
                {
                    ["$function"] = new JsonObject
                    {
                        ["body"] = body ?? MongoOrdinalKeyFunctionBody,
                        ["args"] = new JsonArray(JsonValue.Create("$" + column)),
                        ["lang"] = "js"
                    }
                }
            }
        };

    private static string MongoBoundedExplainPlan(
        DiagnosticsNativeRouteSpec specification,
        JsonObject? winningPlan = null,
        string? rejectedIndex = null)
    {
        var command = JsonNode.Parse(Fixture.MongoAggregateCommand(specification))!.AsObject();
        var commandPipeline = command["pipeline"]!.AsArray();
        var sort = commandPipeline
            .Single(stage => stage!["$sort"] is not null)!["$sort"]!.DeepClone()!.AsObject();
        var projection = new JsonObject { ["_id"] = true };
        foreach (var property in sort)
        {
            if (property.Key.StartsWith("_groundwork_", StringComparison.Ordinal))
                projection[property.Key] = false;
        }

        var winner = winningPlan ?? new JsonObject { ["stage"] = "COLLSCAN", ["direction"] = "forward" };
        var queryPlanner = new JsonObject
        {
            ["winningPlan"] = winner,
            ["rejectedPlans"] = rejectedIndex is null
                ? new JsonArray()
                : new JsonArray
                {
                    new JsonObject { ["stage"] = "IXSCAN", ["indexName"] = rejectedIndex }
                }
        };
        var cursor = new JsonObject
        {
            ["queryPlanner"] = queryPlanner,
            ["executionStats"] = new JsonObject
            {
                ["executionStages"] = winner.DeepClone()
            }
        };
        var stages = new JsonArray { new JsonObject { ["$cursor"] = cursor } };
        foreach (var stage in commandPipeline.Where(stage => stage!["$set"] is not null))
            stages.Add(stage!.DeepClone());
        stages.Add(new JsonObject
        {
            ["$sort"] = new JsonObject
            {
                ["sortKey"] = sort,
                ["limit"] = specification.FiniteLimit + 1,
                ["usedDisk"] = false,
                ["spills"] = 0
            }
        });
        stages.Add(new JsonObject { ["$project"] = projection });

        return new JsonObject
        {
            ["command"] = command,
            ["stages"] = stages
        }.ToJsonString();
    }

    private static string BoundedCatalogPlan(string provider)
    {
        if (provider == "postgresql")
        {
            var ordinalColumns = new[] { "idOrderKey", "id" };
            var scan = new Dictionary<string, object>
            {
                ["Node Type"] = "Seq Scan",
                ["Relation Name"] = "elsa_otel_resources_v2",
                ["Plans"] = Enumerable.Range(1, 2).Select(index =>
                {
                    var alias = index == 1 ? "chars" : $"chars_{index - 1}";
                    return new Dictionary<string, object>
                    {
                        ["Node Type"] = "Aggregate",
                        ["Parent Relationship"] = "SubPlan",
                        ["Subplan Name"] = $"SubPlan {index}",
                        ["Output"] = new[]
                        {
                            $"string_agg(CASE WHEN (ascii({alias}.ch) <= 65535) THEN lpad(to_hex(ascii({alias}.ch)), 4, '0'::text) ELSE " +
                            $"(lpad(to_hex((55296 + ((ascii({alias}.ch) - 65536) >> 10))), 4, '0'::text) || " +
                            $"lpad(to_hex((56320 + ((ascii({alias}.ch) - 65536) & 1023))), 4, '0'::text)) END, ''::text ORDER BY {alias}.ord)"
                        },
                        ["Plans"] = new[]
                        {
                            new Dictionary<string, object>
                            {
                                ["Node Type"] = "Function Scan",
                                ["Function Name"] = "unnest",
                                ["Alias"] = alias,
                                ["Output"] = new[] { $"{alias}.ch", $"{alias}.ord" },
                                ["Function Call"] =
                                    $"unnest(string_to_array((elsa_otel_resources_v2.{ordinalColumns[index - 1]})::text, NULL::text))"
                            }
                        }
                    };
                }).ToArray()
            };
            var sort = new Dictionary<string, object>
            {
                ["Node Type"] = "Sort",
                ["Sort Key"] = PostgreSqlExplainSortKeys(),
                ["Plans"] = new[] { scan }
            };
            var limit = new Dictionary<string, object>
            {
                ["Node Type"] = "Limit",
                ["Plans"] = new[] { sort }
            };
            return JsonSerializer.Serialize(new[]
            {
                new Dictionary<string, object> { ["Plan"] = limit }
            });
        }

        return provider switch
        {
            "sqlite" =>
                "2 0 SCAN elsa_otel_resources_v2\n3 0 USE TEMP B-TREE FOR ORDER BY",
            "sqlserver" =>
                """
                <ShowPlanXML>
                  <RelOp PhysicalOp="Top"><Top><RelOp PhysicalOp="Filter"><Filter>
                    <RelOp PhysicalOp="Sort"><Sort><OrderBy>
                      <OrderByColumn Ascending="0"><ColumnReference Column="[lastSeen]" /></OrderByColumn>
                      <OrderByColumn Ascending="1"><ColumnReference Column="[Expr1005]" /></OrderByColumn>
                      <OrderByColumn Ascending="1"><ColumnReference Column="[Expr1006]" /></OrderByColumn>
                      <OrderByColumn Ascending="1"><ColumnReference Column="[Expr1008]" /></OrderByColumn>
                      <OrderByColumn Ascending="1"><ColumnReference Column="[Expr1009]" /></OrderByColumn>
                    </OrderBy><RelOp PhysicalOp="Compute Scalar"><ComputeScalar><DefinedValues>
                      <DefinedValue><ColumnReference Column="[Expr1005]" /><ScalarOperator ScalarString="[idOrderKey] COLLATE Latin1_General_100_BIN2" /></DefinedValue>
                      <DefinedValue><ColumnReference Column="[Expr1006]" /><ScalarOperator ScalarString="DATALENGTH([idOrderKey] COLLATE Latin1_General_100_BIN2)" /></DefinedValue>
                      <DefinedValue><ColumnReference Column="[Expr1008]" /><ScalarOperator ScalarString="[id] COLLATE Latin1_General_100_BIN2" /></DefinedValue>
                      <DefinedValue><ColumnReference Column="[Expr1009]" /><ScalarOperator ScalarString="DATALENGTH([id] COLLATE Latin1_General_100_BIN2)" /></DefinedValue>
                    </DefinedValues>
                      <RelOp PhysicalOp="Table Scan"><TableScan><Object Table="[elsa_otel_resources_v2]" /></TableScan></RelOp>
                    </ComputeScalar></RelOp></Sort></RelOp>
                  </Filter></RelOp></Top></RelOp>
                </ShowPlanXML>
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
    }

    private static string[] PostgreSqlRenderedOrderTerms(DiagnosticsNativeRouteSpec specification) =>
        specification.EffectiveOrdering.SelectMany(term =>
        {
            var ordinal = term.Column is "id" or "idOrderKey" or "traceKey" or "spanId";
            var persistedOrdinal = term.Column is
                "__groundwork_ordinal_id" or "__groundwork_ordinal_spanId" or "__groundwork_ordinal_traceKey";
            var expression = ordinal || persistedOrdinal
                ? $"(\"{term.Column}\" COLLATE \"C\")"
                : $"\"{term.Column}\"";
            var direction = term.Direction == RuntimeNativeOrderDirection.Descending ? "DESC" : "ASC";
            var nullPlacement = term.Direction == RuntimeNativeOrderDirection.Descending
                ? "NULLS LAST"
                : "NULLS FIRST";
            return new[]
            {
                (ordinal ? PostgreSqlOrdinalKey(expression) : expression) + " " + direction + " " + nullPlacement
            };
        }).ToArray();

    private static string[] PostgreSqlExplainSortKeys() =>
    [
        "elsa_otel_resources_v2.\"lastSeen\" DESC",
        "(COALESCE((SubPlan 1), ''::text))",
        "(COALESCE((SubPlan 2), ''::text))"
    ];

    private static string PostgreSqlOrdinalKey(string expression) =>
        "COALESCE((SELECT string_agg(CASE WHEN ascii(chars.ch) <= 65535 THEN lpad(to_hex(ascii(chars.ch)), 4, '0') ELSE " +
        "lpad(to_hex(55296 + ((ascii(chars.ch) - 65536) >> 10)), 4, '0') || " +
        "lpad(to_hex(56320 + ((ascii(chars.ch) - 65536) & 1023)), 4, '0') END, '' ORDER BY chars.ord) " +
        "FROM unnest(string_to_array(" + expression + ", NULL)) WITH ORDINALITY AS chars(ch, ord)), '')";

    private static string ProviderRenderedRelationalCommand(
        string provider,
        DiagnosticsNativeRouteSpec specification)
    {
        specification = DiagnosticsNativePlanContract.PhysicalCommandSpecification(specification);
        var predicateColumns = new[] { "__groundwork_scope" }
            .Concat(specification.PredicateColumn is null ? [] : [specification.PredicateColumn])
            .ToArray();
        var predicate = string.Join(
            " AND ",
            predicateColumns.Select((column, index) =>
                ProviderRenderedEquality(provider, specification, column, "@p" + index)));
        var ordering = provider switch
        {
            "postgresql" => string.Join(", ", PostgreSqlRenderedOrderTerms(specification)),
            "sqlserver" => string.Join(", ", specification.EffectiveOrdering.SelectMany(term =>
            {
                var isOrdinalString = term.Column is "id" or "idOrderKey" or "traceKey" or "spanId";
                var expression = $"[{term.Column}]" +
                                 (isOrdinalString || term.Column.StartsWith("__groundwork_ordinal_", StringComparison.Ordinal)
                                     ? " COLLATE Latin1_General_100_BIN2"
                                     : string.Empty);
                var direction = term.Direction == RuntimeNativeOrderDirection.Descending ? "DESC" : "ASC";
                return isOrdinalString
                    ? new[] { expression + " " + direction, $"DATALENGTH({expression}) {direction}" }
                    : new[] { expression + " " + direction };
            })),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        return provider == "sqlserver"
            ? $"SELECT * FROM {specification.TableName} WHERE {predicate} ORDER BY {ordering} OFFSET 0 ROWS FETCH NEXT @p9 ROWS ONLY"
            : $"SELECT * FROM {specification.TableName} WHERE {predicate} ORDER BY {ordering} LIMIT @p9";
    }

    private static string ProviderRenderedEquality(
        string provider,
        DiagnosticsNativeRouteSpec specification,
        string column,
        string parameter)
    {
        var isString = string.Equals(column, "__groundwork_scope", StringComparison.Ordinal) ||
                       string.Equals(column, specification.PredicateColumn, StringComparison.Ordinal) &&
                       string.Equals(specification.RouteIdentity, "resources-by-service", StringComparison.Ordinal);
        var expression = provider switch
        {
            "postgresql" when isString => $"(\"{column}\" COLLATE \"C\")",
            "postgresql" => $"\"{column}\"",
            "sqlserver" when isString => $"[{column}] COLLATE Latin1_General_100_BIN2",
            "sqlserver" => $"[{column}]",
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        return provider == "sqlserver" && isString
            ? $"({expression} IS NOT NULL AND DATALENGTH({expression}) = DATALENGTH({parameter}) AND {expression} = {parameter})"
            : $"({expression} IS NOT NULL AND {expression} = {parameter})";
    }

    private static string SqlServerPlanWithoutTop()
    {
        var document = System.Xml.Linq.XDocument.Parse(BoundedCatalogPlan("sqlserver"));
        var top = document.Descendants().Single(element =>
            element.Name.LocalName == "RelOp" &&
            element.Attribute("PhysicalOp")?.Value == "Top");
        var child = top.Descendants().First(element => element.Name.LocalName == "RelOp");
        top.ReplaceWith(new System.Xml.Linq.XElement(child));
        return document.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    private static string PostgreSqlPlanWithSwappedLimitAndSort()
    {
        var document = JsonNode.Parse(BoundedCatalogPlan("postgresql"))!.AsArray();
        var limit = document[0]!["Plan"]!.AsObject();
        var sort = limit["Plans"]![0]!.AsObject();
        limit["Node Type"] = "Sort";
        sort["Node Type"] = "Limit";
        return document.ToJsonString();
    }

    private static string SqlServerPlanWithDetachedSort()
    {
        var document = System.Xml.Linq.XDocument.Parse(BoundedCatalogPlan("sqlserver"));
        var top = document.Descendants().Single(element =>
            element.Name.LocalName == "RelOp" &&
            element.Attribute("PhysicalOp")?.Value == "Top");
        var sort = document.Descendants().Single(element =>
            element.Name.LocalName == "RelOp" &&
            element.Attribute("PhysicalOp")?.Value == "Sort");
        var detachedSort = new System.Xml.Linq.XElement(sort);
        top.RemoveNodes();
        document.Root!.Add(detachedSort);
        return document.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    private static string RelationalCommand(string provider, DiagnosticsNativeRouteSpec specification)
    {
        var predicate = specification.PredicateColumn is null
            ? "__groundwork_scope = @scope"
            : $"__groundwork_scope = @scope AND {specification.PredicateColumn} = @value";
        var ordering = string.Join(
            ", ",
            specification.EffectiveOrdering.Select(term =>
                $"{term.Column} {(term.Direction == RuntimeNativeOrderDirection.Descending ? "DESC" : "ASC")}"));
        return provider == "sqlserver"
            ? $"SELECT TOP ({specification.FiniteLimit}) * FROM {specification.TableName} WHERE {predicate} ORDER BY {ordering}"
            : $"SELECT * FROM {specification.TableName} WHERE {predicate} ORDER BY {ordering} LIMIT {specification.FiniteLimit}";
    }

    private static string SqlServerNullableOrderingCommand(
        DiagnosticsNativeRouteSpec specification,
        bool includeNullRanks)
    {
        var ordering = specification.EffectiveOrdering.SelectMany(term =>
        {
            var value = $"[{term.Column}] {(term.Direction == RuntimeNativeOrderDirection.Descending ? "DESC" : "ASC")}";
            return includeNullRanks
                ? new[] { $"CASE WHEN [{term.Column}] IS NULL THEN 1 ELSE 0 END ASC", value }
                : new[] { value };
        });
        return $"SELECT TOP ({specification.FiniteLimit}) * FROM {specification.TableName} ORDER BY {string.Join(", ", ordering)}";
    }

    private static string SqlServerStructuredLogRecentCommand() =>
        "SELECT * FROM [elsa_structured_logs] WHERE " +
        "([__groundwork_scope] COLLATE Latin1_General_100_BIN2 IS NOT NULL AND " +
        "DATALENGTH([__groundwork_scope] COLLATE Latin1_General_100_BIN2) = DATALENGTH(@p0) AND " +
        "[__groundwork_scope] COLLATE Latin1_General_100_BIN2 = @p0) " +
        "ORDER BY [sequence] DESC OFFSET 0 ROWS FETCH NEXT @p1 ROWS ONLY";

    private static string SqlServerStructuredLogReplayCommand() =>
        "SELECT * FROM [elsa_structured_logs] WHERE " +
        "([__groundwork_scope] IS NOT NULL AND [__groundwork_scope] = @p0) AND " +
        "([sequence] IS NOT NULL AND [sequence] > @p1 AND [sequence] <= @p2) " +
        "ORDER BY [sequence] ASC OFFSET 0 ROWS FETCH NEXT @p3 ROWS ONLY";

    private static string SqlServerStructuredLogPrimaryKeyPlan() =>
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "sqlserver-structured-log-primary-key.xml"));

    private static string SqlServerStructuredLogReplayPlan()
    {
        var plan = SqlServerStructuredLogPrimaryKeyPlan()
            .Replace("ScanDirection=\"BACKWARD\"", "ScanDirection=\"FORWARD\"", StringComparison.Ordinal)
            .Replace("ScalarString=\"CONVERT_IMPLICIT(bigint,[@p1],0)\"", "ScalarString=\"CONVERT_IMPLICIT(bigint,[@p3],0)\"", StringComparison.Ordinal)
            .Replace("FETCH NEXT @p1", "FETCH NEXT @p3", StringComparison.Ordinal)
            .Replace("<ColumnReference Column=\"@p1\"/>", "<ColumnReference Column=\"@p3\"/>", StringComparison.Ordinal)
            .Replace("<ColumnReference Column=\"@p1\" />", "<ColumnReference Column=\"@p3\" />", StringComparison.Ordinal)
            .Replace("<ColumnReference Column=\"@p1\" ParameterDataType=", "<ColumnReference Column=\"@p3\" ParameterDataType=", StringComparison.Ordinal);
        const string prefixEnd = "                                  </Prefix>\n                                </SeekKeys>";
        const string replayRanges = """
                                  </Prefix>
                                  <StartRange ScanType="GT">
                                    <RangeColumns>
                                      <ColumnReference Database="[groundwork_diagnostics_fixture]" Schema="[dbo]" Table="[elsa_structured_logs]" Column="sequence" />
                                    </RangeColumns>
                                    <RangeExpressions>
                                      <ScalarOperator>
                                        <Identifier><ColumnReference Column="@p1" /></Identifier>
                                      </ScalarOperator>
                                    </RangeExpressions>
                                  </StartRange>
                                  <EndRange ScanType="LE">
                                    <RangeColumns>
                                      <ColumnReference Database="[groundwork_diagnostics_fixture]" Schema="[dbo]" Table="[elsa_structured_logs]" Column="sequence" />
                                    </RangeColumns>
                                    <RangeExpressions>
                                      <ScalarOperator>
                                        <Identifier><ColumnReference Column="@p2" /></Identifier>
                                      </ScalarOperator>
                                    </RangeExpressions>
                                  </EndRange>
                                </SeekKeys>
        """;
        return plan.Replace(prefixEnd, replayRanges, StringComparison.Ordinal);
    }

    private static string SqlServerStructuredLogReplayDuplicateStartRange() =>
        """
                              <StartRange ScanType="GT">
                                <RangeColumns>
                                  <ColumnReference Database="[groundwork_diagnostics_fixture]" Schema="[dbo]" Table="[elsa_structured_logs]" Column="sequence" />
                                </RangeColumns>
                                <RangeExpressions>
                                  <ScalarOperator>
                                    <Identifier><ColumnReference Column="@p1" /></Identifier>
                                  </ScalarOperator>
                                </RangeExpressions>
                              </StartRange>
                              <EndRange ScanType="LE">
        """;

    private static string SqlServerIndexSeekPlan(DiagnosticsNativeRouteSpec specification)
    {
        var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("sqlserver", specification);
        return $"<ShowPlanXML><RelOp PhysicalOp=\"Index Seek\"><IndexScan><Object Table=\"[{specification.TableName}]\" Index=\"[{physicalIndex}]\" /></IndexScan></RelOp></ShowPlanXML>";
    }

    private static void AssertBoundedCatalogRejected(
        string provider,
        string nativePlan,
        string routeIdentity = "resources-by-last-seen",
        string? command = null)
    {
        using var fixture = Fixture.Create(
            provider,
            routeIdentity,
            command: command,
            planClassification: DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            nativePlan: nativePlan);
        var exception = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateEnvelope(
                provider,
                fixture.Adapter,
                fixture.Route,
                fixture.Path));
        Assert.True(DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception));
    }

    private static string ReadRealPostgreSqlBoundedCatalogPlan(string fixtureName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName));

    private static string ReadMongoExplainFixture(string fixtureName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName));

    private static string MongoCapturedResourceIndexPlan(DiagnosticsNativeRouteSpec specification)
    {
        var nativePlan = JsonNode.Parse(ReadMongoExplainFixture(
            "mongodb-resources-by-service-explain.json"))!.AsObject();
        var cursor = nativePlan["stages"]!.AsArray()
            .Single(stage => stage!["$cursor"] is not null)!["$cursor"]!.AsObject();
        var queryPlanner = cursor["queryPlanner"]!.AsObject();
        var winningPlan = queryPlanner["winningPlan"]!.AsObject();
        var inputStage = winningPlan["inputStage"]!.AsObject();
        var keyPattern = new JsonObject();
        if (specification.PredicateColumn is not null)
            keyPattern[specification.PredicateColumn] = 1;
        keyPattern["lastSeen"] = -1;
        keyPattern["idOrderKey"] = 1;
        keyPattern["id"] = 1;
        inputStage["keyPattern"] = keyPattern;
        var indexBounds = new JsonObject();
        if (specification.PredicateColumn is not null)
            indexBounds[specification.PredicateColumn] = new JsonArray(JsonValue.Create("[\"service-hash\", \"service-hash\"]"));
        indexBounds["lastSeen"] = new JsonArray(JsonValue.Create("[MaxKey, MinKey]"));
        indexBounds["idOrderKey"] = new JsonArray(JsonValue.Create("[MinKey, MaxKey]"));
        indexBounds["id"] = new JsonArray(JsonValue.Create("[MinKey, MaxKey]"));
        inputStage["indexBounds"] = indexBounds;
        inputStage["indexName"] = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(
            "mongodb",
            specification);

        // Only the service route is captured. Keep both native trees consistent when deriving
        // status and last-seen variants; these variants are contract tests, not live-provider evidence.
        inputStage["multiKeyPaths"] = new JsonObject(keyPattern.Select(property =>
            new KeyValuePair<string, JsonNode?>(property.Key, new JsonArray())));
        var executionIndex = cursor["executionStats"]!["executionStages"]!["inputStage"]!.AsObject();
        foreach (var property in inputStage)
            executionIndex[property.Key] = property.Value?.DeepClone();

        var match = nativePlan["command"]!["pipeline"]!.AsArray()[0]!["$match"]!.AsObject();
        if (specification.PredicateColumn is null)
            match.Clear();
        else if (specification.PredicateColumn != "serviceNameKey")
        {
            var value = match["serviceNameKey"]!.DeepClone();
            match.Remove("serviceNameKey");
            match[specification.PredicateColumn] = value;
        }

        queryPlanner["parsedQuery"] = specification.PredicateColumn is null
            ? new JsonObject()
            : new JsonObject
            {
                [specification.PredicateColumn] = new JsonObject
                {
                    ["$eq"] = match[specification.PredicateColumn]!.DeepClone()
                }
            };
        return nativePlan.ToJsonString();
    }

    private static string RealPostgreSqlBoundedCatalogPlanForRoute(
        DiagnosticsNativeRouteSpec specification)
    {
        var plan = JsonNode.Parse(ReadRealPostgreSqlBoundedCatalogPlan(
            "postgresql-bounded-resources-by-last-seen.json"))!.AsArray();
        var scan = plan[0]!["Plan"]!["Plans"]![0]!["Plans"]![0]!;
        var originalTable = scan["Relation Name"]!.GetValue<string>();
        Assert.Equal("elsa_otel_resources_v2", originalTable);
        scan["Relation Name"] = specification.TableName;
        return plan.ToJsonString();
    }

    private static string PostgreSqlRenderedRelationalCommandForRoute(
        DiagnosticsNativeRouteSpec specification)
    {
        var commandSpecification = specification.RouteIdentity is "metrics-by-last-seen" or "logs-by-last-seen"
            ? specification with
            {
                Ordering = specification.EffectiveOrdering.Select(term => term.Column == "id"
                    ? term with { Column = "__groundwork_ordinal_id" }
                    : term).ToArray()
            }
            : specification;
        return ProviderRenderedRelationalCommand("postgresql", commandSpecification);
    }

    private static string MutateRealPostgreSqlBoundedCatalogPlan(string mutation)
    {
        var plan = JsonNode.Parse(ReadRealPostgreSqlBoundedCatalogPlan(
                "postgresql-bounded-resources-by-last-seen.json"))!.AsArray();
        var sortKeys = plan[0]!["Plan"]!["Plans"]![0]!["Sort Key"]!.AsArray();
        var index = mutation.StartsWith("subplan-", StringComparison.Ordinal) ? 1 : 0;
        var original = sortKeys[index]!.GetValue<string>();
        var replacement = mutation switch
        {
            "simple-null-placement" => original.Replace("DESC NULLS LAST", "DESC NULLS FIRST", StringComparison.Ordinal),
            "simple-direction" => original.Replace("DESC NULLS LAST", "ASC NULLS FIRST", StringComparison.Ordinal),
            "subplan-null-placement" => original.Replace("NULLS FIRST", "NULLS LAST", StringComparison.Ordinal),
            "subplan-direction" => original.Replace("NULLS FIRST", "DESC NULLS LAST", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
        };
        Assert.NotEqual(original, replacement);
        sortKeys[index] = replacement;
        return plan.ToJsonString();
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(
            string provider,
            string adapter,
            string routeIdentity,
            string commandText,
            string nativePlan,
            string? planClassification,
            string? physicalIndexName)
        {
            Adapter = adapter;
            Route = RouteFor(Adapter, provider, routeIdentity, planClassification, physicalIndexName);
            var artifact = new DiagnosticsNativePlanArtifact(
                1,
                provider,
                Adapter,
                routeIdentity,
                RouteSpec.TableName,
                RouteSpec.IndexName,
                physicalIndexName ?? DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(provider, RouteSpec),
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
            string? nativePlan = null,
            bool attachCommandToNativePlan = true,
            string? planClassification = null,
            string? adapter = null,
            string? physicalIndexName = null)
        {
            adapter ??= DiagnosticsNativePlanContract.GroundworkAdapter;
            var spec = DiagnosticsNativePlanContract.For(adapter, routeIdentity);
            command ??= provider switch
            {
                "mongodb" => MongoAggregateCommand(spec),
                "postgresql" or "sqlserver" when spec.EffectiveOrdering.Any(term =>
                    term.Column is "id" or "idOrderKey" or "traceKey" or "spanId") =>
                    ProviderRenderedRelationalCommand(provider, spec),
                _ => SqliteCommand(spec)
            };
            var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(provider, spec);
            nativePlan ??= provider switch
            {
                "sqlite" => $"2 0 SEARCH {spec.TableName} USING INDEX {physicalIndex} (__groundwork_scope=?)",
                "postgresql" => $"[{{\"Plan\":{{\"Node Type\":\"Index Scan\",\"Relation Name\":\"{spec.TableName}\",\"Index Name\":\"{physicalIndex}\"}}}}]",
                "sqlserver" => $"<ShowPlanXML><RelOp PhysicalOp=\"Index Seek\"><IndexScan><Object Table=\"[{spec.TableName}]\" Index=\"[{physicalIndex}]\" /></IndexScan></RelOp></ShowPlanXML>",
                "mongodb" => $"{{\"winningPlan\":{{\"stage\":\"IXSCAN\",\"indexName\":\"{physicalIndex}\"}}}}",
                _ => throw new ArgumentOutOfRangeException(nameof(provider))
            };
            if (provider == "mongodb" && attachCommandToNativePlan)
            {
                var plan = JsonNode.Parse(nativePlan)?.AsObject() ??
                           throw new InvalidOperationException("Mongo fixture plan must be an object.");
                plan["command"] = JsonNode.Parse(command) ??
                                   throw new InvalidOperationException("Mongo fixture command must be valid JSON.");
                nativePlan = plan.ToJsonString();
            }
            return new Fixture(provider, adapter, routeIdentity, command, nativePlan, planClassification, physicalIndexName);
        }

        internal static string MongoPhysicalCollection(DiagnosticsNativeRouteSpec specification) =>
            $"{specification.TableName}__scope__{new string('A', 64)}";

        private static string SqliteCommand(DiagnosticsNativeRouteSpec specification)
        {
            var physical = DiagnosticsNativePlanContract.PhysicalCommandSpecification(specification);
            var order = string.Join(", ", physical.EffectiveOrdering.Select(term =>
                term.Column + (term.Direction == RuntimeNativeOrderDirection.Descending ? " DESC" : " ASC")));
            return $"SELECT * FROM {physical.TableName} WHERE __groundwork_scope = @scope ORDER BY {order} LIMIT @limit";
        }

        internal static string MongoAggregateCommand(
            DiagnosticsNativeRouteSpec specification,
            string? collection = null,
            string? match = null)
        {
            specification = DiagnosticsNativePlanContract.PhysicalCommandSpecification(specification);
            var matchNode = JsonNode.Parse(match ??
                (specification.PredicateColumn is null ? "{}" : $"{{\"{specification.PredicateColumn}\":1}}"))!;
            var pipeline = new JsonArray(new JsonObject { ["$match"] = matchNode });
            var sort = new JsonObject();
            foreach (var (term, index) in specification.EffectiveOrdering.Select((term, index) => (term, index)))
            {
                var column = term.Column is "id" or "idOrderKey" or "serviceNameKey" or "traceKey" or "spanId"
                    ? "_groundwork_ordinal_key_" + index
                    : term.Column;
                if (column.StartsWith("_groundwork_ordinal_key_", StringComparison.Ordinal))
                    pipeline.Add(MongoOrdinalKeyStage(index, term.Column));
                sort[column] = term.Direction == RuntimeNativeOrderDirection.Descending ? -1 : 1;
            }
            pipeline.Add(new JsonObject { ["$sort"] = sort });
            pipeline.Add(new JsonObject { ["$limit"] = specification.FiniteLimit + 1 });
            return new JsonObject
            {
                ["aggregate"] = collection ?? MongoPhysicalCollection(specification),
                ["pipeline"] = pipeline,
                ["cursor"] = new JsonObject()
            }.ToJsonString();
        }

        public void Dispose()
        {
            if (Directory.Exists)
                Directory.Delete(true);
        }

        private static NativeRouteEvidence RouteFor(
            string adapter,
            string provider,
            string routeIdentity,
            string? planClassification = null,
            string? physicalIndexName = null)
        {
            var spec = DiagnosticsNativePlanContract.For(adapter, routeIdentity);
            return new NativeRouteEvidence(
                routeIdentity,
                "route.raw.json",
                new string('a', 64),
                planClassification ?? DiagnosticsNativePlanContract.IndexSearchPlanClassification,
                physicalIndexName ?? DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(provider, spec),
                spec.PhysicalCardinality,
                DiagnosticsNativePlanContract.ExpectedStorageScopePredicate(provider, spec),
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

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
