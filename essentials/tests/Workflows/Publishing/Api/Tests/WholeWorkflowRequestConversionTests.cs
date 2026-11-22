using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using RuntimeInputContract = Elsa.Activities.Runtime.Core.Models.ActivityInputContract;
using ActivityValuePolicy = Elsa.Activities.Runtime.Core.Models.ActivityValuePolicy;
using WorkflowExecutableHasher = Elsa.Workflows.Runtime.Services.Executables.WorkflowExecutableHasher;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class WholeWorkflowRequestConversionTests
{
    public static IEnumerable<object[]> RefusedCases =>
        WholeVariableConversionTests.ConversionIntents.Select(intent => new object[] { "single", false, intent })
            .Concat(from entry in new[] { "catalog", "pinned", "boundary" }
                    from intent in new[] { "Json", "Options" } select new object[] { entry, false, intent })
            .Append(new object[] { "single", true, "Json" });

    public static IEnumerable<object[]> OrdinaryCases =>
        (from entry in new[] { "single", "catalog", "pinned", "boundary" }
         from request in WholeVariableConversionTests.DefaultRequests select new object[] { entry, false, request })
            .Concat(WholeVariableConversionTests.DefaultRequests.Select(request => new object[] { "single", true, request }));

    [Theory]
    [MemberData(nameof(RefusedCases))]
    public void Whole_request_conversion_requires_a_pinned_source_contract(string entry, bool path, string intent)
    {
        var request = WholeVariableConversionTests.Request(intent)!;
        var exception = Assert.Throws<ValueConversionPublicationException>(() => Compile(entry, State(path, request)));

        Assert.Equal("WorkflowRequestSourceContractUnknown", exception.ReasonCode.ToString());
        Assert.Null(exception.SourceType);
        Assert.Null(exception.SourceRepresentation);
        Assert.Equal(new ValueTypeDescriptor("String"), exception.TargetType);
        Assert.Equal(new ValueConversionBindingContext("consumer", "value", ValueConversionBindingKind.Input), exception.Binding);
        Assert.Equal((ValueConversionMode)request.Mode, exception.Mode);
        Assert.Contains("customer", exception.Message, StringComparison.Ordinal);
        Assert.Contains("consumer", exception.Message, StringComparison.Ordinal);
        Assert.Contains("value", exception.Message, StringComparison.Ordinal);
        Assert.Contains("String", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("option-sentinel", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(OrdinaryCases))]
    public void Ordinary_request_Auto_retains_identity_policy_bytes_and_hash(string entry, bool path, string request)
    {
        var baseline = Compile(entry, State(path, null));
        var binding = Compile(entry, State(path, WholeVariableConversionTests.Request(request)));

        Assert.Equal(RuntimeInputBindingSource.WorkflowRequest, binding.Source);
        Assert.Equal("customer", binding.WorkflowRequest!.MemberKey);
        Assert.Equal(path ? "customerId" : null, binding.WorkflowRequest.Path);
        Assert.True(binding.EffectivePolicy.IsSensitive);
        Assert.Null(binding.ConversionRequest);
        Assert.Null(binding.ConversionPlan);
        Assert.Equal(JsonSerializer.Serialize(baseline), JsonSerializer.Serialize(binding));
        var hasher = new WorkflowExecutableHasher();
        Assert.Equal(hasher.ComputeHash(Node(baseline)), hasher.ComputeHash(Node(binding)));
        Assert.Equal(WholeValueBindingHashTests.BindingFingerprint(Node(baseline)), WholeValueBindingHashTests.BindingFingerprint(Node(binding)));
    }

    [Theory]
    [InlineData("single")]
    [InlineData("catalog")]
    [InlineData("pinned")]
    [InlineData("boundary")]
    public void Invalid_request_identity_is_reported_before_conversion(string entry)
    {
        var malformed = State(false, WholeVariableConversionTests.Request("Json")) with
        {
            Value = new ArgumentValue(JsonSerializer.SerializeToElement(new { note = "payload-sentinel" }), "WorkflowRequest")
        };
        var exception = Assert.Throws<ArgumentException>(() => Compile(entry, malformed));
        Assert.IsNotType<ValueConversionPublicationException>(exception);
        Assert.DoesNotContain("payload-sentinel", exception.Message, StringComparison.Ordinal);
    }

    internal static ArgumentState State(bool path, AuthoredValueConversionRequest? conversion, string key = "value") =>
        new(key, new ArgumentValue(JsonSerializer.SerializeToElement(new { memberKey = "customer", path = path ? "customerId" : null }), "WorkflowRequest"),
            null, null, null, true, conversion);

    private static InputDefinition Definition() => new("value", "Value", new TypeReference("String"), null, "Value", null, IsNullable: false);

    private static RuntimeInputBinding Compile(string entry, ArgumentState state)
    {
        var compiler = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create());
        return entry switch
        {
            "single" => compiler.Compile("consumer", Definition(), state),
            "catalog" => compiler.CompileAll("consumer", [Definition()], [state])["value"],
            "pinned" => compiler.CompileAll("consumer", [new RuntimeInputContract("value", "Value", new ValueTypeDescriptor("String"),
                false, false, false, null, ActivityValuePolicy.Default)], [state])["value"],
            "boundary" => compiler.CompilePublishedBoundaryInput("consumer",
                new ActivityInputContract("value", "Value", new TypeReference("String"), false, false, null, "elsa.json"), state),
            _ => throw new ArgumentOutOfRangeException(nameof(entry))
        };
    }

    private static ExecutableNode Node(RuntimeInputBinding binding) =>
        WholeValueBindingHashTests.Node(new Dictionary<string, RuntimeInputBinding> { ["value"] = binding });
}
