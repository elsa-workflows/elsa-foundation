using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T013 (Scenario 1, SC-002, FR-013b) — a single transition spanning several dimensions (add
/// activity + update variable). The event half drives
/// <see cref="Elsa.Workflows.Design.Persistence.Core.Services.DraftStateDiffEngine"/> directly
/// (per-diff mutation-event publication retired from the mutation command) and asserts exactly two
/// events of the correct types in the differ's deterministic dimension order. The persistence half
/// is retained as a command-level round-trip proving the desired State is persisted wholesale.
/// </summary>
public sealed class MultiDimensionDiffTests
{
    [Fact]
    public void Multi_dimension_change_emits_events_in_deterministic_order()
    {
        // Stored: variable v1 and two activities a/b.
        var stored = State(
            variables: [Variable("v1", "Original")],
            activities: [Node("a"), Node("b")]);

        // Desired: v1 renamed and a new activity c added.
        var desired = State(
            variables: [Variable("v1", "Renamed")],
            activities: [Node("a"), Node("b"), Node("c")]);

        var diff = Evaluate(stored, desired);

        Assert.Collection(diff,
            e => Assert.IsType<OnVariableUpdatedInDraft>(e),
            e => Assert.IsType<OnActivityAddedToDraft>(e));
    }

    [Fact]
    public async Task Multi_dimension_change_persists_the_desired_state()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        await Update(host, draftId, State(
            variables: [Variable("v1", "Original")],
            activities: [Node("a"), Node("b")]));

        await Update(host, draftId, State(
            variables: [Variable("v1", "Renamed")],
            activities: [Node("a"), Node("b"), Node("c")]));

        using var ctx = host.CreateContext();
        var draft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == draftId);
        Assert.Contains("Renamed", draft.StateSource);
        Assert.Contains("\"c\"", draft.StateSource);
    }
}
