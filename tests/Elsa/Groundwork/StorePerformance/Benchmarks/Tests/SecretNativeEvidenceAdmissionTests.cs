using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class SecretNativeEvidenceAdmissionTests
{
    [Fact]
    public void Exact_secret_native_route_evidence_is_admitted()
    {
        using var fixture = EvidenceFixture.Create();

        ArtifactAdmission.ValidateCorrectness(
            fixture.Workload,
            fixture.Request,
            fixture.Evidence,
            fixture.Directory);

        var roundTrip = JsonSerializer.Deserialize<CorrectnessEvidence>(
            JsonSerializer.Serialize(fixture.Evidence, ArtifactStore.JsonOptions),
            ArtifactStore.JsonOptions);
        Assert.Equal(
            EvidenceFixture.Concurrency(),
            Assert.IsType<SecretProviderConcurrencyEvidence>(roundTrip!.NativePlan.ProviderConcurrency));

        var comparison = new ComparisonResult(
            1,
            new string('f', 64),
            fixture.Workload.Id,
            fixture.Workload.Version,
            fixture.Request.Provider,
            fixture.Request.Scale,
            "sqlite/ef-secret-repository/entity-type-specific-physical-tables",
            "sqlite/groundwork-secret-repository/entity-type-specific-physical-tables",
            Complete: true,
            CorrectnessEqual: true,
            [],
            [],
            null)
        {
            OracleProviderConcurrency = EvidenceFixture.Concurrency() with
            {
                ProviderCommandOverlapObserved = true,
                ProviderCommandsSerializedByDesign = false,
                DistinctPhysicalConnectionCount = 2
            },
            TargetProviderConcurrency = EvidenceFixture.Concurrency()
        };
        ArtifactSafety.Validate(comparison);
        var comparisonRoundTrip = JsonSerializer.Deserialize<ComparisonResult>(
            JsonSerializer.Serialize(comparison, ArtifactStore.JsonOptions),
            ArtifactStore.JsonOptions);
        Assert.Equal(comparison.OracleProviderConcurrency, comparisonRoundTrip!.OracleProviderConcurrency);
        Assert.Equal(comparison.TargetProviderConcurrency, comparisonRoundTrip.TargetProviderConcurrency);
    }

    [Theory]
    [InlineData(67, 16, 16)]
    [InlineData(68, 15, 15)]
    [InlineData(68, 16, 15)]
    public void Secret_native_route_evidence_requires_the_frozen_page_facts(
        int physicalCardinality,
        int finiteLimit,
        int materializedCandidateCount)
    {
        using var fixture = EvidenceFixture.Create(route => route with
        {
            PhysicalCardinality = physicalCardinality,
            FiniteLimit = finiteLimit,
            MaterializedCandidateCount = materializedCandidateCount
        });

        var error = Assert.Throws<PerformanceContractException>(() =>
            ArtifactAdmission.ValidateCorrectness(
                fixture.Workload,
                fixture.Request,
                fixture.Evidence,
                fixture.Directory));

        Assert.Contains("Secret native-plan", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Secret_native_admission_reparses_the_retained_index_instead_of_trusting_the_summary()
    {
        using var fixture = EvidenceFixture.Create(route => route with { IndexName = "forged_index" });

        Assert.Throws<PerformanceContractException>(() =>
            ArtifactAdmission.ValidateCorrectness(fixture.Workload, fixture.Request, fixture.Evidence, fixture.Directory));
    }

    [Fact]
    public void Secret_native_admission_rejects_missing_raw_predicates_even_when_summary_flags_are_true()
    {
        using var fixture = EvidenceFixture.Create(
            providerPlan: "2\t0\tSEARCH elsa_secrets USING INDEX elsa_secrets_filtered_list (__groundwork_scope=? AND tenantId=?)");

        Assert.Throws<PerformanceContractException>(() =>
            ArtifactAdmission.ValidateCorrectness(fixture.Workload, fixture.Request, fixture.Evidence, fixture.Directory));
    }

    [Fact]
    public void Secret_native_admission_binds_physical_cardinality_inside_the_hashed_raw_plan()
    {
        using var fixture = EvidenceFixture.Create(retainedPhysicalCardinality: 67);

        Assert.Throws<PerformanceContractException>(() =>
            ArtifactAdmission.ValidateCorrectness(fixture.Workload, fixture.Request, fixture.Evidence, fixture.Directory));
    }

    [Fact]
    public void Secret_concurrency_evidence_is_admitted_as_machine_verifiable_correctness_data()
    {
        using var fixture = EvidenceFixture.Create(concurrency: EvidenceFixture.Concurrency() with
        {
            ProviderCommandStartCount = 1,
            ProviderCommandsSerializedByDesign = false
        });

        Assert.Throws<PerformanceContractException>(() =>
            ArtifactAdmission.ValidateCorrectness(fixture.Workload, fixture.Request, fixture.Evidence, fixture.Directory));
    }

    [Fact]
    public void Secret_retained_sqlite_plan_rejects_scan_aliases_even_when_an_index_search_is_present()
    {
        var retained = SecretRetainedNativePlan.Create(
            "sqlite",
            68,
            SecretCreateReadListWorkload.PageSize,
            SecretCreateReadListWorkload.PageSize,
            "2\t0\tSEARCH elsa_secrets AS s1 USING INDEX elsa_secrets_filtered_list (__groundwork_scope=? AND tenantId=? AND status=?)\n3\t0\tSCAN s1");

        Assert.Throws<PerformanceContractException>(() => SecretRetainedNativePlan.Validate(
            "sqlite",
            "groundwork-secret-repository",
            EvidenceFixture.Route("elsa_secrets_filtered_list"),
            retained));
    }

    [Fact]
    public void Secret_retained_mongo_plan_requires_the_exact_frozen_pipeline_limit()
    {
        var retained = SecretRetainedNativePlan.Create(
            "mongodb",
            68,
            SecretCreateReadListWorkload.PageSize,
            SecretCreateReadListWorkload.PageSize,
            """
            {
              "command": {
                "aggregate": "elsa_secrets",
                "pipeline": [
                  { "$match": { "tenantId": { "$eq": "?" }, "status": { "$eq": "?" } } },
                  { "$limit": 15 }
                ]
              },
              "queryPlanner": {
                "winningPlan": { "stage": "IXSCAN", "indexName": "elsa_secrets_filtered_list" }
              }
            }
            """);

        Assert.Throws<PerformanceContractException>(() => SecretRetainedNativePlan.Validate(
            "mongodb",
            "groundwork-secret-repository",
            EvidenceFixture.Route("elsa_secrets_filtered_list"),
            retained));
    }

    [Fact]
    public void Secret_retained_postgresql_plan_does_not_treat_column_mentions_as_equality_proof()
    {
        var retained = SecretRetainedNativePlan.Create(
            "postgresql",
            68,
            SecretCreateReadListWorkload.PageSize,
            SecretCreateReadListWorkload.PageSize,
            """
            [{"Plan":{"Node Type":"Index Scan","Index Name":"elsa_secrets_filtered_list","Index Cond":"(TenantId IS NOT NULL)","Filter":"(status IS NOT NULL)"}}]
            """);

        Assert.Throws<PerformanceContractException>(() => SecretRetainedNativePlan.Validate(
            "postgresql",
            "ef-secret-repository",
            EvidenceFixture.Route("elsa_secrets_filtered_list"),
            retained));
    }

    [Fact]
    public void Secret_retained_postgresql_plan_admits_direct_casted_equality_predicates()
    {
        var retained = SecretRetainedNativePlan.Create(
            "postgresql",
            68,
            SecretCreateReadListWorkload.PageSize,
            SecretCreateReadListWorkload.PageSize,
            """
            [{"Plan":{"Node Type":"Index Scan","Index Name":"elsa_secrets_filtered_list","Index Cond":"((\"__groundwork_scope\" = 'scope'::text) AND (\"tenantId\" = 'tenant'::text) AND (status = 'Active'::text))"}}]
            """);

        SecretRetainedNativePlan.Validate(
            "postgresql",
            "groundwork-secret-repository",
            EvidenceFixture.Route("elsa_secrets_filtered_list"),
            retained);
    }

    [Fact]
    public void Secret_retained_sqlserver_plan_requires_structural_equality_not_bare_column_references()
    {
        var retained = SecretRetainedNativePlan.Create(
            "sqlserver",
            68,
            SecretCreateReadListWorkload.PageSize,
            SecretCreateReadListWorkload.PageSize,
            """
            <ShowPlanXML><RelOp PhysicalOp="Index Seek"><IndexScan><Object Index="[elsa_secrets_filtered_list]" /><Predicate><ColumnReference Column="[TenantId]" /><ColumnReference Column="[status]" /></Predicate></IndexScan></RelOp></ShowPlanXML>
            """);

        Assert.Throws<PerformanceContractException>(() => SecretRetainedNativePlan.Validate(
            "sqlserver",
            "ef-secret-repository",
            EvidenceFixture.Route("elsa_secrets_filtered_list"),
            retained));
    }

    [Theory]
    [InlineData("mongodb", "json", "{\"queryPlanner\":{\"winningPlan\":{\"stage\":\"IXSCAN\",\"indexName\":\"ix\"}}}")]
    [InlineData("sqlserver", "xml", "<ShowPlanXML><RelOp PhysicalOp=\"Index Seek\"><Object Index=\"[ix]\" /></RelOp></ShowPlanXML>")]
    public void Secret_retained_structured_plans_keep_json_and_xml_safety_validation(
        string provider,
        string extension,
        string providerPlan)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa646-secret-structured-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"plan.{extension}");
        try
        {
            File.WriteAllText(path, SecretRetainedNativePlan.Create(provider, 68, 16, 16, providerPlan));

            ArtifactStore.ValidateRawPlanFile(path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
            Func<NativeRouteEvidence, NativeRouteEvidence>? transform = null,
            string? providerPlan = null,
            int? retainedPhysicalCardinality = null,
            SecretProviderConcurrencyEvidence? concurrency = null)
        {
            var workload = WorkloadCatalog.Load(Repository.Root()).Workloads[SecretCreateReadListWorkload.WorkloadId];
            var directory = Path.Combine(Path.GetTempPath(), $"elsa646-secret-native-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);

            var request = new RunRequest(
                "secret-native-cohort",
                "secret-native-set",
                workload.Id,
                workload.Version,
                "sqlite",
                "groundwork-secret-repository",
                "entity-type-specific-physical-tables",
                "small",
                new string('a', 40),
                new string('b', 64),
                new Dictionary<string, string>(),
                new string('c', 64),
                new string('d', 64),
                "3.46.0",
                "file-backed-distinct-connections",
                new Dictionary<string, string> { ["journal_mode"] = "wal" },
                workload.Input.Seed,
                workload.Input.FingerprintSha256,
                "secret-native-plan",
                "secret-native-plan.json",
                new string('e', 64),
                ProcessKind.Measured,
                1);

            var rawPlan = RetainedPlan(
                providerPlan,
                retainedPhysicalCardinality ?? SecretCreateReadListWorkload.CanonicalSecretCount +
                SecretCreateReadListWorkload.NoiseSecretCount + 1);
            var route = (transform?.Invoke(CreateRoute()) ?? CreateRoute()) with
            {
                RawPlanSha256 = Hash(rawPlan)
            };
            concurrency ??= Concurrency();
            File.WriteAllText(Path.Combine(directory, route.RawPlanReference), rawPlan);
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
                [route],
                "provider-native-routes")
            {
                ProviderConcurrency = concurrency
            };
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
                        [route])
                    {
                        ProviderConcurrency = concurrency
                    }),
                directory);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }

        private static NativeRouteEvidence CreateRoute() => new(
            "list-filtered",
            "list-filtered.raw.txt",
            Hash(RetainedPlan()),
            "index-search",
            "elsa_secrets_filtered_list",
            SecretCreateReadListWorkload.CanonicalSecretCount + SecretCreateReadListWorkload.NoiseSecretCount + 1,
            true,
            true,
            SecretCreateReadListWorkload.PageSize,
            SecretCreateReadListWorkload.PageSize);

        internal static SecretProviderConcurrencyEvidence Concurrency() => new(
            SecretCreateReadListWorkload.ConcurrentContenders,
            SecretCreateReadListWorkload.ConcurrentContenders,
            SecretCreateReadListWorkload.ConcurrentContenders,
            ProviderCommandOverlapObserved: false,
            ProviderCommandsSerializedByDesign: true,
            EveryContenderIssuedProviderCommands: true,
            DistinctPhysicalConnectionCount: 1);

        internal static NativeRouteEvidence Route(string indexName) => CreateRoute() with
        {
            IndexName = indexName
        };

        private static string RetainedPlan(string? providerPlan = null, int physicalCardinality = 68) => SecretRetainedNativePlan.Create(
            "sqlite",
            physicalCardinality,
            SecretCreateReadListWorkload.PageSize,
            SecretCreateReadListWorkload.PageSize,
            providerPlan ?? "2\t0\tSEARCH elsa_secrets USING INDEX elsa_secrets_filtered_list (__groundwork_scope=? AND tenantId=? AND status=?)");

        private static string Hash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
