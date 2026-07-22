using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T012 (SC-009, FR-013a) — no-op submission. The event half drives
/// <see cref="Elsa.Workflows.Design.Persistence.Core.Services.DraftStateDiffEngine"/> directly:
/// when the desired state equals the stored state, the diff yields zero mutation events. The
/// command half proves the validation gate still runs (it is unconditional) on a no-op re-submit.
/// </summary>
public sealed class NoOpDiffTests
{
    [Fact]
    public void Identical_desired_state_yields_no_mutation_events()
    {
        var state = State(
            variables: [Variable("v1", "MyVar")],
            activities: [Node("n1")]);

        Assert.Empty(Evaluate(state, state));
    }

    [Fact]
    public async Task No_op_resubmission_still_runs_the_validation_gate()
    {
        using var host = await WorkflowsDesignTestHost.CreateAsync();
        var draftId = await SeedEmptyDraft(host);

        // Establish a non-trivial stored state.
        var state = State(
            variables: [Variable("v1", "MyVar")],
            activities: [Node("n1")]);
        await Update(host, draftId, state);

        // Re-submit the identical state.
        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, state);

        // The validation gate still ran for the no-op pass.
        Assert.NotNull(host.EventPublisher.CapturedEvents.Skip(skip).OfType<OnDraftValidating>().LastOrDefault());
        Assert.NotNull(host.EventPublisher.CapturedEvents.Skip(skip).OfType<OnDraftValidated>().LastOrDefault());
    }
}
