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
        var command =
            "SELECT * FROM \"elsa_otel_spans_v2\" WHERE " +
            "((\"__groundwork_scope\" COLLATE GROUNDWORK_UTF16_ORDINAL IS NOT NULL AND \"__groundwork_scope\" COLLATE GROUNDWORK_UTF16_ORDINAL = @p0) " +
            "AND (\"traceKey\" COLLATE GROUNDWORK_UTF16_ORDINAL IS NOT NULL AND \"traceKey\" COLLATE GROUNDWORK_UTF16_ORDINAL = @p1)) " +
            "AND ((((\"startTime\" IS NOT NULL AND \"startTime\" > @p2) OR \"startTime\" IS NULL) " +
            "OR ((\"startTime\" IS NOT NULL AND \"startTime\" = @p3) AND ((\"spanId\" COLLATE GROUNDWORK_UTF16_ORDINAL IS NOT NULL AND \"spanId\" COLLATE GROUNDWORK_UTF16_ORDINAL > @p4) OR \"spanId\" COLLATE GROUNDWORK_UTF16_ORDINAL IS NULL)) " +
            "OR ((\"startTime\" IS NOT NULL AND \"startTime\" = @p5) AND (\"spanId\" COLLATE GROUNDWORK_UTF16_ORDINAL IS NOT NULL AND \"spanId\" COLLATE GROUNDWORK_UTF16_ORDINAL = @p6) AND ((\"sequence\" IS NOT NULL AND \"sequence\" > @p7) OR \"sequence\" IS NULL))) " +
            "ORDER BY \"startTime\" ASC, \"spanId\" COLLATE GROUNDWORK_UTF16_ORDINAL ASC, \"sequence\" ASC LIMIT @p8;";
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
            new Dictionary<string, string> { ["Groundwork.MongoDb"] = "0.4.0-preview.10" },
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
        pipeline[1]!["$sort"]!.AsObject().Remove("id");
        pipeline[2]!["$limit"] = 1;
        using var fixture = Fixture.Create("mongodb", specification.RouteIdentity, command: command.ToJsonString());

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
        const string nativePlan = """
            {
              "queryPlanner": {
                "winningPlan": {
                  "stage": "SORT",
                  "sortPattern": { "lastSeen": -1, "idOrderKey": 1, "id": 1 },
                  "limitAmount": 127,
                  "inputStage": { "stage": "COLLSCAN", "direction": "forward" }
                }
              }
            }
            """;

        var classification = DiagnosticsNativePlanContract.ClassifyPlan(
            "mongodb",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            specification,
            nativePlan);

        Assert.Equal(DiagnosticsNativePlanContract.BoundedMongoScanSortPlanClassification, classification);
    }

    [Theory]
    [InlineData("resources-by-last-seen")]
    [InlineData("resources-by-status")]
    [InlineData("resources-by-service")]
    public void Mongo_frozen_resource_catalog_admits_an_explicit_bounded_collection_scan_sort(string route)
    {
        using var fixture = Fixture.Create(
            "mongodb",
            route,
            planClassification: DiagnosticsNativePlanContract.BoundedMongoScanSortPlanClassification,
            nativePlan: """
                {
                  "queryPlanner": {
                    "winningPlan": {
                      "stage": "SORT",
                      "sortPattern": { "lastSeen": -1, "idOrderKey": 1, "id": 1 },
                      "limitAmount": 127,
                      "inputStage": { "stage": "COLLSCAN", "direction": "forward" }
                    }
                  },
                  "executionStats": { "nReturned": 127 }
                }
                """);

        DiagnosticsNativePlanContract.ValidateEnvelope(
            "mongodb",
            fixture.Adapter,
            fixture.Route,
            fixture.Path);
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
            planClassification: DiagnosticsNativePlanContract.BoundedMongoScanSortPlanClassification,
            nativePlan: """
                {
                  "queryPlanner": {
                    "winningPlan": {
                      "stage": "SORT",
                      "limitAmount": 127,
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
        using var fixture = Fixture.Create(
            "mongodb",
            "resources-by-last-seen",
            planClassification: DiagnosticsNativePlanContract.BoundedMongoScanSortPlanClassification,
            nativePlan: """
                {
                  "queryPlanner": {
                    "winningPlan": {
                      "stage": "SORT",
                      "limitAmount": 127,
                      "inputStage": { "stage": "COLLSCAN" }
                    }
                  }
                }
                """);

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
        using var fixture = Fixture.Create(
            "mongodb",
            "resources-by-last-seen",
            planClassification: DiagnosticsNativePlanContract.BoundedMongoScanSortPlanClassification,
            nativePlan: $$"""
                {
                  "queryPlanner": {
                    "winningPlan": {
                      "stage": "SORT",
                      "limitAmount": 127,
                      "inputStage": { "stage": "COLLSCAN" }
                    }
                  },
                  "executionStats": { "{{metadataName}}": {{metadataValue}} }
                }
                """);

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
            planClassification: DiagnosticsNativePlanContract.BoundedMongoScanSortPlanClassification,
            nativePlan: """
                {
                  "queryPlanner": {
                    "winningPlan": {
                      "stage": "SORT",
                      "limitAmount": 127,
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
        var command = JsonSerializer.Serialize(new
        {
            aggregate = Fixture.MongoPhysicalCollection(routeSpecification),
            pipeline = new object[]
            {
                new Dictionary<string, object> { ["$match"] = new Dictionary<string, string> { ["traceKey"] = "trace" } },
                new Dictionary<string, object>
                {
                    ["$match"] = new Dictionary<string, object>
                    {
                        ["$or"] = new object[]
                        {
                            new Dictionary<string, object> { ["startTime"] = new Dictionary<string, int> { ["$gt"] = 1 } },
                            new Dictionary<string, object>
                            {
                                ["$and"] = new object[]
                                {
                                    new Dictionary<string, object> { ["startTime"] = new Dictionary<string, int> { ["$eq"] = 1 } },
                                    new Dictionary<string, object> { ["spanId"] = new Dictionary<string, string> { ["$gt"] = "span" } }
                                }
                            },
                            new Dictionary<string, object>
                            {
                                ["$and"] = new object[]
                                {
                                    new Dictionary<string, object> { ["startTime"] = new Dictionary<string, int> { ["$eq"] = 1 } },
                                    new Dictionary<string, object> { ["spanId"] = new Dictionary<string, string> { ["$eq"] = "span" } },
                                    new Dictionary<string, object> { ["sequence"] = new Dictionary<string, int> { ["$gt"] = 1 } }
                                }
                            }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    ["$sort"] = new Dictionary<string, int> { ["startTime"] = 1, ["spanId"] = 1, ["sequence"] = 1 }
                },
                new Dictionary<string, int> { ["$limit"] = constituent.FiniteLimit }
            },
            cursor = new { }
        });
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

    private sealed class Fixture : IDisposable
    {
        private Fixture(
            string provider,
            string routeIdentity,
            string commandText,
            string nativePlan,
            string? planClassification)
        {
            Adapter = DiagnosticsNativePlanContract.GroundworkAdapter;
            Route = RouteFor(Adapter, provider, routeIdentity, planClassification);
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
            string? nativePlan = null,
            bool attachCommandToNativePlan = true,
            string? planClassification = null)
        {
            var spec = DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, routeIdentity);
            command ??= provider switch
            {
                "mongodb" => MongoAggregateCommand(spec),
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
            if (provider == "mongodb" && attachCommandToNativePlan)
            {
                var plan = JsonNode.Parse(nativePlan)?.AsObject() ??
                           throw new InvalidOperationException("Mongo fixture plan must be an object.");
                plan["command"] = JsonNode.Parse(command) ??
                                   throw new InvalidOperationException("Mongo fixture command must be valid JSON.");
                nativePlan = plan.ToJsonString();
            }
            return new Fixture(provider, routeIdentity, command, nativePlan, planClassification);
        }

        internal static string MongoPhysicalCollection(DiagnosticsNativeRouteSpec specification) =>
            $"{specification.TableName}__scope__{new string('A', 64)}";

        internal static string MongoAggregateCommand(
            DiagnosticsNativeRouteSpec specification,
            string? collection = null,
            string? match = null)
        {
            using var matchDocument = JsonDocument.Parse(match ??
                (specification.PredicateColumn is null
                    ? "{}"
                    : $"{{\"{specification.PredicateColumn}\":1}}"));
            var sort = specification.EffectiveOrdering.ToDictionary(
                term => term.Column,
                term => term.Direction == RuntimeNativeOrderDirection.Descending ? -1 : 1,
                StringComparer.Ordinal);
            return JsonSerializer.Serialize(new
            {
                aggregate = collection ?? MongoPhysicalCollection(specification),
                pipeline = new object[]
                {
                    new Dictionary<string, JsonElement> { ["$match"] = matchDocument.RootElement.Clone() },
                    new Dictionary<string, object> { ["$sort"] = sort },
                    new Dictionary<string, int> { ["$limit"] = specification.FiniteLimit }
                },
                cursor = new { }
            });
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
            string? planClassification = null)
        {
            var spec = DiagnosticsNativePlanContract.For(adapter, routeIdentity);
            return new NativeRouteEvidence(
                routeIdentity,
                "route.raw.json",
                new string('a', 64),
                planClassification ?? DiagnosticsNativePlanContract.IndexSearchPlanClassification,
                DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(provider, spec),
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
