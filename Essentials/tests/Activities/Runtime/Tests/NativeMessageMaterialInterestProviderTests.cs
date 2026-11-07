using System.Text.Json;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Services;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class NativeMessageMaterialInterestProviderTests
{
    private readonly NativeMessageMaterialInterestProvider provider = new();

    [Fact]
    public void Whole_native_material_includes_nested_receive_only_nodes_before_any_bookmark_or_start_exists()
    {
        var wait = Node("wait", "order", false);
        var start = Node("start", "created", true);
        var root = Node("root", type: "other", children: [wait, Node("nested", type: "other", children: [start])]);
        var result = provider.Describe(Material(root));
        Assert.True(result.IsRecognized); Assert.Equal(2, result.Declarations.Count);
        Assert.Equal("order", Assert.Single(result.Declarations.Single(item => item.ReceiverAddress == "wait").MessageNames));
        Assert.False(result.Declarations.Single(item => item.ReceiverAddress == "wait").CanStartWorkflow);
        Assert.True(result.Declarations.Single(item => item.ReceiverAddress == "start").CanStartWorkflow);
    }

    [Fact]
    public void Declared_dynamic_names_are_finite_ordinal_and_do_not_evaluate_name_or_business_key_expressions()
    {
        var names = JsonSerializer.SerializeToElement(new[] { "order", "Order" });
        var bindings = Bindings(null, false, names); bindings[nameof(ReceiveMessage.MessageName)] = Expression(nameof(ReceiveMessage.MessageName));
        bindings[nameof(ReceiveMessage.BusinessKey)] = Expression(nameof(ReceiveMessage.BusinessKey));
        var declaration = Assert.Single(provider.Describe(Material(Node("dynamic", bindings: bindings))).Declarations);
        Assert.Equal(new[] { "Order", "order" }, declaration.MessageNames);
    }

    [Fact]
    public void Fixed_names_use_literal_interest_and_never_widen_from_mutable_metadata_or_a_declaration_superset()
    {
        var node = Node("wait", "fixed", false, JsonSerializer.SerializeToElement(new[] { "fixed", "extra" }),
            metadata: new Dictionary<string, string> { ["MessageName"] = "forged", ["CanStartWorkflow"] = "true", ["DeclaredMessageNames"] = "wildcard" });
        var declaration = Assert.Single(provider.Describe(Material(node)).Declarations);
        Assert.Equal("fixed", Assert.Single(declaration.MessageNames)); Assert.False(declaration.CanStartWorkflow);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("start")]
    [InlineData("dynamic-set")]
    public void Each_authoritative_input_independently_changes_material_hash_and_its_declaration(string change)
    {
        var first = Node("wait", "first", false);
        var second = Node("wait", change == "name" ? "second" : "first", change == "start");
        if (change == "dynamic-set")
        {
            var firstBindings = Bindings(null, false, JsonSerializer.SerializeToElement(new[] { "first" }));
            var secondBindings = Bindings(null, false, JsonSerializer.SerializeToElement(new[] { "first", "second" }));
            firstBindings[nameof(ReceiveMessage.MessageName)] = Expression(nameof(ReceiveMessage.MessageName));
            secondBindings[nameof(ReceiveMessage.MessageName)] = Expression(nameof(ReceiveMessage.MessageName));
            first = Node("wait", bindings: firstBindings); second = Node("wait", bindings: secondBindings);
        }
        var hasher = new WorkflowExecutableHasher();
        Assert.NotEqual(hasher.ComputeHash(first), hasher.ComputeHash(second));
        var before = Assert.Single(provider.Describe(Material(first)).Declarations);
        var after = Assert.Single(provider.Describe(Material(second)).Declarations);
        if (change == "start")
        {
            Assert.Equal(before.MessageNames, after.MessageNames);
            Assert.False(before.CanStartWorkflow); Assert.True(after.CanStartWorkflow);
        }
        else
        {
            Assert.Equal(before.CanStartWorkflow, after.CanStartWorkflow);
            Assert.Equal(new[] { "first" }, before.MessageNames);
            Assert.Equal(change == "name" ? new[] { "second" } : new[] { "first", "second" }, after.MessageNames);
        }
    }

    [Theory]
    [InlineData("missing-name")] [InlineData("null-name")] [InlineData("number-name")]
    [InlineData("dynamic-no-declaration")] [InlineData("dynamic-empty")] [InlineData("dynamic-expression-declaration")]
    [InlineData("duplicate")] [InlineData("too-many")] [InlineData("invalid-unicode")] [InlineData("too-long")]
    [InlineData("missing-mode")] [InlineData("dynamic-mode")] [InlineData("nonbool-mode")]
    public void Incomplete_or_malformed_material_refuses_instead_of_silently_losing_interest(string defect)
    {
        var bindings = Bindings("fixed", false);
        switch (defect)
        {
            case "missing-name": bindings.Remove(nameof(ReceiveMessage.MessageName)); break;
            case "null-name": bindings[nameof(ReceiveMessage.MessageName)] = Literal(nameof(ReceiveMessage.MessageName), null); break;
            case "number-name": bindings[nameof(ReceiveMessage.MessageName)] = Literal(nameof(ReceiveMessage.MessageName), 42); break;
            case "missing-mode": bindings.Remove(nameof(ReceiveMessage.CanStartWorkflow)); break;
            case "dynamic-mode": bindings[nameof(ReceiveMessage.CanStartWorkflow)] = Expression(nameof(ReceiveMessage.CanStartWorkflow)); break;
            case "nonbool-mode": bindings[nameof(ReceiveMessage.CanStartWorkflow)] = Literal(nameof(ReceiveMessage.CanStartWorkflow), "false"); break;
            default:
                bindings[nameof(ReceiveMessage.MessageName)] = Expression(nameof(ReceiveMessage.MessageName));
                if (defect == "dynamic-no-declaration") break;
                if (defect == "dynamic-expression-declaration") bindings[nameof(ReceiveMessage.DeclaredMessageNames)] = Expression(nameof(ReceiveMessage.DeclaredMessageNames));
                else if (defect == "invalid-unicode") bindings[nameof(ReceiveMessage.DeclaredMessageNames)] =
                    Literal(nameof(ReceiveMessage.DeclaredMessageNames), JsonDocument.Parse("[\"\\uD800\"]").RootElement.Clone());
                else bindings[nameof(ReceiveMessage.DeclaredMessageNames)] = Literal(nameof(ReceiveMessage.DeclaredMessageNames), defect switch
                {
                    "duplicate" => new[] { "same", "same" }, "too-many" => Enumerable.Range(0, 33).Select(i => "name" + i).ToArray(),
                    "too-long" => new[] { new string('é', 129) }, _ => Array.Empty<string>()
                });
                break;
        }
        Assert.Throws<ArgumentException>(() => provider.Describe(Material(Node("receiver", bindings: bindings))));
    }

    [Theory]
    [InlineData(nameof(ReceiveMessage.MessageName))]
    [InlineData(nameof(ReceiveMessage.DeclaredMessageNames))]
    [InlineData(nameof(ReceiveMessage.CanStartWorkflow))]
    public void Secret_reads_cannot_be_treated_as_missing_literal_declarations(string key)
    {
        var bindings = Bindings("name", false); bindings[key] = SecretBindingTestSupport.SecretRead(key);
        SecretBindingTestSupport.AssertReaderRefusesSecretRead(() => provider.Describe(Material(Node("receiver", bindings: bindings))), "receiver", key);
    }

    [Fact]
    public void Aggregate_bound_refuses_the_whole_material_instead_of_truncating()
    {
        var names = JsonSerializer.SerializeToElement(Enumerable.Range(0, 32).Select(i => "name" + i).ToArray());
        var children = Enumerable.Range(0, 33).Select(i =>
        {
            var bindings = Bindings(null, false, names); bindings[nameof(ReceiveMessage.MessageName)] = Expression(nameof(ReceiveMessage.MessageName));
            return Node("receiver" + i, bindings: bindings);
        }).ToArray();
        Assert.Throws<ArgumentException>(() => provider.Describe(Material(Node("root", type: "other", children: children))));
    }

    [Fact]
    public void Event_and_model_material_are_not_reclassified_and_the_existing_feature_registers_no_Message_provider()
    {
        Assert.Empty(provider.Describe(Material(Node("event", "broadcast", false, type: Event.ActivityType))).Declarations);
        var model = new RuntimeModelMaterial(1, Pin(), new("model", 1), "profile", JsonSerializer.SerializeToElement(new { }), new(1, []), [], [], []);
        Assert.False(provider.Describe(new(model, "incarnation")).IsRecognized);
        var services = new ServiceCollection(); new ActivitiesPrimitivesFeature().ConfigureServices(services);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IMessageMaterialInterestProvider));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IActivityTriggerStimulusProvider) && descriptor.ImplementationType == typeof(EventTriggerStimulusProvider));
    }

    private static RuntimeExecutionMaterial Material(ExecutableNode root) => new(new WorkflowExecutable(Pin(), root,
        new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UnixEpoch, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference), "incarnation");
    private static WorkflowExecutableIdentity Pin() => new("artifact", "definition", "version", "1", "hash");
    private static ExecutableNode Node(string id, string? name = "name", bool start = false, JsonElement? declared = null,
        string? type = null, IReadOnlyCollection<ExecutableNode>? children = null, Dictionary<string, string>? metadata = null,
        Dictionary<string, RuntimeInputBinding>? bindings = null) =>
        new(id, id, type ?? ReceiveMessage.ActivityType, "1.0.0", "test", JsonSerializer.SerializeToElement(new { }),
            bindings ?? Bindings(name, start, declared), metadata ?? new(), childSlots: children is null ? [] : [new("children", children)]);
    private static Dictionary<string, RuntimeInputBinding> Bindings(string? name, bool start, JsonElement? declared = null)
    {
        var result = new Dictionary<string, RuntimeInputBinding> { [nameof(ReceiveMessage.CanStartWorkflow)] = Literal(nameof(ReceiveMessage.CanStartWorkflow), start) };
        if (name is not null) result[nameof(ReceiveMessage.MessageName)] = Literal(nameof(ReceiveMessage.MessageName), name);
        if (declared is not null) result[nameof(ReceiveMessage.DeclaredMessageNames)] = Literal(nameof(ReceiveMessage.DeclaredMessageNames), declared.Value);
        return result;
    }
    private static RuntimeInputBinding Literal(string key, object? value)
    {
        var type = new ValueTypeDescriptor(value is bool ? "Boolean" : "Object");
        return new(key, type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, value is JsonElement json ? json : JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
    }
    private static RuntimeInputBinding Expression(string key) => new(key, new("String"), ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.Expression, expression: new("JavaScript", "throw_if_evaluated()", new RuntimeValueTypeDescriptor("alias", "String", null),
            capabilityProfile: ExpressionCapabilityProfiles.BindingPureV1));
}
