namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentityV2AcceptanceCatalogTests
{
    [Fact]
    public void Every_required_v1_objective_has_an_exact_public_v2_replacement()
    {
        AspNetCoreIdentityV2AcceptanceCatalog.RequireExactCoverage(
            AspNetCoreIdentityV2AcceptanceCatalog.Replacements.Keys);

        foreach (var replacement in AspNetCoreIdentityV2AcceptanceCatalog.Replacements.Values)
        {
            Assert.StartsWith(
                "Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.",
                replacement.TestType.FullName,
                StringComparison.Ordinal);
            Assert.NotNull(replacement.TestType.GetMethod(replacement.MethodName));
        }
    }

    [Fact]
    public void Missing_and_unexpected_objectives_fail_closed()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AspNetCoreIdentityV2AcceptanceCatalog.RequireExactCoverage(
            [
                "tenancy.cross-scope-read-is-not-disclosed",
                "unexpected.claim"
            ]));

        Assert.Contains(
            "missing=[atomicity.injected-failure-does-not-leave-partial-state",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("unexpected=[unexpected.claim]", exception.Message, StringComparison.Ordinal);
    }
}
