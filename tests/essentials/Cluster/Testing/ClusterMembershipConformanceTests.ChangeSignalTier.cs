using Xunit;

namespace Elsa.Cluster.Testing;

public abstract partial class ClusterMembershipConformanceTests
{
    /// <summary>
    /// FR-013's change token: it completes once, the next time the provider observes a fleet change. This one exercises
    /// the in-process default too (its own publish), not only a durable provider's fleet.
    /// </summary>
    [SkippableFact]
    public async Task FR013_a_report_change_fires_the_change_signal()
    {
        var member = await Fixture.StartMemberAsync(Setup("solo", Reads("1")));
        var token = member.Membership.GetChangeToken();
        Assert.False(token.HasChanged);

        member.SetReadability(Reads("1", "2"));
        await member.Membership.PublishReportAsync();

        Assert.True(token.HasChanged, "A changed report must fire the change signal.");
    }

    [SkippableFact]
    public async Task FR013_an_unchanged_publish_does_not_fire_the_change_signal()
    {
        var member = await Fixture.StartMemberAsync(Setup("solo", Reads("1")));
        await member.Membership.PublishReportAsync();
        var token = member.Membership.GetChangeToken();

        await member.Membership.PublishReportAsync();

        Assert.False(token.HasChanged, "An unchanged publish must not fire the change signal.");
    }

    [SkippableFact]
    public async Task FR013_calling_get_change_token_again_after_a_fire_returns_a_fresh_one()
    {
        var member = await Fixture.StartMemberAsync(Setup("solo", Reads("1")));
        var first = member.Membership.GetChangeToken();

        member.SetReadability(Reads("1", "2"));
        await member.Membership.PublishReportAsync();
        Assert.True(first.HasChanged);

        var second = member.Membership.GetChangeToken();
        Assert.False(second.HasChanged);

        member.SetReadability(Reads("1", "3"));
        await member.Membership.PublishReportAsync();
        Assert.True(second.HasChanged);
    }
}
