using System.Security.Cryptography;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class WorkloadCatalogTests
{
    [Fact]
    public void Loads_the_exact_thirteen_versioned_spec_094_workloads()
    {
        var catalog = WorkloadCatalog.Load(Repository.Root());

        Assert.Equal(13, catalog.Workloads.Count);
        Assert.Equal(Expected.Keys.Order(StringComparer.Ordinal), catalog.Workloads.Keys.Order(StringComparer.Ordinal));
        foreach (var (id, expected) in Expected)
        {
            var actual = catalog.Workloads[id];
            Assert.Equal(expected.CoverageRows, actual.CoverageRows);
            Assert.Equal(expected.PhysicalForms, actual.PhysicalFormsFor646);
            Assert.Equal(["sqlite", "sqlserver", "postgresql", "mongodb"], actual.RequiredProviders);
            Assert.Equal(["mongodb", "postgresql", "sqlite", "sqlserver"], actual.RequiredProviderEvidence.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(
                ReproducibleWorkloadScenarioCatalog.TryGetBlockedReason(id, out _) ? "blocked" : "ready",
                actual.BenchmarkAdmission.Status);
        }
    }

    [Fact]
    public void Keeps_historical_sources_separate_from_their_executable_successors()
    {
        var directory = Path.Combine(Repository.Root(), "specs", "094-harden-groundwork-stores", "workloads");

        Assert.Equal(
            "b5681de1cb1cf5fa9e671770df0cc78f026103293889d86d0c9ea63fcc4ee364",
            Hash(Path.Combine(directory, "iam-secrets.json")));
        Assert.Equal(
            "d9359af187da4f8a1568896a7ecae8e97215eb58f68d0e185d677a94833cc240",
            Hash(Path.Combine(directory, "secret-create-read-list-v1.1.json")));
        Assert.Equal(
            "1b81a63d8a2acfe5ceea9e9a7e458de21c0fae8069506be5e94258198eff7d41",
            Hash(Path.Combine(directory, "runtime.json")));
        Assert.Equal("36277c9b9c525d4cbb611c1a7e83c96a02eb3434fb85b6657ce2ede9b8a7a5e3",
            ReproducibleWorkloadScenarioCatalog.HistoricalRecoveryInputFingerprint);
        Assert.Equal("3c7cae42737a2a995968852a862f769070a016b4e4a0289c7a9a5e7205e9eabf",
            ReproducibleWorkloadScenarioCatalog.HistoricalRecoveryResultDigest);
    }

    [Fact]
    public void Rejects_recomputed_digest_drift_in_the_historical_secret_source()
    {
        using var fixture = WorkloadFixture.CopyFromRepository();
        fixture.Replace(
            "\"seed\": \"spec094-secret-create-read-list-v1\"",
            "\"seed\": \"spec094-secret-create-read-list-v1-drifted\"",
            "iam-secrets.json");

        var error = Assert.Throws<WorkloadContractException>(() =>
            WorkloadCatalog.Load(fixture.Root, SourceDigests(fixture.Root)));

        Assert.Contains("historical Secret v1.0 contract", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Contract_definitions_match_independent_literal_goldens_and_secret_has_an_executable_successor()
    {
        var catalog = WorkloadCatalog.Load(Repository.Root());

        Assert.Equal(ExpectedGoldenVectors.Keys.Order(StringComparer.Ordinal), ReproducibleWorkloadScenarioCatalog.GoldenVectors.Keys.Order(StringComparer.Ordinal));
        foreach (var (id, golden) in ExpectedGoldenVectors)
        {
            Assert.Equal(golden, ReproducibleWorkloadScenarioCatalog.GoldenVectors[id]);
            Assert.Equal(golden.InputFingerprint, catalog.Workloads[id].Input.FingerprintSha256);
            Assert.Equal(golden.ResultDigest, catalog.Workloads[id].Correctness.ResultDigestSha256);
        }

        Assert.Equal(12, ReproducibleWorkloadScenarioCatalog.Successors.Count);
        foreach (var (id, scenario) in ReproducibleWorkloadScenarioCatalog.Successors)
        {
            var workload = catalog.Workloads[id];
            var golden = ExpectedGoldenVectors[id];
            Assert.Equal(scenario.Version, workload.Version);
            Assert.Equal(scenario.Seed, workload.Input.Seed);
            Assert.Equal(scenario.OperationSequence, workload.OperationSequence);
            Assert.Equal(golden.InputFingerprint, scenario.ComputeInputFingerprint());
            Assert.Equal(golden.ResultDigest, scenario.ComputeResultDigest());
            Assert.NotEmpty(scenario.CreateExpectedObservations());
            Assert.Equal(
                ReproducibleWorkloadScenarioCatalog.TryGetBlockedReason(id, out var blockedReason)
                    ? new BenchmarkAdmission("blocked", blockedReason)
                    : new BenchmarkAdmission("ready", ReproducibleWorkloadScenarioCatalog.ReadyReasonCode),
                workload.BenchmarkAdmission);
        }

        var secret = catalog.Workloads[ReproducibleWorkloadScenarioCatalog.BlockedWorkloadId];
        Assert.Equal(SecretCreateReadListWorkload.Version, secret.Version);
        Assert.Contains(secret.Id, ReproducibleWorkloadScenarioCatalog.Successors.Keys);
        Assert.Equal(new BenchmarkAdmission("ready", ReproducibleWorkloadScenarioCatalog.ReadyReasonCode), secret.BenchmarkAdmission);
        Assert.Equal(SecretCreateReadListWorkload.HistoricalInputFingerprint, ReproducibleWorkloadScenarioCatalog.BlockedInputFingerprint);
        Assert.Equal(SecretCreateReadListWorkload.HistoricalResultDigest, ReproducibleWorkloadScenarioCatalog.BlockedResultDigest);
        Assert.Contains("real EF Secret repository comparator", ReproducibleWorkloadScenarioCatalog.BlockedReason, StringComparison.Ordinal);

        var diagnostics = catalog.Workloads[ReproducibleWorkloadScenarioCatalog.DiagnosticsWorkloadId];
        Assert.Equal(
            new BenchmarkAdmission("blocked", ReproducibleWorkloadScenarioCatalog.DiagnosticsBlockedReasonCode),
            diagnostics.BenchmarkAdmission);

        var recovery = catalog.Workloads[RuntimeRecoveryScanWorkload.WorkloadId];
        Assert.Equal("1.2.0", recovery.Version);
        Assert.Equal(
            new WorkloadLineage(
                RuntimeRecoveryScanWorkload.WorkloadId,
                "1.1.0",
                "production scanner paging is bounded to four native route candidates per page and the fixture preserves explicit terminal/live execution partitions"),
            recovery.Lineage);
    }

    [Fact]
    public void Rejects_a_workload_file_with_an_unknown_or_missing_contract_property()
    {
        using var fixture = WorkloadFixture.CopyFromRepository();
        fixture.Replace("\"handoffTarget\": \"#646\"", "\"handoffTarget\": \"#646\", \"unreviewed\": true");

        var error = Assert.Throws<WorkloadContractException>(() => WorkloadCatalog.Load(fixture.Root));

        Assert.Contains("unknown", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_any_drift_from_the_frozen_spec_094_contract()
    {
        using var fixture = WorkloadFixture.CopyFromRepository();
        fixture.Replace("\"version\": \"1.1.0\"", "\"version\": \"9.9.9\"");

        var error = Assert.Throws<WorkloadContractException>(() => WorkloadCatalog.Load(fixture.Root));

        Assert.Contains("does not match", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_semantic_input_drift_even_when_the_source_digest_is_reviewed_again()
    {
        using var fixture = WorkloadFixture.CopyFromRepository();
        fixture.Replace("\"checkpointCount\": 1024", "\"checkpointCount\": 1025");

        var error = Assert.Throws<WorkloadContractException>(() =>
            WorkloadCatalog.Load(fixture.Root, SourceDigests(fixture.Root)));

        Assert.Contains("semantic input", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_recovery_successor_lineage_drift_even_when_its_source_digest_is_reviewed_again()
    {
        using var fixture = WorkloadFixture.CopyFromRepository();
        fixture.Replace(
            "production scanner paging is bounded to four native route candidates per page and the fixture preserves explicit terminal/live execution partitions",
            "recovery successor has no predecessor",
            "recovery-scan-v1.2.json");

        var error = Assert.Throws<WorkloadContractException>(() =>
            WorkloadCatalog.Load(fixture.Root, SourceDigests(fixture.Root)));

        Assert.Contains("lineage", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("\"noiseUserCount\": 16", "\"noiseUserCount\": 17")]
    [InlineData("\"scenarioId\": \"identity-authority-baseline\"", "\"scenarioId\": \"identity-authority-drift\"")]
    [InlineData("\"create-canonical-user\"", "\"create-unreviewed-user\"")]
    public void Rejects_identity_semantic_scenario_or_operation_drift_with_a_recomputed_source_digest(
        string from,
        string to)
    {
        using var fixture = WorkloadFixture.CopyFromRepository();
        fixture.Replace(from, to, "iam-secrets.json");

        var error = Assert.Throws<WorkloadContractException>(() =>
            WorkloadCatalog.Load(fixture.Root, SourceDigests(fixture.Root)));

        Assert.Contains("Identity v1.1 golden vector", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"retentionOverflowRecords\": 1000", "\"retentionOverflowRecords\": 1001")]
    [InlineData("\"query-open-telemetry-logs\"", "\"query-open-telemetry-log-drift\"")]
    [InlineData("\"scenarioId\": \"diagnostics-durable-history\"", "\"scenarioId\": \"diagnostics-durable-history-drift\"")]
    public void Rejects_diagnostics_semantic_scenario_or_operation_drift_with_a_recomputed_source_digest(
        string from,
        string to)
    {
        using var fixture = WorkloadFixture.CopyFromRepository();
        fixture.Replace(from, to, "diagnostics-durable-history-v1.2.json");

        var error = Assert.Throws<WorkloadContractException>(() =>
            WorkloadCatalog.Load(fixture.Root, SourceDigests(fixture.Root)));

        Assert.Contains("reproducible successor contract vector", error.Message, StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> SourceDigests(string repositoryRoot)
    {
        var directory = Path.Combine(repositoryRoot, "specs", "094-harden-groundwork-stores", "workloads");
        return Directory.EnumerateFiles(directory, "*.json")
            .ToDictionary(
                path => Path.GetFileName(path),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
                StringComparer.Ordinal);
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private sealed record ExpectedWorkload(string[] CoverageRows, string[] PhysicalForms);

    private static readonly IReadOnlyDictionary<string, ExpectedWorkload> Expected =
        new Dictionary<string, ExpectedWorkload>(StringComparer.Ordinal)
        {
            ["checkpoint-commit"] = new(["runtime-activity-execution-state", "runtime-checkpoint-commit", "runtime-durable-value-state", "runtime-workflow-executable"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "checkpoint-unit-of-work-with-linked-outbox"]),
            ["bookmark-lookup"] = new(["runtime-bookmark-state"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables"]),
            ["trigger-binding-stimulus-lookup"] = new(["runtime-executable-source-reference", "runtime-trigger-binding"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "linked-executable-source-reference-index"]),
            ["recovery-scan"] = new(["runtime-execution-liveness", "runtime-incident-state", "runtime-scheduler-state", "runtime-workflow-execution-state", "runtime-workflow-hold-state"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "recovery-candidate-index"]),
            ["queue-drain"] = new(["runtime-scheduler-poison", "runtime-scheduler-work-queue"], ["dedicated-scheduler-work-documents", "dedicated-scheduler-poison-documents", "shared-documents-with-linked-index-tables"]),
            ["outbox-drain"] = new(["runtime-post-commit-outbox"], ["dedicated-post-commit-outbox-documents", "shared-documents-with-linked-index-tables", "due-order-index"]),
            ["due-timer-selection"] = new(["runtime-durable-timer"], ["dedicated-durable-timer-documents", "shared-documents-with-linked-index-tables", "due-order-index"]),
            ["recurring-schedule-selection"] = new(["runtime-publication-projection-state", "runtime-recurring-trigger-schedule"], ["dedicated-recurring-schedule-documents", "publication-projection-documents", "shared-documents-with-linked-index-tables"]),
            ["iam-normalized-lookup-update"] = new(["iam-application", "iam-claim-mapping", "iam-credential", "iam-external-identity", "iam-provider-configuration-tenant", "iam-role", "iam-tenant-membership", "iam-user"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "entity-type-specific-physical-tables-current-identity-shape"]),
            ["secret-create-read-list"] = new(["secrets-repository"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "entity-type-specific-physical-tables"]),
            ["placement-takeover"] = new(["distributed-execution-placement"], ["dedicated-placement-lease-documents", "shared-documents-with-linked-index-tables", "placement-owner-expiry-index"]),
            ["command-send-lease-ack"] = new(["distributed-command-transport"], ["dedicated-command-transport-documents", "stream-head-documents", "shared-documents-with-linked-index-tables", "visibility-order-index"]),
            ["diagnostics-durable-history"] = new(
                ["diagnostics-open-telemetry-store", "diagnostics-structured-log-store"],
                ["ordinary-groundwork-diagnostics-units", "efcore-diagnostics-relational-tables"])
        };

    private static readonly IReadOnlyDictionary<string, WorkloadGoldenVector> ExpectedGoldenVectors =
        new Dictionary<string, WorkloadGoldenVector>(StringComparer.Ordinal)
        {
            ["bookmark-lookup"] = new("d006e25e22dc8d9374d8931f03e27c6dc45c27314bfe2f819a4dd61b588062e8", "e723ae42c3fd4e970cff04d4a6e867fa40b8d6ea23b0305ab82bf80d3916d6a9"),
            ["checkpoint-commit"] = new("ee4cef346ca64739bbe7cfc84ee3f74e6acefec582f537c685991ca73c62ce13", "ebb92b59a7a331e863c813f7110272093be6a78794a9cc7a0d914103ab4c9c62"),
            ["command-send-lease-ack"] = new("a108e41c890af94ee37d610817e2c4d6339451cbfbbd0e33e0bd794d0d1af5b1", "86439fbc13d29102d02615ee98a5beb53e008e673f6523681e3ee2d926d3389f"),
            ["due-timer-selection"] = new("02cfb91f4f415fcfe8fe6cd64e7c056b88b908e068735d2ec91eb81e0ec8d5bd", "8f380d449eb3a8e88f1edbea73cf9a7ddfa7a7502cab3ac5a8fcfe3e175ffed3"),
            ["diagnostics-durable-history"] = new("33e58245aee02636756fc5e6b8cd5ac73a73e44b3b098129ad55e44eb7acbaa2", "dba49158bd952e065d2bef53a54d80d8b1f1392d52226b93710cd428f827ddc4"),
            ["iam-normalized-lookup-update"] = new("5713ce9b09b68d368d7448041cf513907a648e53df61ccfc307a91381199a8e9", "32b62d5597e8b03715d606be9de81af9a363fe05aa2c7bf6d3f3e4cd185ddbbc"),
            ["outbox-drain"] = new("bc5c6ca1113e78fe948a61de35c66a644129c79028a198d9143dc316cea7bede", "7228f024095bc2fadc0649e0841d56259f3408b55368911ea402b7d96c8b2e71"),
            ["placement-takeover"] = new("17f22a7e7896b3842ebd771e604b13e859d1b480bc5b6093ce576f14a673e985", "3ad65cc7ff9287f9c20a68ec6cd267bc78fa083fb775dda36062c185706fb4b4"),
            ["queue-drain"] = new("15f2d5f9dc8d5814a1613156b7c686e59a150a35bd7e51787a145b6d7230d5e2", "7db639fdbfddc02973a7275d7c0e8835872b62449ca160e97e8086c0ca46eba4"),
            ["recovery-scan"] = new("eb4df814e208fedf12c3f8a995430b1084fac5cf7b7e67bd0464be07d0043eef", "af331fc39ac89be97b601ba9e472fd7872b45ec5e50ccc9bba6b55de53e3aba0"),
            ["recurring-schedule-selection"] = new("384bcbf0fd72f306b63d78b71a8130c4e2e02de146cbd45d066ef581f4d78d17", "9728bad4f576c7e50c3f6210994524ffb1d77761c5258a71f27fe1cf1793cec4"),
            ["secret-create-read-list"] = new("7f64dd6942e976e2cea5ad84db1704f4b6239380136a93d99a6480f5909021ce", "394ff58bd146744fe30f4abd3a8529ab1287129787d40e188ffc0c58038e8783"),
            ["trigger-binding-stimulus-lookup"] = new("4f2515dfa9549935712019f178283f79e6ac1cc9428e810524e733cfdea4cabc", "00b6651345cdb8b6724a205b094c712d383c7a19ef87dcce6fdf026bc7dd7c8a")
        };
}
