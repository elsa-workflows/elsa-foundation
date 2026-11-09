using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;

namespace Elsa.Agent.Tests;

/// <summary>
/// One admission of several retained outcomes: a definite refusal of any outcome wins over another outcome whose owner
/// could not decide, in either order. Current engine flows cannot put an owner outage before a definite refusal in one
/// admission call. Before an admission, history admission re-checks every recorded tool message in order, with the same
/// definite rules (provenance, current authority, host policy). So a definite refusal of any recorded result is returned
/// there first. The only unrecorded result, a paused continuation's own outcome, is always the last entry, and a turn's
/// single mutation means no earlier lifecycle outcome precedes it there. The end-to-end consequence is pinned in
/// AgentLifecycleOutcomeDisclosureTests; the rule itself is pinned here for any future flow that admits them together.
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
