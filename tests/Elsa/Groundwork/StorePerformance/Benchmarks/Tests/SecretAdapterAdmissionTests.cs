using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class SecretAdapterAdmissionTests
{
    [Theory]
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
    [InlineData("ef-secret-repository", "entity-type-specific-physical-tables")]
    [InlineData("groundwork-secret-repository", "wrong-form")]
    [InlineData("unratified-adapter", "entity-type-specific-physical-tables")]
    public void Unratified_secret_adapter_form_is_rejected(string adapter, string physicalForm)
    {
        var error = Assert.Throws<PerformanceContractException>(() =>
            BenchmarkAdapterAdmission.RequireAdmitted(Workload(), "sqlite", adapter, physicalForm));

        Assert.Contains("secret.adapter-form.ratification-required", error.Message, StringComparison.Ordinal);
    }

    private static PerformanceWorkload Workload() =>
        WorkloadCatalog.Load(Repository.Root()).Workloads[SecretCreateReadListWorkload.WorkloadId];
}
