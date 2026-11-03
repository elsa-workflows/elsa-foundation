using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;

namespace Elsa.Agent.Tests;

/// <summary>
/// One admission of several retained outcomes: a definite refusal of any outcome wins over another outcome whose owner
/// could not decide, in either order. A turn cannot place an unavailable outcome before a refused one in one admission
/// pass (history and tool results are admitted in recorded order), so the combination rule is pinned here directly.
/// </summary>
public sealed class AgentRetainedOutcomeAdmissionTests
{
    private static readonly AgentError Unavailable = new("owned.authorization.unavailable", "The owning module could not establish current authority for this operation.", 503);
    private static readonly AgentError OtherOutage = new("owned.store.unavailable", "Owner store outage.", 503);
    private static readonly AgentError Denied = new("agent.result.disclosure_denied", "Withheld.", 403);
    private static readonly AgentError Provenance = new("agent.result.provenance_unavailable", "Unknown owner.", 409);

    [Fact]
    public void Every_outcome_admitted_admits_the_set() => Assert.Null(AgentRetainedOutcomeAdmission.Combine([null, null]));

    [Fact]
    public void An_unavailable_owner_alone_passes_through_unchanged()
        => Assert.Same(Unavailable, AgentRetainedOutcomeAdmission.Combine([null, Unavailable, null]));

    [Fact]
    public void A_later_definite_denial_wins_over_an_earlier_unavailable_owner()
        => Assert.Same(Denied, AgentRetainedOutcomeAdmission.Combine([Unavailable, Denied]));

    [Fact]
    public void A_later_provenance_refusal_wins_over_an_earlier_unavailable_owner()
        => Assert.Same(Provenance, AgentRetainedOutcomeAdmission.Combine([Unavailable, null, Provenance]));

    [Fact]
    public void An_earlier_definite_refusal_wins_and_the_first_outage_is_kept_among_outages()
    {
        Assert.Same(Denied, AgentRetainedOutcomeAdmission.Combine([Denied, Unavailable]));
        Assert.Same(Unavailable, AgentRetainedOutcomeAdmission.Combine([Unavailable, OtherOutage]));
    }
}
