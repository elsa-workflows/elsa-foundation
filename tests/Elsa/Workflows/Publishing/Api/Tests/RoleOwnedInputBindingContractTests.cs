using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Publishing.Api.Services;
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
    public void Compiler_EmitsAliasTypedCanonicalLiteralWithoutClrTypeMetadata()
    {
        var compiler = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create());
        var input = new InputDefinition(
            ReferenceKey: "message",
            Name: "Text",
            Type: new TypeReference("String"),
            StorageDriverType: null,
            DisplayName: "Text",
            Category: null);

        var binding = compiler.Compile("node-write", input, new ArgumentValue("hello", "Literal"));

        Assert.Equal("message", binding.InputKey);
        Assert.Equal("String", binding.TargetType.Alias);
        Assert.Equal("hello", binding.Literal!.InlineValue!.Value.GetString());
        Assert.DoesNotContain("typeName", binding.Metadata);
        Assert.DoesNotContain(", System.", JsonSerializer.Serialize(binding), StringComparison.Ordinal);
    }

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

    private static ExecutableNode Node(RuntimeInputBinding binding) =>
        new(
            executableNodeId: "node-consumer",
            authoredActivityId: "consumer",
            activityType: "Tests.Consumer",
            activityTypeVersion: "1.0.0",
            descriptorType: "clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { typeAlias = "Tests.Consumer" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { [binding.InputKey] = binding },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());
}
