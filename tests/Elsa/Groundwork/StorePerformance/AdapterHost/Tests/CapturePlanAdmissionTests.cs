using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class CapturePlanAdmissionTests
{
    [Theory]
    [InlineData("password", "super-secret")]
    [InlineData("server", "db.example.test")]
    public void Capture_plan_rejects_sensitive_request_fields_before_provider_or_output_access(
        string key,
        string value)
    {
        var output = Path.Combine(Path.GetTempPath(), $"capture-plan-rejected-{Guid.NewGuid():N}");
        var request = Request() with
        {
            ProviderConfiguration = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [key] = value
            }
        };

        try
        {
            Assert.Throws<PerformanceContractException>(() => CapturePlanAdmission.Ensure(request));
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void Capture_plan_rejects_malformed_request_at_the_admission_boundary()
    {
        var request = Request() with { NativePlanEvidenceReference = "../escape.json" };

        var exception = Assert.Throws<PerformanceContractException>(() => CapturePlanAdmission.Ensure(request));

        Assert.Contains("actual frozen input", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_plan_binds_evidence_reference_to_the_measurement_set_before_provider_access()
    {
        var request = Request() with
        {
            NativePlanEvidenceReference = "checkpoint-commit.sqlite.other-set.native-plan.json"
        };

        var exception = Assert.Throws<PerformanceContractException>(() => CapturePlanAdmission.Ensure(request));

        Assert.Contains("checkpoint-commit.sqlite.set.native-plan.json", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sqlserver", "real-sqlserver-container")]
    [InlineData("postgresql", "real-postgresql-container")]
    [InlineData("mongodb", "transaction-capable-replica-set")]
    public void Capture_plan_rejects_non_sqlite_ef_secret_before_provider_or_artifact_access(
        string provider,
        string topology)
    {
        var output = Path.Combine(Path.GetTempPath(), $"capture-plan-secret-rejected-{Guid.NewGuid():N}");

        try
        {
            var exception = Assert.Throws<PerformanceContractException>(() =>
                CapturePlanAdmission.Ensure(SecretRequest(provider, topology)));

            Assert.Contains(BenchmarkAdapterAdmission.SecretEfProviderRequiredReason, exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [Theory]
    [InlineData("run", "sqlserver", "real-sqlserver-container")]
    [InlineData("run", "postgresql", "real-postgresql-container")]
    [InlineData("run", "mongodb", "transaction-capable-replica-set")]
    [InlineData("verify-correctness", "sqlserver", "real-sqlserver-container")]
    [InlineData("verify-correctness", "postgresql", "real-postgresql-container")]
    [InlineData("verify-correctness", "mongodb", "transaction-capable-replica-set")]
    public void Direct_commands_reject_non_sqlite_ef_secret_before_connection_environment_resolution(
        string command,
        string provider,
        string topology)
    {
        var request = SecretRequest(provider, topology);
        var connectionEnvironmentRead = false;
        var args = new[]
        {
            command,
            "--request",
            RunRequestWire.Serialize(request),
            "--out",
            Path.Combine(Path.GetTempPath(), $"secret-direct-rejected-{Guid.NewGuid():N}")
        };

        var exception = Assert.Throws<PerformanceContractException>(() =>
            SecretRunAdmission.ParseAndResolve(
                args,
                command,
                _ =>
                {
                    connectionEnvironmentRead = true;
                    throw new InvalidOperationException("The connection environment must not be read.");
                }));

        Assert.Contains(BenchmarkAdapterAdmission.SecretEfProviderRequiredReason, exception.Message, StringComparison.Ordinal);
        Assert.False(connectionEnvironmentRead);
        Assert.False(Directory.Exists(args[^1]));
    }

    private static RunRequest Request() => new(
        ComparisonCohortId: "cohort",
        MeasurementSetId: "set",
        WorkloadId: "checkpoint-commit",
        WorkloadVersion: "1.1.0",
        Provider: "sqlite",
        ProviderVersion: "3.46.0",
        ProviderTopology: "file-backed-distinct-connections",
        ProviderConfiguration: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mode"] = "ReadWriteCreate"
        },
        Adapter: "groundwork-v2",
        PhysicalForm: "checkpoint-unit-of-work-with-linked-outbox",
        Scale: "small",
        CommitSha: new string('a', 40),
        HarnessAssemblySha256: new string('b', 64),
        CompositionFingerprint: new string('c', 64),
        HostFingerprintSha256: new string('d', 64),
        PackageVersions: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Groundwork.Store"] = "0.4.0-preview.1"
        },
        Seed: "spec094-checkpoint-commit-v1.1",
        InputFingerprintSha256: "ee4cef346ca64739bbe7cfc84ee3f74e6acefec582f537c685991ca73c62ce13",
        NativePlanIdentity: "checkpoint-commit-zero-routes",
        NativePlanEvidenceReference: "checkpoint-commit.sqlite.set.native-plan.json",
        NativePlanContentSha256: new string('e', 64),
        ProcessKind: ProcessKind.Measured,
        ProcessIndex: 0);

    private static RunRequest SecretRequest(string provider, string topology) => new(
        ComparisonCohortId: "secret-cohort",
        MeasurementSetId: "secret-set",
        WorkloadId: SecretCreateReadListWorkload.WorkloadId,
        WorkloadVersion: SecretCreateReadListWorkload.Version,
        Provider: provider,
        ProviderVersion: "1.0.0",
        ProviderTopology: topology,
        ProviderConfiguration: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mode"] = "candidate"
        },
        Adapter: BenchmarkAdapterRegistry.EfSecretRepositoryAdapterId,
        PhysicalForm: EfSecretRepositoryAdapter.PhysicalForm,
        Scale: "small",
        CommitSha: new string('a', 40),
        HarnessAssemblySha256: new string('b', 64),
        CompositionFingerprint: new string('c', 64),
        HostFingerprintSha256: new string('d', 64),
        PackageVersions: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Groundwork.Store"] = "0.4.0-preview.3"
        },
        Seed: SecretCreateReadListWorkload.Seed,
        InputFingerprintSha256: SecretCreateReadListWorkload.ExpectedInputFingerprint,
        NativePlanIdentity: "secret-ef-plan",
        NativePlanEvidenceReference: NativePlanEvidenceStaging.ReferenceFor(
            SecretCreateReadListWorkload.WorkloadId,
            provider,
            "secret-set"),
        NativePlanContentSha256: new string('e', 64),
        ProcessKind: ProcessKind.Measured,
        ProcessIndex: 1);
}
