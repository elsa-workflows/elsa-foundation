using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T017 (FR-013f) — activity-removal cascade. When an activity's <c>NodeId</c> is absent from
/// the desired state, the caller also drops the connections touching it; the diff emits
/// <c>OnActivityRemovedFromDraft</c> for the node plus the connection removals — independent
/// dimensions, each diffed on its own.
/// </summary>
public sealed class ActivityRemovalCascadeTests
{
    [Fact]
    public async Task Removing_an_activity_with_its_connections_emits_removal_plus_connection_removals()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);
        await Update(host, draftId, State(
            activities: [Node("a"), Node("b")],
            connections: [Connection("a", "b")]));

        // Remove activity b and the connection a→b it participated in.
        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(
            activities: [Node("a")],
            connections: []));

        var diff = DiffEventsSince(host, skip);

        Assert.Single(diff.OfType<OnActivityRemovedFromDraft>(), e => e.NodeId == "b");
        Assert.Single(diff.OfType<OnConnectionRemovedFromDraft>(),
            e => e.Connection.Source.ActivityNodeId == "a" && e.Connection.Target.ActivityNodeId == "b");
    }
}
