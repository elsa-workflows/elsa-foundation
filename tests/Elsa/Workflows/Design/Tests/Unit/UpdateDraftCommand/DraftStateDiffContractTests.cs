using Elsa.Workflows.Design.Core.Events;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// Provider-neutral conversion (T070) of the authored-state diff/event objectives that lived in the
/// EF-referencing <c>ActivityDiffTests</c> and <c>MultiDimensionDiffTests</c>. The diff half drives the
/// Core <see cref="Elsa.Workflows.Design.Persistence.Core.Services.DraftStateDiffEngine"/> directly and
/// never touches a persistence provider — the persistence (state round-trip) half of those files is
/// already covered by the shared Groundwork lifecycle contract suite. Match key is <c>NodeId</c>; the
/// engine emits diff events in its deterministic dimension order.
/// </summary>
public sealed class DraftStateDiffContractTests
{
    [Fact]
    public void Adding_an_activity_emits_OnActivityAddedToDraft()
    {
        var diff = Evaluate(State(), State(activities: [Node("node-1")]));

        var added = Assert.IsType<OnActivityAddedToDraft>(Assert.Single(diff));
        Assert.Equal("node-1", added.NodeId);
    }

    [Fact]
    public void Removing_an_activity_emits_OnActivityRemovedFromDraft()
    {
        var diff = Evaluate(State(activities: [Node("node-1")]), State(activities: []));

        var removed = Assert.IsType<OnActivityRemovedFromDraft>(Assert.Single(diff));
        Assert.Equal("node-1", removed.NodeId);
    }

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
}
