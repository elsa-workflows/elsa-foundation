using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Groundwork.Kernel;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class DiagnosticsDurableHistoryAdapterTests
{
    [Fact]
    public async Task Dispatches_to_the_exact_groundwork_diagnostics_form()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request(),
            "unused",
            "unused");

        Assert.IsType<DiagnosticsDurableHistoryAdapter>(adapter);
    }

    [Fact]
    public async Task Dispatches_to_the_temporary_sqlite_ef_diagnostics_form()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request(
                adapter: EfDiagnosticsDurableHistoryAdapter.AdapterId,
                physicalForm: EfDiagnosticsDurableHistoryAdapter.PhysicalForm),
            "unused",
            "unused");

        Assert.IsType<EfDiagnosticsDurableHistoryAdapter>(adapter);
    }

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mongodb")]
    public async Task Temporary_ef_diagnostics_comparator_refuses_non_sqlite_before_provider_open(string provider)
    {
        await using var adapter = new EfDiagnosticsDurableHistoryAdapter(
            Request(
                provider,
                adapter: EfDiagnosticsDurableHistoryAdapter.AdapterId,
                physicalForm: EfDiagnosticsDurableHistoryAdapter.PhysicalForm),
            "unused",
            "unused");

        var exception = await Assert.ThrowsAsync<PerformanceContractException>(() => adapter.PrepareAsync(CancellationToken.None));

        Assert.Contains("only supports sqlite", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Timed_operations_remain_closed_while_the_absolute_budget_gate_is_blocked()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(Request(), "unused", "unused");

        var exception = Assert.Throws<PerformanceContractException>(() => adapter.Operations);

        Assert.Contains(ReproducibleWorkloadScenarioCatalog.DiagnosticsBlockedReasonCode, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SQLite_scoped_clients_share_the_one_process_connection_but_remain_distinct()
    {
        var root = Directory.CreateTempSubdirectory("diagnostics-shared-sqlite-");
        var connectionString = $"Data Source={Path.Combine(root.FullName, "diagnostics.db")};Mode=ReadWriteCreate;Cache=Shared";
        try
        {
            var observed = await ProviderProbe.ReadAsync("sqlite", connectionString, CancellationToken.None);
            var request = Request() with
            {
                ProviderVersion = observed.Version,
                ProviderTopology = observed.Topology,
                ProviderConfiguration = observed.Configuration
            };
            await using var adapter = new DiagnosticsDurableHistoryAdapter(request, connectionString, root.FullName);

            await adapter.PrepareAsync(CancellationToken.None);
            var scopes = await adapter.OpenScopedClientsAsync(CancellationToken.None);

            Assert.NotSame(scopes.Primary, scopes.Secondary);
            Assert.NotSame(scopes.Primary.OpenTelemetry, scopes.Secondary.OpenTelemetry);
            Assert.NotSame(scopes.Primary.StructuredLogs, scopes.Secondary.StructuredLogs);

            await scopes.Secondary.StructuredLogs.AppendAsync(
                new StructuredLogEntry { Message = "secondary", Category = "scope", SourceId = "secondary" },
                CancellationToken.None);
            var primary = await scopes.Primary.StructuredLogs.AppendAsync(
                new StructuredLogEntry { Message = "primary", Category = "scope", SourceId = "primary" },
                CancellationToken.None);
            Assert.Equal(primary.Sequence, await scopes.Primary.StructuredLogs.GetHighWaterMarkAsync());

            adapter.CommandObserver.ClearCommands();
            await scopes.Primary.OpenTelemetry.QueryResourcesAsync(
                new OpenTelemetryResourceFilter { Take = 1 },
                CancellationToken.None);
            await scopes.Primary.StructuredLogs.GetRecentAsync(
                new StructuredLogFilter { MaxCount = 1 },
                CancellationToken.None);

            Assert.Collection(
                adapter.CommandObserver.Commands,
                command => Assert.Contains("elsa_otel_resources_v2", command.CommandText, StringComparison.OrdinalIgnoreCase),
                command => Assert.Contains("elsa_structured_logs", command.CommandText, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public async Task SQLite_durability_poll_retries_a_transient_schema_lock()
    {
        var store = new TransientLockedOpenTelemetryStore();
        var tracking = new DiagnosticsDurableHistoryAdapter.TrackingOpenTelemetryStore(store);

        await tracking.WaitForDurabilityAsync(CancellationToken.None);

        Assert.Equal(2, store.DiagnosticsReadCount);
    }

    [Fact]
    public void Frozen_sequence_and_native_route_cardinalities_match_the_catalog()
    {
        var workload = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads[
            DiagnosticsDurableHistoryWorkload.WorkloadId];

        Assert.Equal(workload.OperationSequence, DiagnosticsDurableHistoryWorkload.OperationIds);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.ExpectedInputFingerprint, workload.Input.FingerprintSha256);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.ExpectedResultDigest, workload.Correctness.ResultDigestSha256);
        Assert.Equal(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["resources-by-last-seen"] = DiagnosticsDurableHistoryWorkload.ResourceCount,
                ["resources-by-status"] = DiagnosticsDurableHistoryWorkload.ResourceCount,
                ["resources-by-service"] = DiagnosticsDurableHistoryWorkload.ResourceCount,
                ["traces-by-last-seen"] = DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
                ["trace-detail"] = DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
                ["metrics-by-last-seen"] = DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
                ["logs-by-last-seen"] = DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
                ["structured-log-recent"] = DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream,
                ["structured-log-replay"] = DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream
            },
            DiagnosticsDurableHistoryWorkload.NativeRouteCardinalities);
    }

    [Fact]
    public void Diagnostics_admission_retains_the_unratified_absolute_budget_reason()
    {
        var workload = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads[
            DiagnosticsDurableHistoryWorkload.WorkloadId];

        Assert.True(BenchmarkAdmissionGuard.TryGetBlockedReason(workload, out var reason));
        Assert.Equal(ReproducibleWorkloadScenarioCatalog.DiagnosticsBlockedReasonCode, reason);
    }

    [Fact]
    public void Trace_detail_artifacts_are_partitioned_by_their_exact_logical_index()
    {
        var directory = Directory.CreateTempSubdirectory("diagnostics-native-pages-");
        try
        {
            var before = Path.Combine(directory.FullName, "000001-sqlite-optimizer-selected-before.txt");
            var spanFirst = Path.Combine(directory.FullName, "000002-sqlite-optimizer-selected-elsa_otel_spans_trace_detail.txt");
            var log = Path.Combine(directory.FullName, "000003-sqlite-optimizer-selected-elsa_otel_logs_trace_detail.txt");
            var spanSecond = Path.Combine(directory.FullName, "000004-sqlite-optimizer-selected-elsa_otel_spans_trace_detail.txt");
            File.WriteAllText(before, "before");
            File.WriteAllText(spanFirst, "span one");
            File.WriteAllText(log, "log");
            File.WriteAllText(spanSecond, "span two");

            var beforeTraceDetail = new HashSet<string>(StringComparer.Ordinal) { before };
            var spans = DiagnosticsNativePlanCapture.RequireNativeArtifacts(
                directory.FullName,
                beforeTraceDetail,
                "sqlite",
                "elsa_otel_spans_trace_detail",
                2);
            var logs = DiagnosticsNativePlanCapture.RequireNativeArtifacts(
                directory.FullName,
                beforeTraceDetail,
                "sqlite",
                "elsa_otel_logs_trace_detail",
                1);

            Assert.Equal([spanFirst, spanSecond], spans);
            Assert.Equal([log], logs);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Trace_detail_page_is_hashed_before_validation_and_published_only_after_admission()
    {
        var specification = DiagnosticsNativePlanContract.TraceDetailConstituents(
                DiagnosticsNativePlanContract.GroundworkAdapter)
            .Single(item => item.RouteIdentity == "trace-detail/spans-by-trace-key-start-id");
        var route = new DiagnosticsNativeRouteSpec(
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            specification.Ordering[0].Column,
            specification.PredicateColumn,
            specification.PhysicalCardinality,
            specification.FiniteLimit,
            specification.StorageScopeRequired,
            false,
            specification.Ordering);
        var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("sqlite", route);
        var command =
            "SELECT * FROM elsa_otel_spans_v2 WHERE __groundwork_scope = @scope AND traceKey = @traceKey " +
            "ORDER BY startTime ASC, spanId ASC, sequence ASC LIMIT 127";
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
        var evidence = new DiagnosticsTraceDetailConstituentEvidence(
            specification.RouteIdentity,
            "page.raw.json",
            "",
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
        var directory = Directory.CreateTempSubdirectory("diagnostics-native-page-publish-");
        var path = Path.Combine(directory.FullName, evidence.RawPlanReference);
        try
        {
            var digest = DiagnosticsNativePlanCapture.ValidateAndPublishTraceDetailPage(
                "sqlite",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                evidence,
                artifact,
                path);

            Assert.Equal(64, digest.Length);
            Assert.True(File.Exists(path));
            Assert.Equal(digest, ArtifactStore.HashFile(path));

            File.Delete(path);
            Assert.Throws<PerformanceContractException>(() =>
                DiagnosticsNativePlanCapture.ValidateAndPublishTraceDetailPage(
                    "sqlite",
                    DiagnosticsNativePlanContract.GroundworkAdapter,
                    evidence,
                    artifact with { PhysicalIndexName = "wrong-index" },
                    path));
            Assert.False(File.Exists(path));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Mongo_trace_detail_rejects_an_unrecognized_non_probe_read_operation()
    {
        var commands = new[]
        {
            new ProviderCommandEvent("mongodb.query", "{}", ProviderCommandKind.Read, false),
            new ProviderCommandEvent("mongodb.lookup", "{}", ProviderCommandKind.Read, false)
        };

        var exception = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanCapture.RequireKnownMongoReadOperations(commands));

        Assert.Contains("mongodb.lookup", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mongo_trace_detail_rejects_a_read_without_command_identity()
    {
        var commands = new[]
        {
            new ProviderCommandEvent("mongodb.read", " ", ProviderCommandKind.Read, false)
        };

        var exception = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanCapture.RequireKnownMongoReadOperations(commands));

        Assert.Contains("without command identity evidence", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mongo_trace_detail_binds_point_reads_by_physical_collection_instead_of_observer_order()
    {
        var specifications = DiagnosticsNativePlanContract.TraceDetailConstituents(
            DiagnosticsNativePlanContract.GroundworkAdapter);
        var summary = specifications.Single(item => item.RouteIdentity == "trace-detail/summary-by-trace-key");
        var resource = specifications.Single(item => item.RouteIdentity == "trace-detail/resources-by-id");
        var summaryCommand = MongoPointCommand(summary.TableName);
        var resourceCommand = MongoPointCommand(resource.TableName);
        var commands = new[]
        {
            new ProviderCommandEvent("mongodb.read", resourceCommand, ProviderCommandKind.Read, false),
            new ProviderCommandEvent("mongodb.read", summaryCommand, ProviderCommandKind.Read, false)
        };

        var classified = DiagnosticsNativePlanCapture.ClassifyMongoPointReads(commands, specifications);

        Assert.Equal(summaryCommand, Assert.Single(classified[summary.RouteIdentity]).CommandText);
        Assert.Equal(resourceCommand, Assert.Single(classified[resource.RouteIdentity]).CommandText);
    }

    [Fact]
    public void Mongo_trace_detail_rejects_a_point_read_for_an_undeclared_collection()
    {
        var specifications = DiagnosticsNativePlanContract.TraceDetailConstituents(
            DiagnosticsNativePlanContract.GroundworkAdapter);
        var commands = new[]
        {
            new ProviderCommandEvent(
                "mongodb.read",
                MongoPointCommand("elsa_unknown"),
                ProviderCommandKind.Read,
                false)
        };

        var exception = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanCapture.ClassifyMongoPointReads(commands, specifications));

        Assert.Contains("exactly one constituent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Process_identity_is_bound_into_storage_and_diagnostic_scopes()
    {
        var first = Request(processIndex: 1);
        var second = Request(processIndex: 2);

        Assert.NotEqual(
            DiagnosticsDurableHistoryAdapter.PersistenceScopeForTesting(first),
            DiagnosticsDurableHistoryAdapter.PersistenceScopeForTesting(second));
        Assert.NotEqual(
            DiagnosticsDurableHistoryAdapter.BindingScopeForTesting(first, "primary"),
            DiagnosticsDurableHistoryAdapter.BindingScopeForTesting(second, "primary"));
        Assert.InRange(
            DiagnosticsDurableHistoryAdapter.BindingTenantForTesting(first, "primary").Length,
            1,
            64);
        Assert.InRange(
            DiagnosticsDurableHistoryAdapter.BindingStorageScopeForTesting(first, "primary").Length,
            1,
            64);
    }

    private static RunRequest Request(
        string provider = "sqlite",
        string adapter = DiagnosticsDurableHistoryAdapter.AdapterId,
        string physicalForm = DiagnosticsDurableHistoryAdapter.PhysicalForm,
        int processIndex = 1) => new(
        ComparisonCohortId: "cohort",
        MeasurementSetId: "set",
        WorkloadId: DiagnosticsDurableHistoryWorkload.WorkloadId,
        WorkloadVersion: DiagnosticsDurableHistoryWorkload.Version,
        Provider: provider,
        ProviderVersion: "3.46.0",
        ProviderTopology: "file-backed-distinct-connections",
        ProviderConfiguration: new Dictionary<string, string>(StringComparer.Ordinal),
        Adapter: adapter,
        PhysicalForm: physicalForm,
        Scale: "small",
        CommitSha: new string('a', 40),
        HarnessAssemblySha256: new string('b', 64),
        PackageVersions: new Dictionary<string, string>(StringComparer.Ordinal),
        CompositionFingerprint: new string('c', 64),
        HostFingerprintSha256: new string('d', 64),
        Seed: DiagnosticsDurableHistoryWorkload.Seed,
        InputFingerprintSha256: DiagnosticsDurableHistoryWorkload.ExpectedInputFingerprint,
        NativePlanIdentity: "diagnostics-plan",
        NativePlanEvidenceReference: "diagnostics-plan.json",
        NativePlanContentSha256: new string('e', 64),
        ProcessKind: ProcessKind.Measured,
        ProcessIndex: processIndex);

    private static string MongoPointCommand(string tableName) => JsonSerializer.Serialize(new
    {
        collection = tableName + "__scope__" + new string('A', 64),
        filter = new Dictionary<string, object>
        {
            ["_id"] = new Dictionary<string, string> { ["$eq"] = "<redacted>" }
        },
        limit = 1
    });

    private sealed class TransientLockedOpenTelemetryStore : IOpenTelemetryStore
    {
        public int DiagnosticsReadCount { get; private set; }

        public ValueTask WriteAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<OpenTelemetryResourceResult> QueryResourcesAsync(OpenTelemetryResourceFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<OpenTelemetryTraceResult> QueryTracesAsync(OpenTelemetryTraceFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<OpenTelemetryTraceDetail?> GetTraceAsync(string traceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<OpenTelemetryMetricResult> QueryMetricsAsync(OpenTelemetryMetricFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<OpenTelemetryLogResult> QueryLogsAsync(OpenTelemetryLogFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<OpenTelemetryStorageDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DiagnosticsReadCount++;
            if (DiagnosticsReadCount == 1)
                throw new SqliteException("database schema is locked: main", 6, 6);

            return ValueTask.FromResult(new OpenTelemetryStorageDiagnostics(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        }
    }
}
