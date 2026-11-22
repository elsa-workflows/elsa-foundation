using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Tests;

/// <summary>
/// #3441 self-review round 1 (S9): the owner-scoped session list reports what a returning owner can still act on, read
/// from the store's plain columns without hydrating anything. Compiled into the durable EF suite too, so both stores
/// answer the same.
/// </summary>
public sealed class WeaverSessionSummaryTests
{
    [Fact]
    public async Task The_owner_list_counts_only_reviews_that_can_still_be_decided_and_reports_an_expired_session_closed()
    {
        using var harness = new WeaverRestartHarness();
        var session = await harness.Host.CreateAsync();
        Assert.NotNull((await harness.Host.Invoker.DispatchAsync(WeaverRecoveryRestartTests.Call(session), session.Policy)).Proposal);
        Assert.Equal((WeaverSessionStatus.Active, 1), Shape(await SummaryAsync(harness, session)));

        // Past the prepared deadline the review can no longer be decided, although nothing hydrated the session yet.
        harness.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal((WeaverSessionStatus.Active, 0), Shape(await SummaryAsync(harness, session)));

        // Past the session's expiry it is listed closed; hydrating it would close it.
        harness.Clock.Advance(TimeSpan.FromHours(8));
        Assert.Equal((WeaverSessionStatus.Expired, 0), Shape(await SummaryAsync(harness, session)));
    }

    [Fact]
    public async Task A_reached_session_limit_is_recorded_and_listed_across_a_restart()
    {
        using var harness = new WeaverRestartHarness();
        var session = await harness.Host.CreateAsync();
        Assert.Null((await SummaryAsync(harness, session)).RecoveryState);

        await harness.Host.Owner.RecordLimitAsync(session.Id, "bytes");
        var host = harness.Restart();

        Assert.Equal("limit-reached:bytes", (await SummaryAsync(harness, session)).RecoveryState);
        Assert.Equal("bytes", (await host.Sessions.FindAsync(session.Id))!.LimitReached);
    }

    internal static async Task<WeaverSessionSummary> SummaryAsync(WeaverRestartHarness harness, WeaverSession session)
        => Assert.Single((await harness.Store.ListOwnedAsync(new(session.ActorId, session.TenantId, null, 20, null))).Items, x => x.Id == session.Id);

    private static (WeaverSessionStatus Status, int Pending) Shape(WeaverSessionSummary summary) => (summary.Status, summary.PendingReviewCount);
}
