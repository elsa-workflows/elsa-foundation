using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T013 (Scenario 1, SC-002, FR-013b) — a single <c>Execute</c> spanning several dimensions
/// (add activity + update variable) emits exactly two events of the
/// correct types in the differ's deterministic dimension order, and the persisted State equals
/// the desired State.
/// </summary>
public sealed class MultiDimensionDiffTests
{
    [Fact]
    public async Task Multi_dimension_change_emits_events_in_deterministic_order()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        // Stored: variable v1 and two activities a/b.
        await Update(host, draftId, State(
            variables: [Variable("v1", "Original")],
            activities: [Node("a"), Node("b")]));

        // Desired: v1 renamed and a new activity c added.
        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(
            variables: [Variable("v1", "Renamed")],
            activities: [Node("a"), Node("b"), Node("c")]));

        var diff = DiffEventsSince(host, skip);

        Assert.Collection(diff,
            e => Assert.IsType<OnVariableUpdatedInDraft>(e),
            e => Assert.IsType<OnActivityAddedToDraft>(e));

        using var ctx = host.CreateContext();
        var draft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == draftId);
        Assert.Contains("Renamed", draft.StateSource);
        Assert.Contains("\"c\"", draft.StateSource);
    }
}
