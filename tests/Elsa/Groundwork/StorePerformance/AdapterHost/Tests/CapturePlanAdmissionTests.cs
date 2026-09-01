using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
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
        NativePlanEvidenceReference: "checkpoint-commit.sqlite.native-plan.json",
        NativePlanContentSha256: new string('e', 64),
        ProcessKind: ProcessKind.Measured,
        ProcessIndex: 0);
}
