using Elsa.Cluster.Testing;
using Elsa.Cluster.Tests.Reference;
using Xunit.Sdk;

namespace Elsa.Cluster.Tests;

/// <summary>
/// Each deliberately broken provider of FR-060 fails a named conformance test. A broken provider that passed would
/// mean the suite cannot tell it from a correct one.
/// </summary>
public sealed class BrokenProviderBiteTests
{
    [Fact]
    public Task A_provider_that_expires_members_early_fails_FR048() =>
        AssertFailsAsync(SharedStoreFault.EarlyExpiry, suite => suite.FR048_no_member_is_judged_expired_before_its_expiry_and_skew_have_passed_and_every_member_is_after());

    [Fact]
    public Task A_provider_that_expires_members_early_fails_FR049_when_the_member_clock_runs_behind() =>
        AssertFailsAsync(SharedStoreFault.EarlyExpiry, suite => suite.FR049_a_member_concludes_it_lapsed_no_later_than_another_member_judges_it_expired(-1));

    [Fact]
    public Task A_provider_that_truncates_the_fleet_view_fails_FR046() =>
        AssertFailsAsync(SharedStoreFault.Truncate, suite => suite.FR046_members_that_share_a_store_see_each_other());

    [Fact]
    public Task A_provider_that_truncates_the_fleet_view_fails_FR052() =>
        AssertFailsAsync(SharedStoreFault.Truncate, suite => suite.FR052_a_fleet_view_is_never_partial());

    [Fact]
    public Task A_provider_whose_publish_returns_before_it_is_visible_fails_FR047() =>
        AssertFailsAsync(SharedStoreFault.DeferPublish, suite => suite.FR047_a_publish_is_visible_to_the_next_fresh_read_on_another_member());

    [Fact]
    public Task A_provider_registered_beside_another_by_last_write_wins_fails_FR056() =>
        AssertFailsAsync(SharedStoreFault.LastWriteWins, suite => suite.FR056_two_opt_in_providers_in_one_host_fail_at_startup_naming_both());

    private static async Task AssertFailsAsync(SharedStoreFault fault, Func<ClusterMembershipConformanceTests, Task> test)
    {
        await using var suite = new Suite(fault);
        await Assert.ThrowsAnyAsync<XunitException>(() => test(suite));
    }

    /// <summary>Not public, so the test runner never runs a broken provider's suite as tests of its own.</summary>
    private sealed class Suite(SharedStoreFault fault) : ClusterMembershipConformanceTests(new SharedStoreConformanceFixture(fault));
}
