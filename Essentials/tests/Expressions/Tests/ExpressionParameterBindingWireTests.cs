using System.Text.Json;
using Elsa.Events;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.Tests;

/// <summary>
/// The persisted shape of every expression parameter binding kind: the <c>kind</c> discriminator value, the member
/// names, and the bytes the payload serializer writes. Executables store these inside their expression definitions, so
/// a binding written before a change must read back the same after it.
/// </summary>
public sealed class ExpressionParameterBindingWireTests
{
    private static readonly IReadOnlyDictionary<string, (ExpressionParameterBinding Binding, string Json)> Kinds =
        new Dictionary<string, (ExpressionParameterBinding, string)>
        {
            ["Literal"] = (
                new LiteralExpressionParameterBinding(JsonSerializer.SerializeToElement(new { amount = 1.5m })),
                """{"kind":"Literal","value":{"amount":1.5}}"""),
            ["WorkflowRequest"] = (
                new WorkflowRequestExpressionParameterBinding("order", "lines[0]"),
                """{"kind":"WorkflowRequest","memberKey":"order","path":"lines[0]"}"""),
            ["Variable"] = (
                new VariableExpressionParameterBinding("workflow", "tax"),
                """{"kind":"Variable","declaringScopeNodeId":"workflow","variableKey":"tax"}"""),
            ["ActivityResult"] = (
                new ActivityResultExpressionParameterBinding("calculate", "amount", isOptional: true),
                """{"kind":"ActivityResult","producerNodeId":"calculate","projectionKey":"amount","isOptional":true}"""),
            ["WorkflowInstance"] = (
                new WorkflowInstanceExpressionParameterBinding(WorkflowInstanceExpressionParameterBinding.Members.CorrelationId),
                """{"kind":"WorkflowInstance","member":"correlationId"}"""),
            ["Clock"] = (
                new ClockExpressionParameterBinding(),
                """{"kind":"Clock"}"""),
            ["UniqueId"] = (
                new UniqueIdExpressionParameterBinding(),
                """{"kind":"UniqueId"}"""),
        };

    private static readonly string[] OriginalKinds = ["Literal", "WorkflowRequest", "Variable", "ActivityResult"];

    [Theory]
    [InlineData("Literal")]
    [InlineData("WorkflowRequest")]
    [InlineData("Variable")]
    [InlineData("ActivityResult")]
    [InlineData("WorkflowInstance")]
    [InlineData("Clock")]
    [InlineData("UniqueId")]
    public void A_binding_kind_keeps_its_discriminator_and_shape(string kind)
    {
        var (binding, json) = Kinds[kind];

        Assert.Equal(json, JsonSerializer.Serialize(binding));
        var read = JsonSerializer.Deserialize<ExpressionParameterBinding>(json)!;
        Assert.Equal(binding.GetType(), read.GetType());
        Assert.Equal(json, JsonSerializer.Serialize(read));
    }

    [Theory]
    [InlineData("correlationId")]
    [InlineData("instanceId")]
    public void A_workflow_instance_binding_reads_a_known_member(string member) =>
        Assert.Equal(member, new WorkflowInstanceExpressionParameterBinding(member).Member);

    [Theory]
    [InlineData("CorrelationId")]
    [InlineData("instanceName")]
    [InlineData("")]
    public void A_workflow_instance_binding_rejects_an_unknown_member(string member)
    {
        Assert.Throws<ArgumentException>(() => new WorkflowInstanceExpressionParameterBinding(member));
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<ExpressionParameterBinding>(
            $$"""{"kind":"WorkflowInstance","member":"{{member}}"}"""));
    }

    /// <summary>
    /// Through the payload serializer the public features compose, after their startup has contributed every converter
    /// through the real event publisher: the expressions converter source, its variable converter and the type aliases
    /// it seeds are internal, so this pins that they still reach the serializer, that a definition keeps its bytes and
    /// fingerprint, and that a persisted variable definition still reads back as a typed variable.
    /// </summary>
    [Fact]
    public async Task A_definition_keeps_its_persisted_bytes_and_a_variable_reads_back_through_the_composed_serializer()
    {
        var services = SerializationServices.Compose();
        new EventsFeature().ConfigureServices(services);
        new ExpressionsFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            foreach (var startupTask in scope.ServiceProvider.GetServices<IStartupTask>())
                await startupTask.ExecuteAsync(CancellationToken.None);
        var serializer = provider.GetRequiredService<IPayloadSerializer>();

        var definition = new ExpressionDefinition(
            "JavaScript",
            "args.a + args.b + args.c + args.d",
            new TypeReference("String"),
            Kinds.Where(kind => OriginalKinds.Contains(kind.Key)).ToDictionary(kind => kind.Key, kind => kind.Value.Binding),
            JsonSerializer.SerializeToElement(new { strict = true }),
            ExpressionCapabilityProfiles.BindingPureV1);
        var persistedDefinition = serializer.Serialize(definition);

        Assert.Equal(
            """{"capabilityProfile":"binding-pure-v1","language":"JavaScript","metadata":{},"options":{"strict":true},"parameters":{"ActivityResult":{"kind":"ActivityResult","isOptional":true,"producerNodeId":"calculate","projectionKey":"amount"},"Literal":{"kind":"Literal","value":{"amount":1.5}},"Variable":{"kind":"Variable","declaringScopeNodeId":"workflow","variableKey":"tax"},"WorkflowRequest":{"kind":"WorkflowRequest","memberKey":"order","path":"lines[0]"}},"resultType":{"alias":"String","collectionKind":"Single"},"source":"args.a \u002B args.b \u002B args.c \u002B args.d"}""",
            persistedDefinition);
        Assert.Equal(persistedDefinition, serializer.Serialize(serializer.Deserialize<ExpressionDefinition>(persistedDefinition)));
        Assert.Equal("sha256:50b90b42c725579871443de1331a179a4faec4f2542f5ea0fa7603093e29cc80", definition.Fingerprint);

        // A persisted variable definition reads back as a typed variable through the expressions converter.
        var variable = serializer.Deserialize<IVariable>(
            """{"referenceKey":"var-total","name":"Total","type":{"alias":"Int32","collectionKind":"Single"},"default":{"value":42}}""");

        Assert.Equal(("var-total", "Total", typeof(int), (object?)42), (variable.Id, variable.Name, variable.GetVariableType(), variable.DefaultValue));
    }

    [Fact]
    public async Task A_workflow_instance_binding_keeps_its_persisted_bytes_through_the_composed_serializer()
    {
        var services = SerializationServices.Compose();
        new EventsFeature().ConfigureServices(services);
        new ExpressionsFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            foreach (var startupTask in scope.ServiceProvider.GetServices<IStartupTask>())
                await startupTask.ExecuteAsync(CancellationToken.None);
        var serializer = provider.GetRequiredService<IPayloadSerializer>();

        var definition = new ExpressionDefinition(
            "JavaScript",
            "args.correlation",
            new TypeReference("String"),
            new Dictionary<string, ExpressionParameterBinding> { ["correlation"] = Kinds["WorkflowInstance"].Binding },
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV1);
        var persistedDefinition = serializer.Serialize(definition);

        Assert.Equal(
            """{"capabilityProfile":"binding-pure-v1","language":"JavaScript","metadata":{},"options":{},"parameters":{"correlation":{"kind":"WorkflowInstance","member":"correlationId"}},"resultType":{"alias":"String","collectionKind":"Single"},"source":"args.correlation"}""",
            persistedDefinition);
        Assert.Equal(persistedDefinition, serializer.Serialize(serializer.Deserialize<ExpressionDefinition>(persistedDefinition)));
    }

    [Fact]
    public async Task Clock_and_unique_id_bindings_keep_their_persisted_bytes_and_change_the_fingerprint()
    {
        var services = SerializationServices.Compose();
        new EventsFeature().ConfigureServices(services);
        new ExpressionsFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            foreach (var startupTask in scope.ServiceProvider.GetServices<IStartupTask>())
                await startupTask.ExecuteAsync(CancellationToken.None);
        var serializer = provider.GetRequiredService<IPayloadSerializer>();

        var definition = new ExpressionDefinition(
            "JavaScript",
            "args.now + args.id",
            new TypeReference("String"),
            new Dictionary<string, ExpressionParameterBinding> { ["now"] = Kinds["Clock"].Binding, ["id"] = Kinds["UniqueId"].Binding },
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV2);
        var persistedDefinition = serializer.Serialize(definition);

        Assert.Equal(
            """{"capabilityProfile":"binding-pure-v2","language":"JavaScript","metadata":{},"options":{},"parameters":{"id":{"kind":"UniqueId"},"now":{"kind":"Clock"}},"resultType":{"alias":"String","collectionKind":"Single"},"source":"args.now \u002B args.id"}""",
            persistedDefinition);
        Assert.Equal(persistedDefinition, serializer.Serialize(serializer.Deserialize<ExpressionDefinition>(persistedDefinition)));

        var withLiterals = new ExpressionDefinition(
            "JavaScript",
            "args.now + args.id",
            new TypeReference("String"),
            new Dictionary<string, ExpressionParameterBinding> { ["now"] = Kinds["Literal"].Binding, ["id"] = Kinds["Literal"].Binding },
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV2);
        Assert.NotEqual(withLiterals.Fingerprint, definition.Fingerprint);
    }
}
