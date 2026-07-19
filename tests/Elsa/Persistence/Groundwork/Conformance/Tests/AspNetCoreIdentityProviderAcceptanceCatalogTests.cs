using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class AspNetCoreIdentityProviderAcceptanceCatalogTests
{
    [Fact]
    public void Exact_coverage_reports_missing_and_unexpected_objectives()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AspNetCoreIdentityProviderAcceptanceCatalog.RequireExactCoverage(
            [
                "tenancy.cross-scope-read-is-not-disclosed",
                "unexpected.claim"
            ]));

        Assert.Contains(
            "missing=[atomicity.injected-failure-does-not-leave-partial-state",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "unexpected=[unexpected.claim]",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Advertised_capability_coverage_reports_missing_and_unsupported_interfaces()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AspNetCoreIdentityProviderAcceptanceCatalog.RequireExactAdvertisedFrameworkCapabilities(
            [
                "user-store",
                "queryable-user-store"
            ]));

        Assert.Contains(
            "missing=[role-claim-store",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "unexpected=[queryable-user-store]",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Atomicity_failure_window_and_receipt_cleanup_objectives_are_required()
    {
        Assert.Subset(
            AspNetCoreIdentityProviderAcceptanceCatalog.RequiredObjectiveIds.ToHashSet(StringComparer.Ordinal),
            new HashSet<string>(
            [
                "atomicity.injected-failure-does-not-leave-partial-state",
                "concurrency.external-login-owner-has-one-winner",
                "failure-window.lost-acknowledgement-reconciles-committed-result",
                "lifecycle.expired-mutation-receipt-is-cleaned"
            ],
            StringComparer.Ordinal));
        Assert.Empty(AspNetCoreIdentityProviderAcceptanceCatalog.DeferredObjectiveIds);
    }
}
