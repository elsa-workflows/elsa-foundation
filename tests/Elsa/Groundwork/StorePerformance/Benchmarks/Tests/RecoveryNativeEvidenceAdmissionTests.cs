using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class RecoveryNativeEvidenceAdmissionTests
{
    [Fact]
    public void Exact_recovery_native_routes_are_admitted()
    {
        using var fixture = EvidenceFixture.Create();

        ArtifactAdmission.ValidateCorrectness(
            fixture.Workload,
            fixture.Request,
            fixture.Evidence,
            fixture.Directory);
    }

    [Theory]
    [InlineData(2047, 1, 1)]
    [InlineData(2048, 2, 1)]
    [InlineData(2048, 1, 2)]
    public void Recovery_native_routes_require_the_frozen_page_facts(
        int physicalCardinality,
        int finiteLimit,
        int materializedCandidateCount)
    {
        using var fixture = EvidenceFixture.Create(routes => routes
            .Select(route => route with
            {
                PhysicalCardinality = physicalCardinality,
                FiniteLimit = finiteLimit,
                MaterializedCandidateCount = materializedCandidateCount
            })
            .ToArray());

        var error = Assert.Throws<PerformanceContractException>(() =>
            ArtifactAdmission.ValidateCorrectness(
                fixture.Workload,
                fixture.Request,
                fixture.Evidence,
                fixture.Directory));

        Assert.Contains("Recovery native-plan", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_native_routes_require_the_frozen_route_names()
    {
        using var fixture = EvidenceFixture.Create(routes => routes
            .Select(route => route.RouteIdentity == "list-recovery-by-heartbeat"
                ? route with { RouteIdentity = "list-recovery-by-heartbeat-drift" }
                : route)
            .ToArray());

        var error = Assert.Throws<PerformanceContractException>(() =>
            ArtifactAdmission.ValidateCorrectness(
                fixture.Workload,
                fixture.Request,
                fixture.Evidence,
                fixture.Directory));

        Assert.Contains("Recovery native-plan", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_native_admission_rejects_arbitrary_explain_text_even_when_the_digest_is_self_consistent()
    {
        using var fixture = EvidenceFixture.Create(rawPlanOverride: "EXPLAIN list-recovery-detected");

        Assert.Throws<PerformanceContractException>(() => RecoveryRetainedNativePlan.Validate(
            "sqlite",
            fixture.Evidence.NativePlan.Routes[0],
            "EXPLAIN list-recovery-detected"));

        Assert.Throws<PerformanceContractException>(() => ArtifactAdmission.ValidateCorrectness(
            fixture.Workload,
            fixture.Request,
            fixture.Evidence,
            fixture.Directory));
    }

    [Fact]
    public void Recovery_native_admission_rejects_a_physical_scan()
    {
        var route = EvidenceFixture.Route("list-recovery-detected");
        var retained = EvidenceFixture.RawPlan("list-recovery-detected")
            .Replace("SEARCH runtime_execution_liveness_state USING INDEX by_recovery_detected", "SCAN runtime_execution_liveness_state", StringComparison.Ordinal);

        Assert.Throws<PerformanceContractException>(() => RecoveryRetainedNativePlan.Validate("sqlite", route, retained));
    }

    [Fact]
    public void Recovery_native_admission_rejects_a_wrong_predicate()
    {
        var route = EvidenceFixture.Route("list-recovery-detected");
        var retained = EvidenceFixture.RawPlan("list-recovery-detected")
            .Replace("interruptedExecutionStatus", "executionLeaseExpiresAt", StringComparison.Ordinal);

        Assert.Throws<PerformanceContractException>(() => RecoveryRetainedNativePlan.Validate("sqlite", route, retained));
    }

    [Fact]
    public void Recovery_native_admission_rejects_a_strict_sqlite_command_for_an_inclusive_cutoff()
    {
        var route = EvidenceFixture.Route("list-recovery-by-lease-expiry");
        var retained = EvidenceFixture.RawPlan("list-recovery-by-lease-expiry")
            .Replace("executionLeaseExpiresAt <= @due", "executionLeaseExpiresAt < @due", StringComparison.Ordinal);

        Assert.Throws<PerformanceContractException>(() => RecoveryRetainedNativePlan.Validate("sqlite", route, retained));
    }

    [Fact]
    public void Recovery_native_admission_rejects_a_wrong_index()
    {
        var route = EvidenceFixture.Route("list-recovery-detected");
        var retained = EvidenceFixture.RawPlan("list-recovery-detected")
            .Replace("by_recovery_detected", "wrong_recovery_index", StringComparison.Ordinal);

        Assert.Throws<PerformanceContractException>(() => RecoveryRetainedNativePlan.Validate("sqlite", route, retained));
    }

    [Fact]
    public void Recovery_native_admission_rejects_a_wrong_literal_limit()
    {
        var route = EvidenceFixture.Route("list-recovery-detected");
        var retained = EvidenceFixture.RawPlan("list-recovery-detected")
            .Replace("LIMIT 1", "LIMIT 2", StringComparison.Ordinal);

        Assert.Throws<PerformanceContractException>(() => RecoveryRetainedNativePlan.Validate("sqlite", route, retained));
    }

    [Fact]
    public void Recovery_native_admission_rejects_a_wrong_order()
    {
        var route = EvidenceFixture.Route("list-recovery-detected");
        var retained = EvidenceFixture.RawPlan("list-recovery-detected")
            .Replace("ORDER BY interruptedExecutionAt, workflowExecutionId, operationalStateId", "ORDER BY workflowExecutionId, interruptedExecutionAt, operationalStateId", StringComparison.Ordinal);

        Assert.Throws<PerformanceContractException>(() => RecoveryRetainedNativePlan.Validate("sqlite", route, retained));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("mongodb")]
    public void Recovery_native_admission_accepts_provider_native_index_structures(string provider)
    {
        var route = EvidenceFixture.Route("list-recovery-detected");

        RecoveryRetainedNativePlan.Validate(
            provider,
            route,
            EvidenceFixture.ProviderPlan("list-recovery-detected", provider));
    }

    private sealed class EvidenceFixture : IDisposable
    {
        private EvidenceFixture(
            PerformanceWorkload workload,
            RunRequest request,
            CorrectnessEvidence evidence,
            string directory)
        {
            Workload = workload;
            Request = request;
            Evidence = evidence;
            Directory = directory;
        }

        public PerformanceWorkload Workload { get; }
        public RunRequest Request { get; }
        public CorrectnessEvidence Evidence { get; }
        public string Directory { get; }

        public static EvidenceFixture Create(
            Func<IReadOnlyList<NativeRouteEvidence>, IReadOnlyList<NativeRouteEvidence>>? transform = null,
            string? rawPlanOverride = null)
        {
            var workload = WorkloadCatalog.Load(Repository.Root()).Workloads[RuntimeRecoveryScanWorkload.WorkloadId];
            var directory = Path.Combine(Path.GetTempPath(), $"elsa646-recovery-native-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            var originalRoutes = RuntimeRecoveryScanWorkload.NativeRouteIdentities
                .Select(route => Route(route, rawPlanOverride))
                .ToArray();
            var rawPlans = originalRoutes.ToDictionary(
                route => route.RawPlanReference,
                route => rawPlanOverride ?? RawPlan(route.RouteIdentity),
                StringComparer.Ordinal);
            var routes = (transform?.Invoke(originalRoutes) ?? originalRoutes).ToArray();

            var request = new RunRequest(
                "recovery-native-cohort",
                "recovery-native-set",
                workload.Id,
                workload.Version,
                "sqlite",
                "groundwork-v2-runtime",
                "recovery-candidate-index",
                "2048",
                new string('a', 40),
                new string('b', 64),
                new Dictionary<string, string> { ["Groundwork.Runtime"] = "0.0.1-preview.60" },
                new string('c', 64),
                new string('d', 64),
                "3.46.0",
                "file-backed-distinct-connections",
                new Dictionary<string, string> { ["journal_mode"] = "wal" },
                workload.Input.Seed,
                workload.Input.FingerprintSha256,
                "recovery-native-plan",
                "recovery-native-plan.json",
                new string('e', 64),
                ProcessKind.Measured,
                1);

            foreach (var route in routes)
                File.WriteAllText(Path.Combine(directory, route.RawPlanReference), rawPlans[route.RawPlanReference]);

            var document = new NativePlanEvidenceDocument(
                2,
                request.ComparisonCohortId,
                request.MeasurementSetId,
                request.WorkloadId,
                request.WorkloadVersion,
                request.Provider,
                request.Adapter,
                request.PhysicalForm,
                request.Scale,
                request.CommitSha,
                request.HarnessAssemblySha256,
                request.CompositionFingerprint,
                request.HostFingerprintSha256,
                request.ProviderVersion,
                request.ProviderTopology,
                request.ProviderConfiguration,
                request.Seed,
                request.InputFingerprintSha256,
                request.NativePlanIdentity,
                routes,
                "provider-native-routes");
            var evidencePath = Path.Combine(directory, request.NativePlanEvidenceReference);
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(document, ArtifactStore.JsonOptions));
            request = request with { NativePlanContentSha256 = ArtifactStore.HashFile(evidencePath) };

            return new EvidenceFixture(
                workload,
                request,
                new CorrectnessEvidence(
                    workload.Correctness.ResultDigestSha256,
                    request.ProviderVersion,
                    request.ProviderTopology,
                    request.ProviderConfiguration,
                    new NativePlanEvidence(
                        request.NativePlanIdentity,
                        request.NativePlanEvidenceReference,
                        request.NativePlanContentSha256,
                        routes)),
                directory);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }

        internal static NativeRouteEvidence Route(string routeIdentity, string? rawPlanOverride = null)
        {
            var definition = RecoveryRetainedNativePlan.Definition(routeIdentity);
            var rawPlan = rawPlanOverride ?? RawPlan(routeIdentity);
            return new(
                routeIdentity,
                $"{routeIdentity}.raw.txt",
                Hash(rawPlan),
                "index-search",
                definition.IndexName,
                definition.PhysicalCardinality,
                true,
                true,
                definition.FiniteLimit,
                definition.MaterializedCandidateCount);
        }

        internal static string RawPlan(string routeIdentity) =>
            RecoveryRetainedNativePlan.Create(
                "sqlite",
                routeIdentity.EndsWith("-drift", StringComparison.Ordinal)
                    ? routeIdentity[..^"-drift".Length]
                    : routeIdentity,
                ProviderCommand(routeIdentity),
                SqlitePlan(routeIdentity));

        internal static string ProviderPlan(string routeIdentity, string provider)
        {
            var definition = RecoveryRetainedNativePlan.Definition(routeIdentity);
            var order = string.Join(", ", definition.OrderFields.Select(field => $"\"{field}\""));
            var mongoOperator = definition.PredicateOperator == "=" ? "$eq" : "$lte";
            var command = provider == "mongodb"
                ? $"{{\"find\":\"runtime_execution_liveness_state\",\"filter\":{{\"__groundwork_scope\":\"scope\",\"{definition.PredicateField}\":{{\"{mongoOperator}\":1}}}},\"sort\":{{\"{definition.OrderFields[0]}\":1,\"{definition.OrderFields[1]}\":1,\"{definition.OrderFields[2]}\":1}},\"limit\":1}}"
                : $"SELECT * FROM runtime_execution_liveness_state WHERE __groundwork_scope = @scope AND {definition.PredicateField} {definition.PredicateOperator} @due ORDER BY {order} LIMIT 1";
            var plan = provider switch
            {
                "sqlite" => SqlitePlan(routeIdentity),
                "postgresql" => $"[{{\"Plan\":{{\"Node Type\":\"Index Scan\",\"Index Name\":\"{definition.IndexName}\",\"Index Cond\":\"({definition.PredicateField} {definition.PredicateOperator} 1)\"}}}}]",
                "sqlserver" => $"<ShowPlanXML><RelOp PhysicalOp=\"Index Seek\"><IndexScan><Object Index=\"[{definition.IndexName}]\" /><SeekPredicates><SeekPredicateNew><SeekKeys><Prefix ScanType=\"EQ\"><RangeColumns><ColumnReference Column=\"{definition.PredicateField}\" /></RangeColumns></Prefix></SeekKeys></SeekPredicateNew></SeekPredicates></IndexScan></RelOp></ShowPlanXML>",
                "mongodb" => $"{{\"command\":{{\"find\":\"runtime_execution_liveness_state\",\"filter\":{{\"{definition.PredicateField}\":{{\"{mongoOperator}\":1}}}}}},\"queryPlanner\":{{\"winningPlan\":{{\"stage\":\"IXSCAN\",\"indexName\":\"{definition.IndexName}\"}}}}}}",
                _ => throw new ArgumentOutOfRangeException(nameof(provider))
            };
            return RecoveryRetainedNativePlan.Create(provider, routeIdentity, command, plan);
        }

        private static string ProviderCommand(string routeIdentity)
        {
            var definition = RecoveryRetainedNativePlan.Definition(routeIdentity.EndsWith("-drift", StringComparison.Ordinal)
                ? routeIdentity[..^"-drift".Length]
                : routeIdentity);
            return $"SELECT * FROM runtime_execution_liveness_state WHERE __groundwork_scope = @scope AND {definition.PredicateField} {definition.PredicateOperator} @due ORDER BY {string.Join(", ", definition.OrderFields)} LIMIT 1";
        }

        private static string SqlitePlan(string routeIdentity)
        {
            var definition = RecoveryRetainedNativePlan.Definition(routeIdentity.EndsWith("-drift", StringComparison.Ordinal)
                ? routeIdentity[..^"-drift".Length]
                : routeIdentity);
            return $"2\t0\tSEARCH runtime_execution_liveness_state USING INDEX {definition.IndexName} ({definition.PredicateField}{(definition.PredicateOperator == "=" ? "=?" : "<?")})";
        }

        private static string Hash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
