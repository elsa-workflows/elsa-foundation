using Elsa.Workflows.Publishing.Services;
using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class RoleOwnedInputBindingContractTests
{
    private static readonly ValueTypeDescriptor StringType = new("System.String");

    [Fact]
    public void CanonicalBinding_CarriesExactlyOneRoleOwnedSource()
    {
        var request = new RuntimeInputBinding(
            inputKey: "customer-id",
            targetType: StringType,
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference("customer-id"));
        var variable = new RuntimeInputBinding(
            inputKey: "customer-id",
            targetType: StringType,
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.VariableRead,
            variable: new RuntimeVariableReference("customer", "scope-root"));
        var result = new RuntimeInputBinding(
            inputKey: "customer-id",
            targetType: StringType,
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("node-fetch", "customer-id", "scope-root"));

        Assert.Equal("customer-id", request.WorkflowRequest!.MemberKey);
        Assert.Equal("customer", variable.Variable!.VariableKey);
        Assert.Equal("node-fetch", result.ActivityResult!.ProducerExecutableNodeId);
        Assert.Throws<ArgumentException>(() => new RuntimeInputBinding(
            inputKey: "invalid",
            targetType: StringType,
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference("customer-id"),
            variable: new RuntimeVariableReference("customer", "scope-root")));
    }

    [Fact]
    public void LiteralBinding_OwnsAliasTypedEnvelopeAndDistinguishesNull()
    {
        var binding = new RuntimeInputBinding(
            inputKey: "customer-id",
            targetType: StringType,
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Null(StringType, ValueProtectionPolicy.InstanceInline));

        Assert.Equal(ValuePresence.ExplicitNull, binding.Literal!.Presence);
        Assert.Equal("System.String", binding.TargetType.Alias);
        Assert.Equal(ValueProtectionPolicy.InstanceInline, binding.EffectivePolicy);
    }

    [Fact]
    public void ActivityResultReference_UsesStructuralIdentityNotConcreteExecutionIdentity()
    {
        var properties = typeof(RuntimeActivityResultReference).GetProperties();

        Assert.Contains(properties, property => property.Name == nameof(RuntimeActivityResultReference.ProducerExecutableNodeId));
        Assert.Contains(properties, property => property.Name == nameof(RuntimeActivityResultReference.ProjectionKey));
        Assert.Contains(properties, property => property.Name == nameof(RuntimeActivityResultReference.ProducerScopeId));
        Assert.DoesNotContain(properties, property => property.Name.Contains("ActivityExecutionId", StringComparison.Ordinal));
    }

    [Fact]
    public void CanonicalBindingSerialization_DoesNotContainAssemblyQualifiedTypeMetadata()
    {
        var binding = new RuntimeInputBinding(
            inputKey: "customer-id",
            targetType: StringType,
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(
                StringType,
                JsonSerializer.SerializeToElement("customer-1"),
                ValueProtectionPolicy.InstanceInline));

        var json = JsonSerializer.Serialize(binding);

        Assert.Contains("System.String", json);
        Assert.DoesNotContain("typeName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Version=", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExecutableHash_CoversCanonicalRolePayloadAndNormalizesMetadataOrder()
    {
        var first = ResultBinding("customer-id", new Dictionary<string, string>
        {
            ["z"] = "last",
            ["a"] = "first"
        });
        var reordered = ResultBinding("customer-id", new Dictionary<string, string>
        {
            ["a"] = "first",
            ["z"] = "last"
        });
        var changedProjection = ResultBinding("account-id", first.Metadata);
        var hasher = new WorkflowExecutableHasher();

        Assert.Equal(hasher.ComputeHash(Node(first)), hasher.ComputeHash(Node(reordered)));
        Assert.NotEqual(hasher.ComputeHash(Node(first)), hasher.ComputeHash(Node(changedProjection)));
    }

    [Fact]
    public void ExecutableHash_NormalizesExpressionParametersAndCoversOptionsAndCapabilities()
    {
        var first = ExpressionBinding(
            new Dictionary<string, ExpressionParameterBinding>
            {
                ["tax"] = new VariableExpressionParameterBinding("workflow", "tax"),
                ["subtotal"] = new WorkflowRequestExpressionParameterBinding("subtotal")
            },
            JsonSerializer.SerializeToElement(new { strict = true }),
            ExpressionCapabilityProfiles.BindingPureV1);
        var reordered = ExpressionBinding(
            new Dictionary<string, ExpressionParameterBinding>
            {
                ["subtotal"] = new WorkflowRequestExpressionParameterBinding("subtotal"),
                ["tax"] = new VariableExpressionParameterBinding("workflow", "tax")
            },
            JsonSerializer.SerializeToElement(new { strict = true }),
            ExpressionCapabilityProfiles.BindingPureV1);
        var changedOptions = ExpressionBinding(
            first.Expression!.Parameters,
            JsonSerializer.SerializeToElement(new { strict = false }),
            ExpressionCapabilityProfiles.BindingPureV1);
        var changedCapability = ExpressionBinding(
            first.Expression.Parameters,
            first.Expression.Options,
            "test/non-binding-capability-v1");
        var hasher = new WorkflowExecutableHasher();

        Assert.Equal(hasher.ComputeHash(Node(first)), hasher.ComputeHash(Node(reordered)));
        Assert.NotEqual(hasher.ComputeHash(Node(first)), hasher.ComputeHash(Node(changedOptions)));
        Assert.NotEqual(hasher.ComputeHash(Node(first)), hasher.ComputeHash(Node(changedCapability)));
    }

    [Fact]
    public void ExecutableHash_CoversIntrinsicKindAndVariableTarget()
    {
        var value = new RuntimeInputBinding(
            WorkflowIntrinsicInputKeys.Value,
            StringType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(
                StringType,
                JsonSerializer.SerializeToElement("updated"),
                ValueProtectionPolicy.InstanceInline));
        var first = IntrinsicNode(value, "scope-root", "first");
        var changedTarget = IntrinsicNode(value, "scope-root", "second");
        var hasher = new WorkflowExecutableHasher();

        Assert.NotEqual(hasher.ComputeHash(first), hasher.ComputeHash(changedTarget));
    }

    [Fact]
    public void Compiler_EmitsAliasTypedCanonicalLiteralWithoutClrTypeMetadata()
    {
        var compiler = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create());
        var input = new InputDefinition(
            ReferenceKey: "message",
            Name: "Text",
            Type: new TypeReference("String"),
            StorageDriverType: null,
            DisplayName: "Text",
            Category: null,
            IsNullable: false);

        var binding = compiler.Compile("node-write", input, new ArgumentValue("hello", "Literal"));

        Assert.Equal("message", binding.InputKey);
        Assert.Equal("String", binding.TargetType.Alias);
        Assert.Equal("hello", binding.Literal!.InlineValue!.Value.GetString());
        Assert.DoesNotContain("typeName", binding.Metadata);
        Assert.DoesNotContain(", System.", JsonSerializer.Serialize(binding), StringComparison.Ordinal);
    }

    [Fact]
    public void Compiler_PreservesAuthoredInputStorageAndSensitivityPolicy()
    {
        var compiler = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create());
        var input = new InputDefinition(
            ReferenceKey: "message",
            Name: "Text",
            Type: new TypeReference("String"),
            StorageDriverType: null,
            DisplayName: "Text",
            Category: null,
            IsNullable: false);
        var state = new ArgumentState(
            "message",
            new ArgumentValue("secret", "Literal"),
            AutoEvaluate: null,
            EvaluatorType: null,
            StorageDriverType: "encrypted-payloads",
            IsSensitive: true);

        var binding = compiler.Compile("node-write", input, state);

        Assert.Equal(DurableValueStorage.External, binding.EffectivePolicy.Storage);
        Assert.True(binding.EffectivePolicy.IsSensitive);
        Assert.Equal("encrypted-payloads", binding.EffectivePolicy.Metadata["storageProfile"]);
        Assert.Equal(binding.EffectivePolicy, binding.Literal!.Policy);
    }

    [Fact]
    public void Compiler_PreservesPortableExpressionDefinitionWithoutAmbientCompatibility()
    {
        var compiler = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create());
        var input = new InputDefinition(
            ReferenceKey: "message",
            Name: "Text",
            Type: new TypeReference("String"),
            StorageDriverType: null,
            DisplayName: "Text",
            Category: null,
            IsNullable: false);
        var definition = new ExpressionDefinition(
            "JavaScript",
            "args.customerId",
            new TypeReference("String"),
            new Dictionary<string, ExpressionParameterBinding>
            {
                ["customerId"] = new WorkflowRequestExpressionParameterBinding("customer-id")
            },
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV1,
            new Dictionary<string, string> { ["origin"] = "elsa3-import" });

        var binding = compiler.Compile(
            "node-write",
            input,
            new ArgumentValue(JsonSerializer.SerializeToElement(definition), "JavaScript"));

        Assert.Equal(RuntimeInputBindingSource.Expression, binding.Source);
        var expression = Assert.IsType<RuntimeExpressionBinding>(binding.Expression);
        Assert.Equal("args.customerId", expression.Expression);
        Assert.Equal(ExpressionCapabilityProfiles.BindingPureV1, expression.CapabilityProfile);
        Assert.Equal("customer-id", Assert.IsType<WorkflowRequestExpressionParameterBinding>(expression.Parameters["customerId"]).MemberKey);
        Assert.Equal("elsa3-import", expression.Metadata["origin"]);
    }

    [Fact]
    public void Compiler_CoercesScalarLiteral_IntoSingleElementStringCollection()
    {
        // Issue #924: a bare scalar typed into a collection input compiles to a one-element collection ("GET" → ["GET"]).
        var compiler = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create());
        var input = CollectionInput("SupportedMethods", "String");

        var binding = compiler.Compile("node-endpoint", input, new ArgumentValue("GET", "Literal"));

        var array = binding.Literal!.InlineValue!.Value;
        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        Assert.Equal(["GET"], array.EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void Compiler_CoercesCommaSeparatedScalar_IntoPrimitiveCollection()
    {
        // Issue #924: a comma-separated scalar into a primitive collection splits per item ("200, 404" → [200, 404]).
        var compiler = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create());
        var input = CollectionInput("ExpectedStatusCodes", "Int32");

        var binding = compiler.Compile("node-send", input, new ArgumentValue("200, 404", "Literal"));

        var array = binding.Literal!.InlineValue!.Value;
        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        Assert.Equal([200, 404], array.EnumerateArray().Select(item => item.GetInt32()));
    }

    [Fact]
    public void Compiler_UnconvertibleLiteral_RaisesStructuredCoercionDiagnostic_NotRawException()
    {
        // Issue #924: a literal that cannot be coerced surfaces as the structured VF-COER-001 diagnostic (with node id
        // and reference key) rather than letting a raw FormatException/JsonException escape as an unstructured 500.
        var compiler = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create());
        var input = new InputDefinition(
            ReferenceKey: "count",
            Name: "Count",
            Type: new TypeReference("Int32"),
            StorageDriverType: null,
            DisplayName: "Count",
            Category: null,
            IsNullable: false);

        var exception = Assert.Throws<ValueConversionPublicationException>(() =>
            compiler.Compile("node-send", input, new ArgumentValue("not-a-number", "Literal")));

        Assert.Equal(ValueConversionRejectionReason.LiteralCoercionFailed, exception.ReasonCode);
        Assert.StartsWith("VF-COER-001", exception.Message, StringComparison.Ordinal);
        Assert.Equal("node-send", exception.Binding!.NodeId);
        Assert.Equal("count", exception.Binding.ReferenceKey);
    }

    [Fact]
    public void CompileOmitted_NonNullableValueType_PinsClrDefault()
    {
        // Issue #925: an omitted optional input whose non-nullable CLR type has a natural default is pinned to it
        // (bool → false) instead of raising VF-ACT-003, so a UI that authors no value can still publish.
        var compiler = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create());
        var input = new InputDefinition(
            ReferenceKey: "Authorize",
            Name: "Authorize",
            Type: new TypeReference("Boolean"),
            StorageDriverType: null,
            DisplayName: "Authorize",
            Category: null,
            IsNullable: false,
            IsRequired: false);

        var binding = compiler.CompileOmitted(input);

        Assert.Equal(RuntimeInputBindingSource.Literal, binding.Source);
        Assert.Equal(ValuePresence.Present, binding.Literal!.Presence);
        Assert.False(binding.Literal.InlineValue!.Value.GetBoolean());
    }

    [Fact]
    public void CompileOmitted_NonNullableReferenceType_StillRaisesVfAct003()
    {
        // A non-nullable reference type has no fabricable non-null default, so omission remains a hard contract error.
        var compiler = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create());
        var input = new InputDefinition(
            ReferenceKey: "message",
            Name: "Text",
            Type: new TypeReference("String"),
            StorageDriverType: null,
            DisplayName: "Text",
            Category: null,
            IsNullable: false,
            IsRequired: false);

        var exception = Assert.Throws<ArgumentException>(() => compiler.CompileOmitted(input));
        Assert.Contains("VF-ACT-003", exception.Message);
    }

    private static InputDefinition CollectionInput(string key, string elementAlias) =>
        new(
            ReferenceKey: key,
            Name: key,
            Type: new TypeReference(elementAlias, CollectionKind.List),
            StorageDriverType: null,
            DisplayName: key,
            Category: null,
            IsNullable: true);

    private static RuntimeInputBinding ResultBinding(
        string projectionKey,
        IReadOnlyDictionary<string, string> metadata) =>
        new(
            inputKey: "customer-id",
            targetType: StringType,
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("node-fetch", projectionKey, "scope-root"),
            metadata: metadata);

    private static RuntimeInputBinding ExpressionBinding(
        IReadOnlyDictionary<string, ExpressionParameterBinding> parameters,
        JsonElement options,
        string capabilityProfile) =>
        new(
            inputKey: "total",
            targetType: new ValueTypeDescriptor("Decimal"),
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(
                "JavaScript",
                "args.subtotal + args.tax",
                new RuntimeValueTypeDescriptor("alias", "Decimal", null),
                parameters: parameters,
                options: options,
                capabilityProfile: capabilityProfile));

    private static ExecutableNode Node(RuntimeInputBinding binding) =>
        new(
            executableNodeId: "node-consumer",
            authoredActivityId: "consumer",
            activityType: "Tests.Consumer",
            activityTypeVersion: "1.0.0",
            descriptorType: "clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { typeAlias = "Tests.Consumer" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { [binding.InputKey] = binding },
            metadata: new Dictionary<string, string>());

    private static ExecutableNode IntrinsicNode(RuntimeInputBinding binding, string scopeId, string variableKey) =>
        new(
            executableNodeId: "node-set",
            authoredActivityId: "set",
            activityType: "elsa.intrinsic.set",
            activityTypeVersion: "1.0.0",
            descriptorType: "intrinsic",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { [binding.InputKey] = binding },
            metadata: new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Set,
            intrinsicVariable: new RuntimeVariableReference(variableKey, scopeId));
}
