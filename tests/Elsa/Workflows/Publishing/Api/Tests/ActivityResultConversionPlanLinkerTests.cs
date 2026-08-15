using Elsa.Workflows.Runtime.Services;
using Elsa.Workflows.Publishing.Services;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class ActivityResultConversionPlanLinkerTests
{
    [Fact]
    public void Link_pins_a_safe_widening_plan_from_the_producer_projection_contract()
    {
        var linked = new ActivityResultConversionPlanLinker(new ValueConversionPlanResolver()).Link(
            Tree(Producer("UInt32"), Consumer("Int64")));

        var binding = NodeById(linked, "consumer").InputBindings["value"];

        Assert.Equal(ValueConversionOperation.NumericWidening, binding.ConversionPlan!.Operation);
        Assert.Equal("UInt32", binding.ConversionPlan.SourceType.Alias);
        Assert.Equal(ValueRepresentation.TypedValue, binding.ConversionPlan.SourceRepresentation);
        Assert.Equal("Int64", binding.ConversionPlan.TargetType.Alias);
    }

    [Fact]
    public void Link_rejects_an_unsupported_direct_result_conversion_at_publication()
    {
        var exception = Assert.Throws<ValueConversionPublicationException>(() =>
            new ActivityResultConversionPlanLinker(new ValueConversionPlanResolver()).Link(
                Tree(Producer("Int64"), Consumer("Int32"))));

        Assert.StartsWith("VF-COER-001", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Int64", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Int32", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Link_uses_the_declared_projection_source_representation()
    {
        var linked = new ActivityResultConversionPlanLinker(new ValueConversionPlanResolver()).Link(
            Tree(Producer("String", ValueRepresentation.FormattedContent), Consumer("String")));

        var binding = NodeById(linked, "consumer").InputBindings["value"];

        Assert.Equal(ValueConversionOperation.Identity, binding.ConversionPlan!.Operation);
        Assert.Equal(ValueRepresentation.FormattedContent, binding.ConversionPlan.SourceRepresentation);
    }

    [Fact]
    public void Link_honors_authored_direct_result_conversion_request()
    {
        var linked = new ActivityResultConversionPlanLinker(new ValueConversionPlanResolver()).Link(
            Tree(
                Producer("String", ValueRepresentation.FormattedContent),
                Consumer("Elsa.Any", new RuntimeValueConversionRequest(ValueConversionMode.Json))));

        var binding = NodeById(linked, "consumer").InputBindings["value"];

        Assert.Null(binding.ConversionRequest);
        Assert.Equal(ValueConversionMode.Json, binding.ConversionPlan!.Mode);
        Assert.Equal("elsa.json", binding.ConversionPlan.Profile!.Id);
        Assert.Equal(ValueConversionOperation.Profile, binding.ConversionPlan.Operation);
    }

    [Fact]
    public void Linked_plan_fingerprint_is_part_of_the_executable_hash()
    {
        var linked = new ActivityResultConversionPlanLinker(new ValueConversionPlanResolver()).Link(
            Tree(Producer("UInt32"), Consumer("Int64")));
        var linkedBinding = NodeById(linked, "consumer").InputBindings["value"];
        var changedPlan = new ValueConversionPlanResolver().Resolve(
            Type("UInt16"),
            ValueRepresentation.TypedValue,
            Type("Int64"));
        var changed = ReplaceConsumerBinding(linked, CloneWithPlan(linkedBinding, changedPlan));
        var hasher = new WorkflowExecutableHasher();

        Assert.NotEqual(linkedBinding.ConversionPlan!.Fingerprint, changedPlan.Fingerprint);
        Assert.NotEqual(hasher.ComputeHash(linked), hasher.ComputeHash(changed));
    }

    private static ExecutableNode Tree(ExecutableNode producer, ExecutableNode consumer) => new(
        "root",
        "root",
        "test.root",
        "1",
        new RuntimeActivityDescriptor("test", "1", JsonSerializer.SerializeToElement(new { })),
        new Dictionary<string, RuntimeInputBinding>(),
        new Dictionary<string, RuntimeOutputCapture>(),
        new Dictionary<string, string>(),
        [new ExecutableChildSlot("activities", [producer, consumer])]);

    private static ExecutableNode Producer(string projectionAlias, ValueRepresentation sourceRepresentation = ValueRepresentation.TypedValue) => new(
        "producer",
        "producer",
        "test.producer",
        "1",
        new RuntimeActivityDescriptor("test", "1", JsonSerializer.SerializeToElement(new { })),
        new Dictionary<string, RuntimeInputBinding>(),
        new Dictionary<string, RuntimeOutputCapture>(),
        new Dictionary<string, string>(),
        activityContract: Contract(projectionAlias, sourceRepresentation));

    private static ExecutableNode Consumer(string targetAlias, RuntimeValueConversionRequest? conversionRequest = null) => new(
        "consumer",
        "consumer",
        "test.consumer",
        "1",
        new RuntimeActivityDescriptor("test", "1", JsonSerializer.SerializeToElement(new { })),
        new Dictionary<string, RuntimeInputBinding>
        {
            ["value"] = new(
                "value",
                Type(targetAlias),
                ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.ActivityResult,
                activityResult: new RuntimeActivityResultReference("producer", "value", "root"),
                conversionRequest: conversionRequest)
        },
        new Dictionary<string, RuntimeOutputCapture>(),
        new Dictionary<string, string>());

    private static ActivityContract Contract(string projectionAlias, ValueRepresentation sourceRepresentation)
    {
        var projectionType = Type(projectionAlias);
        return new ActivityContract(
            "test.producer",
            "1",
            "test.producer",
            JsonSerializer.SerializeToElement(new { }),
            [],
            new ActivityResultContract(
                Type("Test.ProducerResult"),
                isRequired: true,
                ActivityValuePolicy.Default,
                [new ActivityResultProjectionContract("value", "value", projectionType, true, ActivityValuePolicy.Default, sourceRepresentation)]),
            ["Done"],
            new ActivityActivationRequirement("test.producer", "test"));
    }

    private static ExecutableNode ReplaceConsumerBinding(ExecutableNode root, RuntimeInputBinding binding) => new(
        root.ExecutableNodeId,
        root.AuthoredActivityId,
        root.ActivityType,
        root.ActivityTypeVersion,
        root.Descriptor,
        root.InputBindings,
        root.OutputCaptures,
        root.Metadata,
        root.ChildSlots.Select(slot => new ExecutableChildSlot(
            slot.Name,
            slot.Activities.Select(node => StringComparer.Ordinal.Equals(node.ExecutableNodeId, "consumer")
                ? new ExecutableNode(
                    node.ExecutableNodeId,
                    node.AuthoredActivityId,
                    node.ActivityType,
                    node.ActivityTypeVersion,
                    node.Descriptor,
                    new Dictionary<string, RuntimeInputBinding> { ["value"] = binding },
                    node.OutputCaptures,
                    node.Metadata,
                    node.ChildSlots,
                    node.Structure,
                    node.ActivityContract,
                    node.IntrinsicKind,
                    node.IntrinsicVariable)
                : node).ToArray())).ToArray(),
        root.Structure,
        root.ActivityContract,
        root.IntrinsicKind,
        root.IntrinsicVariable);

    private static RuntimeInputBinding CloneWithPlan(RuntimeInputBinding binding, ValueConversionPlan plan) => new(
        binding.InputName,
        binding.TargetType,
        binding.EffectivePolicy,
        binding.Source,
        binding.Literal,
        binding.WorkflowRequest,
        binding.Variable,
        binding.ActivityResult,
        binding.Expression,
        binding.Metadata,
        plan);

    private static ExecutableNode NodeById(ExecutableNode root, string nodeId) =>
        root.ChildSlots.SelectMany(slot => slot.Activities).Single(node => node.ExecutableNodeId == nodeId);

    private static ValueTypeDescriptor Type(string alias) => new(alias);
}
