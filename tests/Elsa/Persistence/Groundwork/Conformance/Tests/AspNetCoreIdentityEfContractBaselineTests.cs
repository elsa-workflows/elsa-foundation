using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class AspNetCoreIdentityEfContractBaselineTests
{
    [Fact]
    public void Ef_baseline_is_metadata_only_and_does_not_claim_execution()
    {
        var baseline = typeof(AspNetCoreIdentityEfContractBaseline);

        Assert.Equal("not-executed", AspNetCoreIdentityEfContractBaseline.ExecutionStatus);
        Assert.Equal("#646", AspNetCoreIdentityEfContractBaseline.ExecutionOwner);
        Assert.DoesNotContain(baseline.GetMethods(), method => method.Name == "ExecuteAsync");
        Assert.Null(baseline.GetProperty("MutatesEfSurface"));
    }

    [Fact]
    public void Ef_contract_baseline_does_not_add_an_ef_implementation_reference_to_conformance_tests()
    {
        var referencedAssemblies = typeof(AspNetCoreIdentityEfContractBaseline)
            .Assembly
            .GetReferencedAssemblies()
            .Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore", referencedAssemblies);
    }
}
