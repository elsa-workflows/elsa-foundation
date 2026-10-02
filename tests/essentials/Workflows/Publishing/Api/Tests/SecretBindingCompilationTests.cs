using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using RuntimeActivityInputContract = Elsa.Activities.Runtime.Core.Models.ActivityInputContract;
using VariableDefinition = Elsa.Expressions.Core.Models.VariableDefinition;
using VariableReference = Elsa.Expressions.Core.Models.VariableReference;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;
using static Elsa.Workflows.Publishing.Api.Tests.SecretBindingCompilerFixture;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// A <c>Secret</c> binding compiles to a <see cref="RuntimeInputBindingSource.SecretRead"/> with a conversion plan
/// from text, is refused where the input type cannot hold text, and is refused on activity kinds that read inputs
/// outside CLR activation (spec 188, T010; research R11, R12).
/// </summary>
public sealed class SecretBindingCompilationTests
{
    private const string InputKey = "target";

    private readonly RuntimeInputBindingCompiler _compiler = new(TestWellKnownTypeRegistry.Create());

    public static TheoryData<string?> SecretTypes => new() { "text", "rsa-key", null };

    public static TheoryData<string, CollectionKind, string?> TextTargets => Cross(
        [("String", CollectionKind.Single), ("Elsa.Any", CollectionKind.Single), ("JsonNode", CollectionKind.Single)]);

    public static TheoryData<string, CollectionKind, string?> NonTextTargets => Cross(
    [
        ("Int32", CollectionKind.Single),
        ("Boolean", CollectionKind.Single),
        ("DateTime", CollectionKind.Single),
        ("TimeSpan", CollectionKind.Single),
        ("System.DayOfWeek", CollectionKind.Single),
        ("Guid", CollectionKind.Single),
        ("Uri", CollectionKind.Single),
        ("Object", CollectionKind.Single),
        ("JsonElement", CollectionKind.Single),
        ("JsonObject", CollectionKind.Single),
        ("String", CollectionKind.List),
        ("Int32", CollectionKind.List)
    ]);

    [Theory]
    [MemberData(nameof(SecretTypes))]
    public void A_secret_binding_compiles_to_a_secret_read_never_to_an_expression(string? typeName)
    {
        var binding = _compiler.Compile("node-1", Input("String"), Secret(InputKey, typeName));

        Assert.Equal(RuntimeInputBindingSource.SecretRead, binding.Source);
        Assert.Null(binding.Expression);
        Assert.Null(binding.Literal);
        Assert.Equal(new RuntimeSecretReference(SecretName, typeName), binding.Secret);
        Assert.True(binding.EffectivePolicy.IsSensitive);
        Assert.True(binding.EffectivePolicy.RequiresEncryption);
    }

    [Fact]
    public void The_reference_name_is_required_and_type_and_scope_are_optional()
    {
        var binding = _compiler.Compile("node-1", Input("String"), State(new { name = SecretName, typeName = "text", scope = "billing" }));
        var nameOnly = _compiler.Compile("node-1", Input("String"), State(new { name = SecretName }));

        Assert.Equal(new RuntimeSecretReference(SecretName, "text", "billing"), binding.Secret);
        Assert.Equal(new RuntimeSecretReference(SecretName), nameOnly.Secret);
    }

    [Theory]
    [InlineData("\"sk-live-literal-text\"")]
    [InlineData("[\"sk-live-literal-text\"]")]
    [InlineData("{\"name\":\"   \",\"note\":\"sk-live-literal-text\"}")]
    [InlineData("{\"typeName\":\"sk-live-literal-text\"}")]
    [InlineData("{\"name\":\"payments.api-key\",\"scope\":{\"value\":\"sk-live-literal-text\"}}")]
    public void A_malformed_secret_payload_is_refused_without_echoing_it(string payload)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => _compiler.Compile(
            "node-1",
            Input("String"),
            new WorkflowArgumentState(InputKey, new ArgumentValue(JsonDocument.Parse(payload).RootElement.Clone(), "Secret"), null, null, null, null)));

        Assert.Contains("'node-1'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{InputKey}'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-live-literal-text", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(TextTargets))]
    public void A_text_or_any_typed_input_accepts_a_secret_with_a_plan_from_text(string alias, CollectionKind collectionKind, string? typeName)
    {
        var binding = _compiler.Compile("node-1", Input(alias, collectionKind), Secret(InputKey, typeName));

        var plan = binding.ConversionPlan!;
        Assert.Equal(RuntimeInputBindingSource.SecretRead, binding.Source);
        Assert.Equal("String", plan.SourceType.Alias);
        Assert.Equal(ValueRepresentation.TextValue, plan.SourceRepresentation);
        Assert.Equal(alias == "String" ? ValueConversionOperation.Identity : ValueConversionOperation.CanonicalAny, plan.Operation);
    }

    [Theory]
    [MemberData(nameof(NonTextTargets))]
    public void An_input_without_a_plan_from_text_refuses_a_secret_with_the_conversion_code(string alias, CollectionKind collectionKind, string? typeName)
    {
        var exception = Assert.Throws<ValueConversionPublicationException>(() =>
            _compiler.Compile("node-1", Input(alias, collectionKind), Secret(InputKey, typeName)));

        Assert.StartsWith("VF-COER-001:", exception.Message, StringComparison.Ordinal);
        Assert.Equal("node-1", exception.Binding!.NodeId);
        Assert.Equal(InputKey, exception.Binding.ReferenceKey);
    }

    [Fact]
    public void The_pinned_contract_path_compiles_a_secret_the_same_way()
    {
        var contract = new RuntimeActivityInputContract(InputKey, InputKey, new ValueTypeDescriptor("String"), false, true, false, null, ActivityValuePolicy.Default);

        var binding = Assert.Single(_compiler.CompileAll("node-1", [contract], [Secret(InputKey)])).Value;

        Assert.Equal(RuntimeInputBindingSource.SecretRead, binding.Source);
        Assert.Equal(ValueConversionOperation.Identity, binding.ConversionPlan!.Operation);
        Assert.True(binding.EffectivePolicy.RequiresEncryption);
    }

    [Fact]
    public async Task Publication_compiles_a_secret_on_a_clr_activity_input_to_a_secret_read()
    {
        var executable = await CompileAsync(Node(typeof(TestWriteLineActivity), Secret("Text", "text")), [typeof(TestWriteLineActivity)]);

        var binding = executable.RootActivity.InputBindings["Text"];
        Assert.Equal(RuntimeInputBindingSource.SecretRead, binding.Source);
        Assert.Equal(new RuntimeSecretReference(SecretName, "text"), binding.Secret);
        Assert.Equal(ValueConversionOperation.Identity, binding.ConversionPlan!.Operation);
    }

    [Fact]
    public async Task An_intrinsic_node_refuses_a_secret_reference()
    {
        var variable = new VariableDefinition("var-token", "Token", new TypeReference("String"), StorageDriverType: null, Default: null);
        var setNode = new ActivityNode("set-1", "$intrinsic", [new WorkflowArgumentState(WorkflowIntrinsicInputKeys.Value, SecretValue(), null, null, null, null)], [])
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(
                AuthoredWorkflowIntrinsicKind.Set,
                new TypeReference("String"),
                new VariableReference("var-token", VariableReference.WorkflowScopeId))
        };

        var exception = await AssertRefusedAsync(setNode, [], variables: [variable]);

        Assert.Equal(
            SecretBindingDiagnostics.SecretBindingRefused("set-1", WorkflowIntrinsicInputKeys.Value, "workflow intrinsics write their values into persisted workflow state").Message,
            exception.Message);
    }

    [Fact]
    public async Task A_graph_activity_node_refuses_a_secret_reference()
    {
        var graphVersion = ActivityVersion(
            "test.graph",
            WellKnownRuntimeActivityConsumers.GraphActivity,
            JsonSerializer.SerializeToElement(new { entryOccurrenceId = "entry" }),
            [Input("String") with { ReferenceKey = "Text" }]);

        var exception = await AssertRefusedAsync(new ActivityNode(NodeId, "test.graph", [Secret("Text")], []), [], [graphVersion]);

        Assert.Equal(
            SecretBindingDiagnostics.SecretBindingRefused(NodeId, "Text", $"activity consumer '{WellKnownRuntimeActivityConsumers.GraphActivity}' does not resolve inputs when the activity runs").Message,
            exception.Message);
    }

    [Fact]
    public async Task A_checkpoint_participant_refuses_a_secret_reference()
    {
        var exception = await AssertRefusedAsync(
            Node(typeof(CheckpointParticipantActivity), Secret(nameof(CheckpointParticipantActivity.Text))),
            [typeof(CheckpointParticipantActivity)]);

        Assert.Equal(
            SecretBindingDiagnostics.SecretBindingRefused(NodeId, nameof(CheckpointParticipantActivity.Text), "the activity reads its inputs into checkpoint state outside activation").Message,
            exception.Message);
    }

    private static InputDefinition Input(string alias, CollectionKind collectionKind = CollectionKind.Single) =>
        new(InputKey, InputKey, new TypeReference(alias, collectionKind), null, InputKey, null, IsNullable: true);

    private static WorkflowArgumentState State(object payload) =>
        new(InputKey, new ArgumentValue(JsonSerializer.SerializeToElement(payload), "Secret"), null, null, null, null);

    private static TheoryData<string, CollectionKind, string?> Cross(IEnumerable<(string Alias, CollectionKind CollectionKind)> targets)
    {
        var data = new TheoryData<string, CollectionKind, string?>();
        foreach (var (alias, collectionKind) in targets)
        foreach (var typeName in new[] { "text", "rsa-key", null })
            data.Add(alias, collectionKind, typeName);
        return data;
    }

    private sealed class CheckpointParticipantActivity : Activity<ActivityUnit>, IRuntimeActivityCheckpointParticipant
    {
        [Elsa.Activities.Runtime.Core.Attributes.ActivityInput(Key = nameof(Text))]
        public string? Text { get; set; }

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));

        public ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> PrepareEntryCheckpointAsync(
            IRuntimeActivityExecutionContext context,
            IReadOnlyDictionary<string, object?> effectiveInputs,
            DateTimeOffset capturedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<RuntimeActivityCompletionCheckpointPreparation> PrepareCompletionCheckpointAsync(
            IRuntimeActivityExecutionContext context,
            IReadOnlyCollection<DurableValueState> persistedValues,
            DateTimeOffset capturedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
