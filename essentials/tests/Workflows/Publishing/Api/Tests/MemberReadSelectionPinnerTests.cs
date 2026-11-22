using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
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

    [Fact]
    public void Occurrence_pinning_uses_explicit_identity_and_never_enters_nested_children()
    {
        var original = Tree();
        string[] segments = ["address", "a.b", ""];
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline,
            isSensitive: true, requiresEncryption: true, redactionMode: "Full", retentionPolicy: "P7D",
            metadata: new Dictionary<string, string> { ["policy.owner"] = "kept" });
        var binding = new RuntimeInputBinding("value", new ValueTypeDescriptor("String", CollectionKind.List), policy,
            RuntimeInputBindingSource.ActivityResultMember, new RuntimeReadSelection(1, segments, RuntimeReadSelectionMode.DeferredFit),
            activityResult: new RuntimeActivityResultReference("producer", "$result", null, isOptional: true),
            metadata: new Dictionary<string, string> { ["input.owner"] = "kept" });
        var root = new ExecutableNode("placed-root", "dependency-authored-id", "test", "1", original.Descriptor,
            new Dictionary<string, RuntimeInputBinding>
            {
                ["value"] = binding,
                ["scope"] = new("scope", new ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.VariableRead, variable: new RuntimeVariableReference("local", PlacedReferenceRemapper.LegacyGraphScopeId))
            }, original.OutputCaptures, original.Metadata, childSlots: original.ChildSlots);
        ArgumentState[] arguments = [new("value", new ArgumentValue(JsonSerializer.SerializeToElement(new
        {
            producerNodeId = "producer", projectionKey = "$result", isOptional = true, memberPath = segments
        }), "ActivityResultMember"), null, null, null, null)];
        // These are the effective flags already composed by the shape owner from optional/nullable prefixes.
        var fact = new ActivityDataMemberReadFacts("body-reader/inputs/value", "producer", "$result", [.. segments], true,
            new TypeReference("String", CollectionKind.List), true, true, ValueRepresentation.StructuredValue, "elsa.activity-value-json/1");
        var originalBytes = JsonSerializer.Serialize(new { root, arguments, fact });
        var reads = MemberReadSelectionPinner.CaptureOccurrence("body-reader", arguments, [fact]);
        var capturedBytes = JsonSerializer.Serialize(reads);
        var pinned = MemberReadSelectionPinner.PinOccurrence(root, "body-reader", reads);
        var remapped = PlacedReferenceRemapper.Remap(pinned,
            new Dictionary<string, string> { ["producer"] = "placed-producer" }, "placed-graph-scope");
        var selected = remapped.InputBindings["value"];
        Assert.Equal("dependency-authored-id", pinned.AuthoredActivityId);
        Assert.Equal("dependency-authored-id", remapped.AuthoredActivityId);
        Assert.Equal(RuntimeReadSelectionMode.Known, selected.Selection!.Mode);
        Assert.Equal(new ValueTypeDescriptor("String", CollectionKind.List), selected.Selection.SelectedType);
        Assert.True(selected.Selection.MayBeAbsent);
        Assert.True(selected.Selection.MayBeNull);
        Assert.Equal(ValueRepresentation.StructuredValue, selected.Selection.Representation);
        Assert.Equal("elsa.activity-value-json/1", selected.Selection.ProfileRevision);
        Assert.Equal(segments, selected.Selection.Segments);
        Assert.Equal("placed-producer", selected.ActivityResult!.ProducerExecutableNodeId);
        Assert.True(selected.ActivityResult.IsOptional);
        Assert.Null(selected.ActivityResult.ProducerScopeId); // A body read stays scope-free; its own graph frame resolves it.
        Assert.Equal("placed-graph-scope", remapped.InputBindings["scope"].Variable!.DeclaringScopeId);
        Assert.Same(policy, selected.EffectivePolicy);
        Assert.Equal(DurableValueLifecycle.Instance, selected.EffectivePolicy.Lifecycle);
        Assert.Equal(DurableValueStorage.Inline, selected.EffectivePolicy.Storage);
        Assert.True(selected.EffectivePolicy.IsSensitive);
        Assert.True(selected.EffectivePolicy.RequiresEncryption);
        Assert.Equal("Full", selected.EffectivePolicy.RedactionMode);
        Assert.Equal("P7D", selected.EffectivePolicy.RetentionPolicy);
        Assert.Equal("kept", selected.EffectivePolicy.Metadata["policy.owner"]);
        Assert.Equal("kept", selected.Metadata["input.owner"]);
        Assert.Same(pinned.InputBindings["value"].Selection, selected.Selection);
        Assert.Same(root.ChildSlots.Single(), pinned.ChildSlots.Single());
        Assert.Same(root.ChildSlots.Single(), remapped.ChildSlots.Single());
        Assert.Equal(RuntimeReadSelectionMode.DeferredFit, Consumer(pinned).InputBindings["value"].Selection!.Mode);
        Assert.Equal(RuntimeReadSelectionMode.DeferredFit, root.InputBindings["value"].Selection!.Mode);
        Assert.Equal("producer", root.InputBindings["value"].ActivityResult!.ProducerExecutableNodeId);
        Assert.Equal(originalBytes, JsonSerializer.Serialize(new { root, arguments, fact }));
        Assert.Equal(capturedBytes, JsonSerializer.Serialize(reads));
    }

    [Theory]
    [InlineData("producer", "$result", false, "address,street", "missing")]
    [InlineData("elsewhere", "$result", false, "address,street", "value")]
    [InlineData("producer", "other", false, "address,street", "value")]
    [InlineData("producer", "$result", true, "address,street", "value")]
    [InlineData("producer", "$result", false, "address", "value")]
    public void A_pinned_occurrence_contract_mismatch_is_refused_without_fallback(string producer, string projection, bool optional, string segments, string key)
    {
        var read = new ExecutableActivityTemplateMemberRead(new(producer, projection, null, optional),
            new(1, segments.Split(','), RuntimeReadSelectionMode.Known, new("String"), false, false,
                ValueRepresentation.TextValue, "elsa.activity-value-json/1"));
        var error = Assert.Throws<ActivityDataSelectionRejectedException>(() => MemberReadSelectionPinner.PinOccurrence(Consumer(Tree()), "occurrence",
            new Dictionary<string, ExecutableActivityTemplateMemberRead> { [key] = read }));
        Assert.Equal($"occurrence/inputs/{key}", Assert.Single(error.Findings).Path);
        Assert.Equal("ActivityDataSelection/MemberContractMismatch", error.Findings[0].Type);
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
