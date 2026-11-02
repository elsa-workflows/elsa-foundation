using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Tests;

/// <summary>
/// #3441 self-review round 1 (S9): the owner-scoped session list reports what a returning owner can still act on, read
/// from the store's plain columns without hydrating anything. Compiled into the durable EF suite too, so both stores
/// answer the same.
/// </summary>
public sealed class AgentSessionSummaryTests
{
    [Fact]
    public async Task The_owner_list_counts_only_reviews_that_can_still_be_decided_and_reports_an_expired_session_closed()
    {
        using var harness = new AgentRestartHarness();
        var session = await harness.Host.CreateAsync();
        Assert.NotNull((await harness.Host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal);
        Assert.Equal((AgentSessionStatus.Active, 1), Shape(await SummaryAsync(harness, session)));

        // Past the prepared deadline the review can no longer be decided, although nothing hydrated the session yet.
        harness.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal((AgentSessionStatus.Active, 0), Shape(await SummaryAsync(harness, session)));

        // Past the session's expiry it is listed closed; hydrating it would close it.
        harness.Clock.Advance(TimeSpan.FromHours(8));
        Assert.Equal((AgentSessionStatus.Expired, 0), Shape(await SummaryAsync(harness, session)));
    }

    internal static async Task<AgentSessionSummary> SummaryAsync(AgentRestartHarness harness, AgentSession session)
        => Assert.Single((await harness.Store.ListOwnedAsync(new(session.ActorId, session.TenantId, null, 20, null))).Items, x => x.Id == session.Id);

    private static (AgentSessionStatus Status, int Pending) Shape(AgentSessionSummary summary) => (summary.Status, summary.PendingReviewCount);
}
