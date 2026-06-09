using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T016 (FR-013e) — a connection carries no id and has no update event; its identity IS its
/// endpoint tuple. Any endpoint change therefore diffs as remove(old) + add(new).
/// </summary>
public sealed class ConnectionChangeDiffTests
{
    [Fact]
    public async Task Changing_a_connection_endpoint_diffs_as_remove_plus_add()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);
        await Update(host, draftId, State(
            activities: [Node("a"), Node("b"), Node("c")],
            connections: [Connection("a", "b")]));

        // Retarget a→b to a→c. No update event exists, so this is remove(a→b) + add(a→c).
        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(
            activities: [Node("a"), Node("b"), Node("c")],
            connections: [Connection("a", "c")]));

        var diff = DiffEventsSince(host, skip);

        var removed = Assert.Single(diff.OfType<OnConnectionRemovedFromDraft>());
        Assert.Equal("b", removed.Connection.Target.ActivityNodeId);

        var added = Assert.Single(diff.OfType<OnConnectionAddedToDraft>());
        Assert.Equal("c", added.Connection.Target.ActivityNodeId);
    }
}
