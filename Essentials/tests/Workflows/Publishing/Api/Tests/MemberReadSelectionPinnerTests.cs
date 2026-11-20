using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// The pin pass upgrades a compiled result member read to a known selection only from the analysis facts for that
/// exact read; anything else stays deferred.
/// </summary>
public sealed class MemberReadSelectionPinnerTests
{
    private static readonly string[] Segments = ["address", "street"];

    [Fact]
    public void A_typed_read_is_pinned_with_the_selected_member_facts()
    {
        var pinned = MemberReadSelectionPinner.Pin(Tree(), [Fact()]);

        var selection = Consumer(pinned).InputBindings["value"].Selection!;
        Assert.Equal(RuntimeReadSelectionMode.Known, selection.Mode);
        Assert.Equal(new ValueTypeDescriptor("String"), selection.SelectedType);
        Assert.True(selection.MayBeAbsent);
        Assert.False(selection.MayBeNull);
        Assert.Equal(ValueRepresentation.TextValue, selection.Representation);
        Assert.Equal("elsa.activity-value-json/1", selection.ProfileRevision);
        Assert.Equal(Segments, selection.Segments);
        Assert.Equal("producer", Consumer(pinned).InputBindings["value"].ActivityResult!.ProducerExecutableNodeId);
    }

    [Fact]
    public void A_read_with_more_than_one_matching_fact_stays_deferred_instead_of_failing_publication()
    {
        var tree = Tree();

        var pinned = MemberReadSelectionPinner.Pin(tree, [Fact(), Fact() with { SelectedType = new TypeReference("Int32") }]);

        Assert.Same(tree, pinned);
        Assert.Equal(RuntimeReadSelectionMode.DeferredFit, Consumer(pinned).InputBindings["value"].Selection!.Mode);
    }

    [Fact]
    public void A_read_the_analysis_could_not_type_stays_deferred()
    {
        var tree = Tree();

        Assert.Same(tree, MemberReadSelectionPinner.Pin(tree, [Fact() with { IsKnown = false, SelectedType = null }]));
        Assert.Same(tree, MemberReadSelectionPinner.Pin(tree, []));
    }

    [Theory]
    [InlineData("producer-elsewhere", "$result", "address,street", "consumer/inputs/value")]
    [InlineData("producer", "other", "address,street", "consumer/inputs/value")]
    [InlineData("producer", "$result", "address", "consumer/inputs/value")]
    [InlineData("producer", "$result", "address,street", "consumer/inputs/other")]
    public void Facts_for_another_read_never_pin_this_one(string producer, string projection, string segments, string path)
    {
        var tree = Tree();

        var pinned = MemberReadSelectionPinner.Pin(tree, [Fact() with
        {
            ProducerNodeId = producer, ProjectionKey = projection, MemberPath = [.. segments.Split(',')], Path = path
        }]);

        Assert.Same(tree, pinned);
    }

    private static ActivityDataMemberReadFacts Fact() => new("consumer/inputs/value", "producer", "$result", [.. Segments], IsKnown: true,
        new TypeReference("String"), MayBeAbsent: true, MayBeNull: false, ValueRepresentation.TextValue, "elsa.activity-value-json/1");

    private static ExecutableNode Consumer(ExecutableNode root) => root.ChildSlots.Single().Activities.Single(node => node.ExecutableNodeId == "consumer");

    private static ExecutableNode Tree() => new(
        "root", "root", "test.root", "1",
        new Elsa.Activities.Runtime.Core.Models.RuntimeActivityDescriptor("test", "1", JsonSerializer.SerializeToElement(new { })),
        new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>(),
        [new ExecutableChildSlot("activities", [Node("producer", new Dictionary<string, RuntimeInputBinding>()), Node("consumer", new Dictionary<string, RuntimeInputBinding>
        {
            ["value"] = new("value", new ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResultMember,
                new RuntimeReadSelection(1, Segments, RuntimeReadSelectionMode.DeferredFit),
                activityResult: new RuntimeActivityResultReference("producer", "$result", producerScopeId: null))
        })])]);

    private static ExecutableNode Node(string id, IReadOnlyDictionary<string, RuntimeInputBinding> bindings) => new(
        id, id, $"test.{id}", "1",
        new Elsa.Activities.Runtime.Core.Models.RuntimeActivityDescriptor("test", "1", JsonSerializer.SerializeToElement(new { })),
        bindings, new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>());
}
