using System.Text.Json;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T011 (FR-001a) — layout diff coverage, driving
/// <see cref="Elsa.Workflows.Design.Persistence.Core.Services.DraftStateDiffEngine"/> directly
/// (per-diff mutation-event publication retired from the mutation command). Designer-layout records
/// live on the <c>WorkflowDefinitionDraftLayout</c> sibling, NOT inside
/// <c>WorkflowDefinitionState</c> — the engine takes them as a separate argument. A new or changed
/// <c>DesignMetadataRecord</c> for a <c>NodeId</c> diffs to <c>ActivityMovedInDraft</c>.
/// </summary>
public sealed class LayoutDiffTests
{
    [Fact]
    public void A_new_layout_record_emits_ActivityMovedInDraft()
    {
        var diff = Evaluate(
            State(activities: [Node("n1")]),
            State(activities: [Node("n1")]),
            storedLayout: [],
            desiredLayout: [LayoutRecord("n1", 10, 20, 100, 50)]);

        var moved = Assert.Single(diff.OfType<ActivityMovedInDraft>());
        Assert.Equal("n1", moved.NodeId);
        Assert.Equal(10, moved.NewX);
        Assert.Equal(20, moved.NewY);
        Assert.Equal(100, moved.NewWidth);
        Assert.Equal(50, moved.NewHeight);
    }

    [Fact]
    public void A_changed_layout_record_emits_ActivityMovedInDraft()
    {
        var diff = Evaluate(
            State(activities: [Node("n1")]),
            State(activities: [Node("n1")]),
            storedLayout: [LayoutRecord("n1", 10, 20)],
            desiredLayout: [LayoutRecord("n1", 99, 88)]);

        var moved = Assert.IsType<ActivityMovedInDraft>(Assert.Single(diff.OfType<ActivityMovedInDraft>()));
        Assert.Equal(99, moved.NewX);
        Assert.Equal(88, moved.NewY);
    }

    [Fact]
    public void An_unchanged_layout_record_emits_no_move()
    {
        var diff = Evaluate(
            State(activities: [Node("n1")]),
            State(activities: [Node("n1")]),
            storedLayout: [LayoutRecord("n1", 10, 20)],
            desiredLayout: [LayoutRecord("n1", 10, 20)]);

        Assert.Empty(diff.OfType<ActivityMovedInDraft>());
    }

    [Fact]
    public void An_unchanged_record_with_an_opaque_bag_emits_no_move()
    {
        // The stored bag is rehydrated from JSON into a JsonElement backed by a *different* JsonDocument than
        // the desired side (two independent parses of identical bytes) — exactly the persistence round-trip.
        // Record equality on JsonElement is reference-based, so the diff must normalise the bag by canonical
        // JSON rather than by record equality, or an unchanged node emits a phantom move.
        var diff = Evaluate(
            State(activities: [Node("n1")]),
            State(activities: [Node("n1")]),
            storedLayout: [RecordWithBag("n1", 10, 20, """{"z":1,"a":2}""")],
            desiredLayout: [RecordWithBag("n1", 10, 20, """{"z":1,"a":2}""")]);

        Assert.Empty(diff.OfType<ActivityMovedInDraft>());
    }

    [Fact]
    public void A_changed_opaque_bag_still_emits_ActivityMovedInDraft()
    {
        // The normalisation must not blind the diff to a genuine bag change on an otherwise-unmoved node.
        var diff = Evaluate(
            State(activities: [Node("n1")]),
            State(activities: [Node("n1")]),
            storedLayout: [RecordWithBag("n1", 10, 20, """{"label":"Start"}""")],
            desiredLayout: [RecordWithBag("n1", 10, 20, """{"label":"Begin"}""")]);

        Assert.Single(diff.OfType<ActivityMovedInDraft>());
    }

    private static DesignMetadataRecord RecordWithBag(string nodeId, double x, double y, string bagJson) =>
        new(nodeId, x, y, AdditionalProperties: JsonSerializer.Deserialize<JsonElement>(bagJson));
}
