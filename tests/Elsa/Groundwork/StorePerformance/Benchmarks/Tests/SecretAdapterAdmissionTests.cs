using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class SecretAdapterAdmissionTests
{
    [Theory]
    [InlineData("ef-secret-repository")]
    [InlineData("groundwork-secret-repository")]
    public void Exact_secret_v1_1_adapter_form_is_admitted(string adapter)
    {
        BenchmarkAdapterAdmission.RequireAdmitted(
            Workload(),
            "sqlite",
            adapter,
            "entity-type-specific-physical-tables");
    }

    [Theory]
    [InlineData("ef-secret-repository", "wrong-form")]
    [InlineData("groundwork-secret-repository", "wrong-form")]
    [InlineData("unratified-adapter", "entity-type-specific-physical-tables")]
    public void Unratified_secret_adapter_form_is_rejected(string adapter, string physicalForm)
    {
        var error = Assert.Throws<PerformanceContractException>(() =>
            BenchmarkAdapterAdmission.RequireAdmitted(Workload(), "sqlite", adapter, physicalForm));

        Assert.Contains("secret.adapter-form.ratification-required", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mongodb")]
    public void Ef_secret_is_rejected_for_non_sqlite_providers_at_matrix_admission(string provider)
    {
        var workload = Workload();
        var request = EfRequest(workload, provider);

        var error = Assert.Throws<PerformanceContractException>(() => MatrixPlan.Create(workload, request));

        Assert.Contains(BenchmarkAdapterAdmission.SecretEfProviderRequiredReason, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mongodb")]
    public async Task Ef_secret_is_rejected_before_matrix_child_or_artifact_access(string provider)
    {
        var workload = Workload();
        var request = EfRequest(workload, provider);
        var run = new RunRequest(
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
            request.PackageVersions,
            request.CompositionFingerprint,
            request.HostFingerprintSha256,
            request.ProviderVersion,
            request.ProviderTopology,
            request.ProviderConfiguration,
            request.Seed,
            request.InputFingerprintSha256,
            request.NativePlanIdentity,
            request.NativePlanEvidenceReference,
            request.NativePlanContentSha256,
            ProcessKind.Warmup,
            0);
        var plan = new MatrixPlan(workload, BenchmarkProtocol.Acceptance, [run]);
        var directory = Path.Combine(Path.GetTempPath(), $"secret-ef-provider-rejected-{Guid.NewGuid():N}");
        var childCalled = false;

        try
        {
            var error = await Assert.ThrowsAsync<PerformanceContractException>(() =>
                ProcessMatrixRunner.RunForTestAsync(
                    plan,
                    directory,
                    (_, output, _) =>
                    {
                        childCalled = true;
                        Directory.CreateDirectory(output);
                        File.WriteAllText(Path.Combine(output, "unexpected-artifact"), "must-not-exist");
                        return Task.FromResult(0);
                    },
                    CancellationToken.None));

            Assert.Contains(BenchmarkAdapterAdmission.SecretEfProviderRequiredReason, error.Message, StringComparison.Ordinal);
            Assert.False(childCalled);
            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static MatrixRequest EfRequest(PerformanceWorkload workload, string provider) => new(
            "secret-admission-cohort",
            "secret-admission-set",
            workload.Id,
            workload.Version,
            provider,
            "ef-secret-repository",
            "entity-type-specific-physical-tables",
            "small",
            new string('a', 40),
            new string('b', 64),
            new Dictionary<string, string> { ["Groundwork.Store"] = "0.4.0-preview.3" },
            new string('c', 64),
            new string('d', 64),
            "1.0.0",
            workload.RequiredProviderEvidence[provider],
            new Dictionary<string, string> { ["mode"] = "candidate" },
            workload.Input.Seed,
            workload.Input.FingerprintSha256,
            "secret-native-plan",
            $"secret-create-read-list.{provider}.secret-admission-set.native-plan.json",
            new string('e', 64));

    private static PerformanceWorkload Workload() =>
        WorkloadCatalog.Load(Repository.Root()).Workloads[SecretCreateReadListWorkload.WorkloadId];
}
