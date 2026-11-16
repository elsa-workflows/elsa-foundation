using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeStructureInputMaterializationTests
{
    private static readonly ValueTypeDescriptor BooleanType = new("Boolean");

    [Fact]
    public async Task First_true_pins_consumed_values_without_evaluating_a_later_expression()
    {
        var policy = new FirstTruePolicy("first", "later");
        var node = Node(Literal("first", true), Expression("later"));
        var materializer = new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), [policy]);

        var snapshot = await materializer.MaterializeSnapshotAsync(node, "invocation", Context(), DateTimeOffset.UnixEpoch);

        Assert.True(snapshot.Values["first"].InlineValue!.Value.GetBoolean());
        Assert.False(snapshot.Values.ContainsKey("later"));
        Assert.Equal(new[] { "first" }, policy.ReadKeys);
    }

    [Fact]
    public async Task False_inputs_are_pinned_in_policy_order_until_the_first_true()
    {
        var policy = new FirstTruePolicy("z-first", "a-second", "later");
        var node = Node(Literal("z-first", false), Literal("a-second", true), Expression("later"));
        var materializer = new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), [policy]);

        var snapshot = await materializer.MaterializeSnapshotAsync(node, "invocation", Context(), DateTimeOffset.UnixEpoch);

        Assert.False(snapshot.Values["z-first"].InlineValue!.Value.GetBoolean());
        Assert.True(snapshot.Values["a-second"].InlineValue!.Value.GetBoolean());
        Assert.Equal(2, snapshot.Values.Count);
        Assert.Equal(new[] { "z-first", "a-second" }, policy.ReadKeys);
    }

    [Fact]
    public async Task Structure_inputs_require_their_owning_policy()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeActivityInputMaterializer().MaterializeSnapshotAsync(
                Node(Literal("condition", true)), "invocation", Context(), DateTimeOffset.UnixEpoch).AsTask());

        Assert.Contains("test.conditions@2.0.0", exception.Message);
    }

    [Fact]
    public void Duplicate_policy_ownership_is_refused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), [new FirstTruePolicy(), new FirstTruePolicy()]));

        Assert.Contains("test.conditions@2.0.0", exception.Message);
    }

    [Fact]
    public void Structure_input_contract_round_trips_with_its_fingerprint_and_old_contracts_omit_the_flag()
    {
        var contract = Node(Literal("condition", true)).ActivityContract!;
        var json = JsonSerializer.Serialize(contract);
        var roundTripped = JsonSerializer.Deserialize<ActivityContract>(json)!;

        Assert.True(roundTripped.Inputs["condition"].IsStructureInput);
        Assert.Equal(contract.SchemaFingerprint, roundTripped.SchemaFingerprint);
        var ordinary = new ActivityInputContract("value", "Value", BooleanType, false, false, false, null, ActivityValuePolicy.Default);
        Assert.DoesNotContain("IsStructureInput", JsonSerializer.Serialize(ordinary));
    }

    private static RuntimeInputBinding Literal(string key, bool value) => new(
        key, BooleanType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
        literal: ValueEnvelope.Inline(BooleanType, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));

    private static RuntimeInputBinding Expression(string key) => new(
        key, BooleanType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
        expression: new RuntimeExpressionBinding("JavaScript", "unavailableEvaluator()"));

    private static RuntimeInputBindingResolutionContext Context() => new("workflow", "invocation");

    private static ExecutableNode Node(params RuntimeInputBinding[] bindings)
    {
        var payload = JsonSerializer.SerializeToElement(new { });
        var contract = new ActivityContract(
            "test/conditions", "2.0.0", "test", payload,
            bindings.Select(binding => new ActivityInputContract(
                binding.InputKey, binding.InputKey, BooleanType, false, false, false, null, ActivityValuePolicy.Default)
            { IsStructureInput = true }),
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            ["Done"], new ActivityActivationRequirement("test", "test/conditions"));
        return new ExecutableNode(
            "conditions", "conditions", "test/conditions", "2.0.0", "test", payload,
            bindings.ToDictionary(binding => binding.InputKey), new Dictionary<string, string>(),
            structure: new ExecutableActivityStructure("test.conditions", "2.0.0", payload),
            activityContract: contract);
    }

    private sealed class FirstTruePolicy(params string[] keys) : IRuntimeStructureInputMaterializationPolicy
    {
        public string Kind => "test.conditions";
        public string SchemaVersion => "2.0.0";
        public List<string> ReadKeys { get; } = [];

        public async ValueTask MaterializeAsync(IRuntimeStructureInputMaterializationContext context, CancellationToken cancellationToken)
        {
            foreach (var key in keys)
            {
                ReadKeys.Add(key);
                var value = await context.MaterializeAsync(key, cancellationToken);
                if (value.InlineValue!.Value.GetBoolean()) return;
            }
        }
    }
}
