using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Exceptions;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using RuntimeInputContract = Elsa.Activities.Runtime.Core.Models.ActivityInputContract;
using ActivityValuePolicy = Elsa.Activities.Runtime.Core.Models.ActivityValuePolicy;
using WorkflowExecutableHasher = Elsa.Workflows.Runtime.Services.Executables.WorkflowExecutableHasher;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class WholeVariableConversionTests
{
    private static readonly string[] EntryPoints = ["single", "catalog", "pinned", "boundary"];
    private static readonly string[] ReferenceForms = ["bare", "implicit-workflow", "workflow", "scope"];
    internal static readonly string[] ConversionIntents =
        ["None", "Json", "Xml", "Profile", "AutoProfile", "Depth", "Items", "Bytes", "Options", "ArrayOptions", "UnknownMode"];
    internal static readonly string[] DefaultRequests = ["omitted", "Auto", "empty-limits", "default-limits", "empty-options", "NullOptions", "all-defaults"];

    public static IEnumerable<object[]> RefusedCases =>
        from entry in EntryPoints from form in ReferenceForms from intent in ConversionIntents select new object[] { entry, form, intent };

    public static IEnumerable<object[]> AcceptedCases =>
        from entry in EntryPoints from form in ReferenceForms from request in DefaultRequests select new object[] { entry, form, request };

    [Theory]
    [MemberData(nameof(RefusedCases))]
    public void Meaningful_conversion_intent_is_refused_without_inventing_a_source_contract(string entry, string form, string intent)
    {
        var request = Request(intent)!;
        var refusal = Assert.Throws<ValueConversionPublicationException>(() => Compile(entry, State(form, request)));

        Assert.Equal(ValueConversionRejectionReason.VariableSourceContractUnknown, refusal.ReasonCode);
        Assert.Null(refusal.SourceType);
        Assert.Null(refusal.SourceRepresentation);
        Assert.Equal(new ValueTypeDescriptor("String"), refusal.TargetType);
        Assert.Equal(new ValueConversionBindingContext("consumer", "value", ValueConversionBindingKind.Input), refusal.Binding);
        Assert.DoesNotContain("payload-sentinel", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("option-sentinel", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AcceptedCases))]
    public void Ordinary_Auto_keeps_the_whole_variable_binding_bytes_and_hash(string entry, string form, string request)
    {
        var baseline = Compile(entry, State(form, null));
        var binding = Compile(entry, State(form, Request(request)));

        Assert.Equal(RuntimeInputBindingSource.VariableRead, binding.Source);
        Assert.Equal("payload-sentinel", binding.Variable!.VariableKey);
        Assert.Equal(form == "scope" ? "sequence" : "workflow", binding.Variable.DeclaringScopeId);
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
    public void A_malformed_variable_reference_wins_over_conversion_refusal(string entry)
    {
        var state = State("bare", Request("Json")) with
        {
            Value = new ArgumentValue(JsonSerializer.SerializeToElement(new { note = "payload-sentinel" }), "Variable")
        };

        var refusal = Assert.Throws<ArgumentException>(() => Compile(entry, state));

        Assert.DoesNotContain("payload-sentinel", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_nodes_credential_literal_refusal_wins_over_its_variable_conversion(bool pinned)
    {
        var compiler = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create());
        var variable = State("bare", Request("Json"));
        var credential = new ArgumentState("credential", new ArgumentValue("credential-sentinel", "Literal"), null, null, null, null);
        var refusal = Assert.Throws<CredentialLiteralRefusedException>(() =>
        {
            if (pinned)
                compiler.CompileAll("consumer", [Pinned("value"), Pinned("credential", credential: true)], [variable, credential]);
            else
                compiler.CompileAll("consumer", [Definition(), Definition() with { ReferenceKey = "credential", Name = "Credential", IsCredential = true }], [variable, credential]);
        });

        Assert.DoesNotContain("credential-sentinel", refusal.Message, StringComparison.Ordinal);
    }

    internal static AuthoredValueConversionRequest? Request(string intent) => intent switch
    {
        "omitted" => null,
        "Auto" => new(),
        "None" => new(AuthoredValueConversionMode.None),
        "Json" => new(AuthoredValueConversionMode.Json),
        "Xml" => new(AuthoredValueConversionMode.Xml),
        "Profile" => new(AuthoredValueConversionMode.Profile, new("elsa.json", "1")),
        "AutoProfile" => new(Profile: new("elsa.json", "1")),
        "Depth" => new(Limits: new(MaxDepth: 12)),
        "Items" => new(Limits: new(MaxCollectionItems: 12)),
        "Bytes" => new(Limits: new(MaxPayloadBytes: 12)),
        "Options" => new(Options: JsonSerializer.SerializeToElement(new { value = "option-sentinel" })),
        "NullOptions" => new(Options: JsonSerializer.SerializeToElement<object?>(null)),
        "ArrayOptions" => new(Options: JsonSerializer.SerializeToElement(Array.Empty<string>())),
        "UnknownMode" => new((AuthoredValueConversionMode)999),
        "empty-limits" => new(Limits: new()),
        "default-limits" => new(Limits: DefaultLimits()),
        "empty-options" => new(Options: JsonSerializer.SerializeToElement(new { })),
        "all-defaults" => new(Limits: DefaultLimits(), Options: JsonSerializer.SerializeToElement(new { })),
        _ => throw new ArgumentOutOfRangeException(nameof(intent))
    };

    private static AuthoredValueConversionLimits DefaultLimits() => new(
        ValueConversionLimits.Default.MaxDepth, ValueConversionLimits.Default.MaxCollectionItems, ValueConversionLimits.Default.MaxPayloadBytes);

    private static ArgumentState State(string form, AuthoredValueConversionRequest? request) => new("value",
        new ArgumentValue(form switch
        {
            "bare" => JsonSerializer.SerializeToElement("payload-sentinel"),
            "implicit-workflow" => JsonSerializer.SerializeToElement(new { referenceKey = "payload-sentinel" }),
            "workflow" => JsonSerializer.SerializeToElement(new { referenceKey = "payload-sentinel", declaringScopeId = "workflow" }),
            "scope" => JsonSerializer.SerializeToElement(new { referenceKey = "payload-sentinel", declaringScopeId = "sequence" }),
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        }, "Variable"), null, null, null, null, request);

    private static InputDefinition Definition() => new("value", "Value", new TypeReference("String"), null, "Value", null, IsNullable: false);
    private static RuntimeInputContract Pinned(string key, bool credential = false) =>
        new(key, key, new ValueTypeDescriptor("String"), false, false, false, null, ActivityValuePolicy.Default, isCredential: credential);

    private static RuntimeInputBinding Compile(string entry, ArgumentState state)
    {
        var compiler = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create());
        return entry switch
        {
            "single" => compiler.Compile("consumer", Definition(), state),
            "catalog" => compiler.CompileAll("consumer", [Definition()], [state])["value"],
            "pinned" => compiler.CompileAll("consumer", [Pinned("value")], [state])["value"],
            "boundary" => compiler.CompilePublishedBoundaryInput("consumer",
                new ActivityInputContract("value", "Value", new TypeReference("String"), false, false, null, "elsa.json"), state),
            _ => throw new ArgumentOutOfRangeException(nameof(entry))
        };
    }

    private static ExecutableNode Node(RuntimeInputBinding binding) =>
        WholeValueBindingHashTests.Node(new Dictionary<string, RuntimeInputBinding> { ["value"] = binding });
}
