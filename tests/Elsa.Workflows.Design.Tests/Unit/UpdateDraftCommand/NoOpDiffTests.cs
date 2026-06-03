using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T012 (SC-009, FR-013a) — no-op submission. When the desired state equals the stored state,
/// the diff yields zero mutation events, yet the validation pair still runs (the gate is
/// unconditional) and the persisted State is semantically unchanged.
/// </summary>
public sealed class NoOpDiffTests
{
    [Fact]
    public async Task Identical_desired_state_yields_no_mutation_events_but_still_validates()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        // Establish a non-trivial stored state.
        var state = State(
            variables: [Variable("v1", "MyVar")],
            activities: [Node("n1")]);
        await Update(host, draftId, state);

        // Re-submit the identical state.
        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, state);

        // Zero per-diff mutation events.
        Assert.Empty(DiffEventsSince(host, skip));

        // The validation gate still ran for the no-op pass.
        Assert.NotNull(host.EventPublisher.CapturedEvents.Skip(skip).OfType<OnDraftValidating>().LastOrDefault());
        Assert.NotNull(host.EventPublisher.CapturedEvents.Skip(skip).OfType<OnDraftValidated>().LastOrDefault());
    }
}
