using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T007 (FR-013, SC-010) — migrated per-activity I/O diff coverage. A matched activity (same
/// <c>NodeId</c>) diffs its <c>Inputs</c>/<c>Outputs</c> by (<c>NodeId</c>,<c>ReferenceKey</c>):
/// add/update/remove map to the six <c>OnActivityInput*</c>/<c>OnActivityOutput*</c> events.
/// </summary>
public sealed class ActivityIoDiffTests
{
    [Fact]
    public async Task Adding_an_activity_input_emits_OnActivityInputAddedToDraft()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);
        await Update(host, draftId, State(activities: [Node("n1", inputs: [Arg("ak1", "x")])]));

        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(activities: [Node("n1", inputs: [Arg("ak1", "x"), Arg("ak2", "y")])]));

        var added = Assert.IsType<OnActivityInputAddedToDraft>(Assert.Single(DiffEventsSince(host, skip)));
        Assert.Equal("n1", added.NodeId);
        Assert.Equal("ak2", added.InputReferenceKey);
    }

    [Fact]
    public async Task Updating_an_activity_input_payload_emits_OnActivityInputUpdatedInDraft()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);
        await Update(host, draftId, State(activities: [Node("n1", inputs: [Arg("ak1", "x")])]));

        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(activities: [Node("n1", inputs: [Arg("ak1", "changed")])]));

        var updated = Assert.IsType<OnActivityInputUpdatedInDraft>(Assert.Single(DiffEventsSince(host, skip)));
        Assert.Equal("n1", updated.NodeId);
        Assert.Equal("ak1", updated.InputReferenceKey);
    }

    [Fact]
    public async Task Removing_an_activity_input_emits_OnActivityInputRemovedFromDraft()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);
        await Update(host, draftId, State(activities: [Node("n1", inputs: [Arg("ak1", "x")])]));

        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(activities: [Node("n1", inputs: [])]));

        var removed = Assert.IsType<OnActivityInputRemovedFromDraft>(Assert.Single(DiffEventsSince(host, skip)));
        Assert.Equal("n1", removed.NodeId);
    }

    [Fact]
    public async Task Output_add_update_remove_emit_the_OnActivityOutput_events()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);
        await Update(host, draftId, State(activities: [Node("n1", outputs: [Arg("ok1", "x")])]));

        // Add.
        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(activities: [Node("n1", outputs: [Arg("ok1", "x"), Arg("ok2", "y")])]));
        Assert.IsType<OnActivityOutputAddedToDraft>(Assert.Single(DiffEventsSince(host, skip)));

        // Update.
        skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(activities: [Node("n1", outputs: [Arg("ok1", "changed"), Arg("ok2", "y")])]));
        Assert.IsType<OnActivityOutputUpdatedInDraft>(Assert.Single(DiffEventsSince(host, skip)));

        // Remove.
        skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(activities: [Node("n1", outputs: [Arg("ok1", "changed")])]));
        Assert.IsType<OnActivityOutputRemovedFromDraft>(Assert.Single(DiffEventsSince(host, skip)));
    }
}
