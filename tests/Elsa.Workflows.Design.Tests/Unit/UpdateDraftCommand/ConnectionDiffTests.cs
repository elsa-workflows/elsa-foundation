using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T008 (FR-013, SC-010) — migrated connection diff coverage. Connections match by their
/// endpoint tuple (value equality, no id): a new tuple → <c>OnConnectionAddedToDraft</c>; a
/// tuple absent from desired → <c>OnConnectionRemovedFromDraft</c>.
/// </summary>
public sealed class ConnectionDiffTests
{
    [Fact]
    public async Task Adding_a_connection_emits_OnConnectionAddedToDraft()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);
        await Update(host, draftId, State(activities: [Node("a"), Node("b")]));

        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(
            activities: [Node("a"), Node("b")],
            connections: [Connection("a", "b")]));

        var added = Assert.IsType<OnConnectionAddedToDraft>(Assert.Single(DiffEventsSince(host, skip)));
        Assert.Equal("a", added.Connection.Source.ActivityNodeId);
        Assert.Equal("b", added.Connection.Target.ActivityNodeId);
    }

    [Fact]
    public async Task Removing_a_connection_emits_OnConnectionRemovedFromDraft()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);
        await Update(host, draftId, State(
            activities: [Node("a"), Node("b")],
            connections: [Connection("a", "b")]));

        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(activities: [Node("a"), Node("b")], connections: []));

        var removed = Assert.IsType<OnConnectionRemovedFromDraft>(Assert.Single(DiffEventsSince(host, skip)));
        Assert.Equal("a", removed.Connection.Source.ActivityNodeId);
        Assert.Equal("b", removed.Connection.Target.ActivityNodeId);
    }
}
