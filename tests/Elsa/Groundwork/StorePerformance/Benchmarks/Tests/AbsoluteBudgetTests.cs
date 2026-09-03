using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using System.Text.Json;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class AbsoluteBudgetTests
{
    [Fact]
    public void No_comparand_measurement_is_a_distinct_admitted_result()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("groundwork", "store", ArtifactFixture.BookmarkOperations);
        fixture.Bind();

        var measurement = Measurement.Measure(fixture.Directory, WorkloadCatalog.Load(Repository.Root()));

        Assert.Equal(2, measurement.SchemaVersion);
        Assert.Equal(MeasurementResultStatus.Ungraded, measurement.EvaluationStatus);
        Assert.True(measurement.Complete);
        Assert.True(measurement.CorrectnessValid);
        Assert.Equal("sqlite/groundwork/document-type-specific-tables", measurement.Target);
        Assert.NotEmpty(measurement.Operations);
        Assert.Null(measurement.BlockReason);
    }

    [Fact]
    public void Diagnostics_measurement_exception_bypasses_only_the_exact_absolute_budget_block()
    {
        var catalog = WorkloadCatalog.Load(Repository.Root());
        var workload = catalog.Workloads[ReproducibleWorkloadScenarioCatalog.DiagnosticsWorkloadId];
        var request = new RunRequest(
            "diagnostics-cohort", "diagnostics-set", workload.Id, workload.Version, "sqlite",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "ordinary-groundwork-diagnostics-units", "100k", new string('a', 40), new string('b', 64),
            new Dictionary<string, string> { ["Groundwork.Sqlite"] = "0.0.1-preview.103" }, new string('c', 64),
            new string('d', 64), "3.46.0", workload.RequiredProviderEvidence["sqlite"],
            new Dictionary<string, string> { ["journal_mode"] = "wal", ["synchronous"] = "normal" },
            workload.Input.Seed, workload.Input.FingerprintSha256, "diagnostics-plan", "diagnostics-plan.json", new string('e', 64),
            ProcessKind.Measured, 1);

        ArtifactAdmission.ValidateEvidenceRequest(workload, request);
        var blocked = Assert.Throws<PerformanceContractException>(() => ArtifactAdmission.ValidateRequest(workload, request));
        Assert.Contains(ReproducibleWorkloadScenarioCatalog.DiagnosticsBlockedReasonCode, blocked.Message, StringComparison.Ordinal);

        var unrelated = workload;
        var unrelatedRequest = request with
        {
            Adapter = "unreviewed-adapter",
            PhysicalForm = "ordinary-groundwork-diagnostics-units"
        };
        var unrelatedBlocked = Assert.Throws<PerformanceContractException>(() => ArtifactAdmission.ValidateEvidenceRequest(unrelated, unrelatedRequest));
        Assert.DoesNotContain(ReproducibleWorkloadScenarioCatalog.DiagnosticsBlockedReasonCode, unrelatedBlocked.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostics_blocked_route_evidence_remains_blocked_for_ungraded_measurement()
    {
        using var fixture = DiagnosticsArtifactFixture.Create();
        fixture.Bind();

        var measurement = Measurement.Measure(fixture.Directory, WorkloadCatalog.Load(Repository.Root()));

        Assert.Equal(MeasurementResultStatus.Blocked, measurement.EvaluationStatus);
        Assert.False(measurement.Complete);
        Assert.False(measurement.CorrectnessValid);
        Assert.Contains("complete provider-native plan", measurement.BlockReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fully_admitted_diagnostics_measurement_succeeds_when_every_declared_route_is_present()
    {
        using var fixture = DiagnosticsArtifactFixture.Create(fullNativePlan: true);
        fixture.Bind();
        var catalog = DiagnosticsArtifactFixture.CatalogWithDeclaredRoutes(DiagnosticsArtifactFixture.IndexedDiagnosticsRoutes);

        var measurement = Measurement.Measure(fixture.Directory, catalog);

        Assert.Equal(2, measurement.SchemaVersion);
        Assert.Equal(MeasurementResultStatus.Ungraded, measurement.EvaluationStatus);
        Assert.True(measurement.Complete, measurement.BlockReason);
        Assert.True(measurement.CorrectnessValid);
        Assert.Null(measurement.BlockReason);
    }

    [Fact]
    public void Absolute_gate_uses_only_the_measured_target_and_requires_every_operation_budget()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("groundwork", "store", ArtifactFixture.BookmarkOperations);
        fixture.Bind();
        var measurement = Measurement.Measure(fixture.Directory, WorkloadCatalog.Load(Repository.Root()));
        var review = new GateReview(measurement.WorkloadId, measurement.WorkloadVersion, "owner", "reviewer", "#646-review", "2026-09-02T00:00:00Z");

        var policy = AbsoluteBudgetPolicy.Create(
            measurement.WorkloadId,
            measurement.WorkloadVersion,
            measurement.Provider,
            measurement.Operations.ToDictionary(
                operation => operation.Operation,
                _ => new AbsoluteBudget(2d, 2d, .01d),
                StringComparer.Ordinal),
            review);
        var result = AbsoluteBudgetEvaluator.Evaluate(policy, measurement);

        Assert.Equal(PerformanceVerdict.Pass, result.Verdict);
        Assert.Equal(measurement.Operations.Count, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.True(row.Pass));

        var classPolicy = policy with
        {
            Budgets = new Dictionary<string, AbsoluteBudget>(StringComparer.Ordinal) { ["bounded-read"] = new(2d, 2d, .01d) },
            OperationClasses = measurement.Operations.ToDictionary(operation => operation.Operation, _ => "bounded-read", StringComparer.Ordinal)
        };
        var classResult = AbsoluteBudgetEvaluator.Evaluate(classPolicy, measurement);
        Assert.Equal(PerformanceVerdict.Pass, classResult.Verdict);
        Assert.All(classResult.Rows, row => Assert.Equal("bounded-read", row.OperationClass));

        var notHotMapping = measurement.Operations.ToDictionary(operation => operation.Operation, _ => "bounded-read", StringComparer.Ordinal);
        notHotMapping[measurement.Operations[0].Operation] = "NotHotPath";
        var notHotResult = AbsoluteBudgetEvaluator.Evaluate(classPolicy with { OperationClasses = notHotMapping }, measurement);
        Assert.Equal(PerformanceVerdict.Pass, notHotResult.Verdict);
        Assert.Equal(PerformanceVerdict.NotHotPath, notHotResult.Rows.Single(row => row.Operation == measurement.Operations[0].Operation).Verdict);

        var doubleDefined = classPolicy with
        {
            Budgets = classPolicy.Budgets.Concat(new[] { new KeyValuePair<string, AbsoluteBudget>(measurement.Operations[0].Operation, new(2d, 2d, .01d)) })
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        };
        var doubleDefinedResult = AbsoluteBudgetEvaluator.Evaluate(doubleDefined, measurement);
        Assert.Equal(PerformanceVerdict.Blocked, doubleDefinedResult.Verdict);
        Assert.Contains("both a direct budget and a class mapping", doubleDefinedResult.Reason, StringComparison.OrdinalIgnoreCase);

        var extraMapping = classPolicy with
        {
            OperationClasses = classPolicy.OperationClasses!.Concat(new[] { new KeyValuePair<string, string>("unused-operation", "bounded-read") })
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        };
        var extraMappingResult = AbsoluteBudgetEvaluator.Evaluate(extraMapping, measurement);
        Assert.Equal(PerformanceVerdict.Blocked, extraMappingResult.Verdict);
        Assert.Contains("only measured operations", extraMappingResult.Reason, StringComparison.OrdinalIgnoreCase);

        var incomplete = policy with { Budgets = policy.Budgets.Skip(1).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) };
        var blocked = AbsoluteBudgetEvaluator.Evaluate(incomplete, measurement);
        Assert.Equal(PerformanceVerdict.Blocked, blocked.Verdict);
        Assert.Contains("every measured operation", blocked.Reason, StringComparison.OrdinalIgnoreCase);

        var extra = policy with
        {
            Budgets = policy.Budgets.Concat(new[] { new KeyValuePair<string, AbsoluteBudget>("unused-operation", new(2d, 2d, .01d)) })
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        };
        var extraBlocked = AbsoluteBudgetEvaluator.Evaluate(extra, measurement);
        Assert.Equal(PerformanceVerdict.Blocked, extraBlocked.Verdict);
        Assert.Contains("unused or extra", extraBlocked.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Absolute_gate_reports_p99_and_throughput_failures_without_a_ratio_comparand()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("groundwork", "store", ArtifactFixture.BookmarkOperations);
        fixture.Bind();
        var measurement = Measurement.Measure(fixture.Directory, WorkloadCatalog.Load(Repository.Root()));
        var review = new GateReview(measurement.WorkloadId, measurement.WorkloadVersion, "owner", "reviewer", "#646-review", "2026-09-02T00:00:00Z");
        var policy = AbsoluteBudgetPolicy.Create(
            measurement.WorkloadId,
            measurement.WorkloadVersion,
            measurement.Provider,
            measurement.Operations.ToDictionary(
                operation => operation.Operation,
                _ => new AbsoluteBudget(2d, .5d, 4d),
                StringComparer.Ordinal),
            review);

        var result = AbsoluteBudgetEvaluator.Evaluate(policy, measurement);

        Assert.Equal(PerformanceVerdict.Redesign, result.Verdict);
        Assert.All(result.Rows, row =>
        {
            Assert.False(row.P99Pass);
            Assert.False(row.ThroughputPass);
            Assert.False(row.Pass);
        });

        var p95Failure = policy with
        {
            Budgets = policy.Budgets.ToDictionary(pair => pair.Key, _ => new AbsoluteBudget(.5d, 2d, .01d), StringComparer.Ordinal)
        };
        var p95Result = AbsoluteBudgetEvaluator.Evaluate(p95Failure, measurement);
        Assert.Equal(PerformanceVerdict.Redesign, p95Result.Verdict);
        Assert.All(p95Result.Rows, row => Assert.False(row.P95Pass));
    }

    [Fact]
    public void Absolute_gate_preserves_block_reason_and_rejects_tampering_and_bad_review()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("groundwork", "store", ArtifactFixture.BookmarkOperations);
        fixture.Bind();
        var measurement = Measurement.Measure(fixture.Directory, WorkloadCatalog.Load(Repository.Root()));
        var review = new GateReview(measurement.WorkloadId, measurement.WorkloadVersion, "owner", "reviewer", "#646-review", "2026-09-02T00:00:00Z");
        var policy = AbsoluteBudgetPolicy.Create(measurement.WorkloadId, measurement.WorkloadVersion, measurement.Provider,
            measurement.Operations.ToDictionary(operation => operation.Operation, _ => new AbsoluteBudget(2d, 2d, .01d), StringComparer.Ordinal), review);

        var tampered = measurement with { Operations = measurement.Operations.Skip(1).ToArray() };
        var tamperResult = AbsoluteBudgetEvaluator.Evaluate(policy, tampered);
        Assert.Equal(PerformanceVerdict.Blocked, tamperResult.Verdict);
        Assert.Contains("changed after artifact admission", tamperResult.Reason, StringComparison.OrdinalIgnoreCase);

        var selfReviewed = policy with { Review = review with { ReviewedBy = review.ProposedBy } };
        var selfResult = AbsoluteBudgetEvaluator.Evaluate(selfReviewed, measurement);
        Assert.Equal(PerformanceVerdict.Blocked, selfResult.Verdict);
        Assert.Contains("independent review", selfResult.Reason, StringComparison.OrdinalIgnoreCase);

        var invalid = policy with
        {
            Budgets = measurement.Operations.ToDictionary(operation => operation.Operation, _ => new AbsoluteBudget(double.NaN, 2d, .01d), StringComparer.Ordinal)
        };
        var invalidResult = AbsoluteBudgetEvaluator.Evaluate(invalid, measurement);
        Assert.Equal(PerformanceVerdict.Blocked, invalidResult.Verdict);
        Assert.Contains("finite positive", invalidResult.Reason, StringComparison.OrdinalIgnoreCase);

        var wrongProvider = policy with { Provider = "postgresql" };
        var providerResult = AbsoluteBudgetEvaluator.Evaluate(wrongProvider, measurement);
        Assert.Equal(PerformanceVerdict.Blocked, providerResult.Verdict);
        Assert.Contains("does not match", providerResult.Reason, StringComparison.OrdinalIgnoreCase);

        using var blockedFixture = ArtifactFixture.Create();
        blockedFixture.WriteTarget("groundwork", "store", ArtifactFixture.BookmarkOperations, omitFromRun: (2, ArtifactFixture.BookmarkOperations[0]));
        blockedFixture.Bind();
        var blockedMeasurement = Measurement.Measure(blockedFixture.Directory, WorkloadCatalog.Load(Repository.Root()));
        var blockedPolicy = AbsoluteBudgetPolicy.Create(blockedMeasurement.WorkloadId, blockedMeasurement.WorkloadVersion, blockedMeasurement.Provider, new Dictionary<string, AbsoluteBudget>(StringComparer.Ordinal), review);
        var blockedResult = AbsoluteBudgetEvaluator.Evaluate(blockedPolicy, blockedMeasurement);
        Assert.Equal(PerformanceVerdict.Blocked, blockedResult.Verdict);
        Assert.Contains(blockedMeasurement.BlockReason!, blockedResult.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Budget_gate_cli_writes_a_separate_result_and_rejects_a_ratio_gate_shape()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("groundwork", "store", ArtifactFixture.BookmarkOperations);
        fixture.Bind();
        var measurement = Measurement.Measure(fixture.Directory, WorkloadCatalog.Load(Repository.Root()));
        var policyPath = Path.Combine(System.IO.Path.GetTempPath(), $"elsa646-absolute-policy-{Guid.NewGuid():N}.json");
        try
        {
            var policy = AbsoluteBudgetPolicy.Create(
                measurement.WorkloadId,
                measurement.WorkloadVersion,
                measurement.Provider,
                measurement.Operations.ToDictionary(operation => operation.Operation, _ => new AbsoluteBudget(2d, 2d, .01d), StringComparer.Ordinal),
                new GateReview(measurement.WorkloadId, measurement.WorkloadVersion, "owner", "reviewer", "#646-review", "2026-09-02T00:00:00Z"));
            File.WriteAllText(policyPath, JsonSerializer.Serialize(policy, ArtifactStore.JsonOptions));

            var exitCode = await BenchmarkCli.RunForTestAsync(["budget-gate", "--out", fixture.Directory, "--policy", policyPath]);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(fixture.Directory, "measurement.v1.json")));
            Assert.True(File.Exists(Path.Combine(fixture.Directory, "budget-gate.v1.json")));
            using var report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture.Directory, "budget-gate.v1.json")));
            Assert.Equal((int)PerformanceVerdict.Pass, report.RootElement.GetProperty("Payload").GetProperty("Verdict").GetProperty("Verdict").GetInt32());

            var rejected = await BenchmarkCli.RunForTestAsync(["budget-gate", "--out", fixture.Directory, "--policy", policyPath, "--measurement-result", "ignored.json"]);
            Assert.Equal(2, rejected);
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public async Task Budget_gate_cli_preserves_an_incomplete_measurement_without_loading_a_policy()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget(
            "groundwork",
            "store",
            ArtifactFixture.BookmarkOperations,
            omitFromRun: (2, ArtifactFixture.BookmarkOperations[0]));
        fixture.Bind();
        var measurement = Measurement.Measure(fixture.Directory, WorkloadCatalog.Load(Repository.Root()));

        var exitCode = await BenchmarkCli.RunForTestAsync(["budget-gate", "--out", fixture.Directory]);

        Assert.Equal(2, exitCode);
        using var report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture.Directory, "budget-gate.v1.json")));
        var verdict = report.RootElement.GetProperty("Payload").GetProperty("Verdict");
        Assert.Equal((int)PerformanceVerdict.Blocked, verdict.GetProperty("Verdict").GetInt32());
        Assert.Equal(measurement.BlockReason, verdict.GetProperty("Reason").GetString());
        Assert.Equal(JsonValueKind.Null, report.RootElement.GetProperty("Payload").GetProperty("PolicySha256").ValueKind);
    }
}

/// <summary>Small, deterministic envelope fixture for exercising diagnostics admission without running a provider.
/// The native routes deliberately contain only the two currently indexed routes; the remaining routes are explicit
/// blocked evidence, matching the diagnostics contract.</summary>
internal sealed class DiagnosticsArtifactFixture : IDisposable
{
    private static readonly string[] IndexedRoutes = ["resources-by-last-seen", "traces-by-last-seen"];
    internal static readonly string[] IndexedDiagnosticsRoutes = ["resources-by-last-seen", "traces-by-last-seen"];
    private readonly bool fullNativePlan;

    private DiagnosticsArtifactFixture(string directory, bool fullNativePlan)
    {
        Directory = directory;
        this.fullNativePlan = fullNativePlan;
    }
    public string Directory { get; }

    public static DiagnosticsArtifactFixture Create(bool fullNativePlan = false)
    {
        var fixture = new DiagnosticsArtifactFixture(Path.Combine(Path.GetTempPath(), "elsa646-diagnostics-artifacts-", Guid.NewGuid().ToString("N")), fullNativePlan);
        fixture.Write();
        return fixture;
    }

    public void Bind() => ArtifactStore.WriteManifest(Directory);

    public static WorkloadCatalog CatalogWithDeclaredRoutes(IReadOnlyList<string> routes)
    {
        var source = WorkloadCatalog.Load(Repository.Root());
        var workloads = source.Workloads.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var diagnostics = workloads[ReproducibleWorkloadScenarioCatalog.DiagnosticsWorkloadId];
        workloads[diagnostics.Id] = diagnostics with { RequiredNativeRoutes = routes };
        var constructor = typeof(WorkloadCatalog).GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, [typeof(IReadOnlyDictionary<string, PerformanceWorkload>)], null)!;
        return (WorkloadCatalog)constructor.Invoke([workloads]);
    }

    private void Write()
    {
        var catalog = WorkloadCatalog.Load(Repository.Root());
        var workload = catalog.Workloads[ReproducibleWorkloadScenarioCatalog.DiagnosticsWorkloadId];
        var provider = "sqlite";
        var adapter = DiagnosticsNativePlanContract.GroundworkAdapter;
        var form = "ordinary-groundwork-diagnostics-units";
        var topology = workload.RequiredProviderEvidence[provider];
        var host = new string('d', 64);
        var commit = new string('a', 40);
        var harness = new string('b', 64);
        var composition = new string('c', 64);
        var packageVersions = new Dictionary<string, string> { ["Groundwork.Sqlite"] = "0.0.1-preview.103" };
        var configuration = new Dictionary<string, string> { ["journal_mode"] = "wal", ["synchronous"] = "normal" };
        var routes = IndexedRoutes.Select(Route).ToArray();
        var blockedRoutes = fullNativePlan ? [] : workload.RequiredNativeRoutes.Except(IndexedRoutes, StringComparer.Ordinal).ToArray();
        var evidenceReference = "diagnostics.native-plan.json";
        var identity = "diagnostics-plan";
        var rawPlanHashes = routes.ToDictionary(route => route.RouteIdentity, route => route.RawPlanSha256, StringComparer.Ordinal);
        var evidence = new NativePlanEvidence(identity, evidenceReference, new string('e', 64), routes)
        {
            RouteContract = fullNativePlan ? "provider-native-routes" : DiagnosticsNativePlanContract.BlockedRouteContract,
            BlockedRoutes = blockedRoutes
        };
        var evidenceDocument = new NativePlanEvidenceDocument(
            2, "diagnostics-cohort", "diagnostics-set", workload.Id, workload.Version, provider, adapter, form,
            "100k", commit, harness, composition, host, "3.46.0", topology, configuration,
            workload.Input.Seed, workload.Input.FingerprintSha256, identity, routes,
            fullNativePlan ? "provider-native-routes" : DiagnosticsNativePlanContract.BlockedRouteContract, blockedRoutes);
        var evidenceBytes = JsonSerializer.SerializeToUtf8Bytes(evidenceDocument, ArtifactStore.JsonOptions);
        evidence = evidence with { ContentSha256 = Hash(evidenceBytes) };
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllBytes(Path.Combine(Directory, evidenceReference), evidenceBytes);
        foreach (var route in routes)
        {
            var artifact = route.RouteIdentity == "resources-by-last-seen"
                ? new DiagnosticsNativePlanArtifact(1, provider, adapter, route.RouteIdentity, "elsa_otel_resources_v2", "elsa_otel_resources_last_seen",
                    route.IndexName,
                    "SELECT * FROM elsa_otel_resources_v2 WHERE __groundwork_scope = @scope ORDER BY lastSeen DESC, idOrderKey ASC, id ASC LIMIT 127",
                    $"2 0 SEARCH elsa_otel_resources_v2 USING INDEX {route.IndexName} (__groundwork_scope=?)")
                : new DiagnosticsNativePlanArtifact(1, provider, adapter, route.RouteIdentity, "elsa_otel_trace_summaries_v3", "elsa_otel_trace_summaries_start",
                    route.IndexName,
                    "SELECT * FROM elsa_otel_trace_summaries_v3 WHERE __groundwork_scope = @scope ORDER BY startTime DESC, traceKey ASC LIMIT 127",
                    $"2 0 SEARCH elsa_otel_trace_summaries_v3 USING INDEX {route.IndexName} (__groundwork_scope=?)");
            var rawBytes = JsonSerializer.SerializeToUtf8Bytes(artifact, ArtifactStore.JsonOptions);
            var rawPath = Path.Combine(Directory, route.RawPlanReference);
            File.WriteAllBytes(rawPath, rawBytes);
            if (Hash(rawBytes) != rawPlanHashes[route.RouteIdentity])
                throw new InvalidOperationException("Diagnostics fixture route digest was not deterministic.");
        }

        foreach (var kind in new[] { ProcessKind.Warmup, ProcessKind.Measured })
            foreach (var index in kind == ProcessKind.Warmup ? new[] { 0 } : new[] { 1, 2, 3 })
            {
                var request = new RunRequest("diagnostics-cohort", "diagnostics-set", workload.Id, workload.Version, provider, adapter, form, "100k", commit, harness, packageVersions, composition, host, "3.46.0", topology, configuration, workload.Input.Seed, workload.Input.FingerprintSha256, identity, evidenceReference, Hash(evidenceBytes), kind, index);
                var samples = kind == ProcessKind.Warmup
                    ? Array.Empty<OperationSample>()
                    : workload.OperationSequence.Select(operation => new OperationSample(operation, 100, 30, 100d / 30d, 1d, 1d, 1d, Enumerable.Repeat(1d, 100).ToArray()) { RoundTrips = 100, RawRoundTrips = Enumerable.Repeat(1L, 100).ToArray() }).ToArray();
                ArtifactStore.Write(Directory, new ProcessArtifact(2, request, BenchmarkProtocol.Acceptance, true, new CorrectnessEvidence(workload.Correctness.ResultDigestSha256, request.ProviderVersion, topology, configuration, evidence), samples, new MachineMetadata("test-os", "test-runtime", "X64", "X64", 1, host, "2026-09-02T00:00:00Z")) { RoundTripInstrumentation = kind == ProcessKind.Measured ? "test-observer" : null });
            }
    }

    private NativeRouteEvidence Route(string identity)
    {
        var specification = DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, identity);
        var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("sqlite", specification);
        var rawReference = $"{identity}.raw.json";
        var rawContent = identity == "resources-by-last-seen"
            ? new DiagnosticsNativePlanArtifact(1, "sqlite", DiagnosticsNativePlanContract.GroundworkAdapter, identity, specification.TableName, specification.IndexName, physicalIndex, "SELECT * FROM elsa_otel_resources_v2 WHERE __groundwork_scope = @scope ORDER BY lastSeen DESC, idOrderKey ASC, id ASC LIMIT 127", $"2 0 SEARCH elsa_otel_resources_v2 USING INDEX {physicalIndex} (__groundwork_scope=?)")
            : new DiagnosticsNativePlanArtifact(1, "sqlite", DiagnosticsNativePlanContract.GroundworkAdapter, identity, specification.TableName, specification.IndexName, physicalIndex, "SELECT * FROM elsa_otel_trace_summaries_v3 WHERE __groundwork_scope = @scope ORDER BY startTime DESC, traceKey ASC LIMIT 127", $"2 0 SEARCH elsa_otel_trace_summaries_v3 USING INDEX {physicalIndex} (__groundwork_scope=?)");
        return new NativeRouteEvidence(identity, rawReference, Hash(JsonSerializer.SerializeToUtf8Bytes(rawContent, ArtifactStore.JsonOptions)), "index-search", physicalIndex, specification.PhysicalCardinality, true, false, specification.FiniteLimit, specification.FiniteLimit);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true); }
}
