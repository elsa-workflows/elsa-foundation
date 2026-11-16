using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// A member read of an activity result is planned from the selected member's contract, never from the producer's
/// whole value or projection. A read whose member is not known gets no plan, and only the default conversion.
/// </summary>
public sealed partial class ActivityResultConversionPlanLinkerTests
{
    [Fact]
    public void A_known_member_read_is_planned_from_the_selected_member_not_the_projection()
    {
        // The projection is a String, which cannot widen to Int64; the selected member is a UInt32, which can.
        var linked = new ActivityResultConversionPlanLinker(new ValueConversionPlanResolver()).Link(
            Tree(Producer("String"), MemberConsumer("Int64", Known("UInt32"))));

        var binding = NodeById(linked, "consumer").InputBindings["value"];

        Assert.Equal(RuntimeInputBindingSource.ActivityResultMember, binding.Source);
        Assert.Equal(ValueConversionOperation.NumericWidening, binding.ConversionPlan!.Operation);
        Assert.Equal("UInt32", binding.ConversionPlan.SourceType.Alias);
        Assert.Equal("Int64", binding.ConversionPlan.TargetType.Alias);
        Assert.Equal(RuntimeReadSelectionMode.Known, binding.Selection!.Mode);
        Assert.Equal(["customer", "count"], binding.Selection.Segments);
        Assert.Equal("producer", binding.ActivityResult!.ProducerExecutableNodeId);
    }

    [Fact]
    public void A_known_member_that_cannot_convert_to_the_input_is_refused_at_publication()
    {
        // The projection itself is an Int32, which would fit; the selected member is an Int64, which would not.
        var exception = Assert.Throws<ValueConversionPublicationException>(() =>
            new ActivityResultConversionPlanLinker(new ValueConversionPlanResolver()).Link(
                Tree(Producer("Int32"), MemberConsumer("Int32", Known("Int64")))));

        Assert.StartsWith("VF-COER-001", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Int64", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_deferred_member_read_gets_no_plan()
    {
        var consumer = MemberConsumer("Int64", Deferred());
        var linked = new ActivityResultConversionPlanLinker(new ValueConversionPlanResolver()).Link(Tree(Producer("String"), consumer));

        Assert.Same(consumer.InputBindings["value"], NodeById(linked, "consumer").InputBindings["value"]);
        Assert.Null(NodeById(linked, "consumer").InputBindings["value"].ConversionPlan);
    }

    [Fact]
    public void A_variable_member_read_is_passed_through_unplanned()
    {
        var consumer = new ExecutableNode("consumer", "consumer", "test.consumer", "1",
            new Elsa.Activities.Runtime.Core.Models.RuntimeActivityDescriptor("test", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>
            {
                ["value"] = new("value", Type("Int64"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableMember, Deferred(),
                    variable: new RuntimeVariableReference("customer", "workflow"))
            },
            new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>());

        var linked = new ActivityResultConversionPlanLinker(new ValueConversionPlanResolver()).Link(Tree(Producer("String"), consumer));

        Assert.Same(consumer.InputBindings["value"], NodeById(linked, "consumer").InputBindings["value"]);
    }

    [Fact]
    public void A_deferred_member_read_with_an_explicit_conversion_is_refused_at_publication()
    {
        var exception = Assert.Throws<ValueConversionPublicationException>(() =>
            new ActivityResultConversionPlanLinker(new ValueConversionPlanResolver()).Link(
                Tree(Producer("String"), MemberConsumer("Elsa.Any", Deferred(), new RuntimeValueConversionRequest(ValueConversionMode.Json)))));

        Assert.Equal(ValueConversionRejectionReason.SelectedMemberContractUnknown, exception.ReasonCode);
        Assert.StartsWith("VF-COER-001", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_member_read_of_a_producer_missing_from_the_executable_is_refused_as_a_whole_read_is()
    {
        var exception = Assert.Throws<ValueConversionPublicationException>(() =>
            new ActivityResultConversionPlanLinker(new ValueConversionPlanResolver()).Link(
                Tree(Unrelated(), MemberConsumer("String", Deferred()))));

        Assert.Equal(ValueConversionRejectionReason.ProducerNodeMissing, exception.ReasonCode);
    }

    private static ExecutableNode Unrelated() => new(
        "unrelated",
        "unrelated",
        "test.unrelated",
        "1",
        new Elsa.Activities.Runtime.Core.Models.RuntimeActivityDescriptor("test", "1", JsonSerializer.SerializeToElement(new { })),
        new Dictionary<string, RuntimeInputBinding>(),
        new Dictionary<string, RuntimeOutputCapture>(),
        new Dictionary<string, string>());

    private static RuntimeReadSelection Known(string alias) => new(1, ["customer", "count"], RuntimeReadSelectionMode.Known, Type(alias),
        mayBeAbsent: false, mayBeNull: false, ValueRepresentation.TypedValue, "elsa.activity-value-json/1");

    private static RuntimeReadSelection Deferred() => new(1, ["customer", "count"], RuntimeReadSelectionMode.DeferredFit);

    private static ExecutableNode MemberConsumer(string targetAlias, RuntimeReadSelection selection, RuntimeValueConversionRequest? conversionRequest = null) => new(
        "consumer",
        "consumer",
        "test.consumer",
        "1",
        new Elsa.Activities.Runtime.Core.Models.RuntimeActivityDescriptor("test", "1", JsonSerializer.SerializeToElement(new { })),
        new Dictionary<string, RuntimeInputBinding>
        {
            ["value"] = new(
                "value",
                Type(targetAlias),
                ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.ActivityResultMember,
                selection,
                activityResult: new RuntimeActivityResultReference("producer", "value", producerScopeId: null),
                conversionRequest: conversionRequest)
        },
        new Dictionary<string, RuntimeOutputCapture>(),
        new Dictionary<string, string>());
}
