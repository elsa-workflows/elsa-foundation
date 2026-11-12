using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class StructureInputHydrationTests
{
    [Fact]
    public void Consumed_structure_values_are_delivered_without_hydrating_or_requiring_unconsumed_CLR_properties()
    {
        var contract = Contract();
        var snapshot = Snapshot(contract);
        var activity = new Consumer();
        var hydrator = new ActivityInputHydrator();

        hydrator.Hydrate(activity, contract, snapshot);

        Assert.Equal("hello", activity.Message);
        Assert.True(activity.Inputs["first"].InlineValue!.Value.GetBoolean());
        Assert.Single(activity.Inputs);
        Assert.False(activity.Inputs.ContainsKey("later"));
        Assert.Throws<InvalidOperationException>(() => hydrator.Hydrate(activity, contract, snapshot));
    }

    [Fact]
    public void Structure_contracts_require_an_explicit_consumer()
    {
        var contract = Contract();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ActivityInputHydrator().Hydrate(new OrdinaryActivity(), contract, Snapshot(contract)));

        Assert.Contains(nameof(IRuntimeStructureInputConsumer), exception.Message);
    }

    private static ActivityContract Contract() => new(
        "test/structure", "2.0.0", "test", JsonSerializer.SerializeToElement(new { }),
        [
            new("message", "Message", new ValueTypeDescriptor("String"), false, false, false, null, ActivityValuePolicy.Default),
            new ActivityInputContract("first", "First", new ValueTypeDescriptor("Boolean"), false, false, false, null, ActivityValuePolicy.Default) { IsStructureInput = true },
            new ActivityInputContract("later", "Later", new ValueTypeDescriptor("Boolean"), false, false, false, null, ActivityValuePolicy.Default) { IsStructureInput = true }
        ],
        new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
        ["Done"], new ActivityActivationRequirement("test", "test/structure"));

    private static ActivityInputSnapshot Snapshot(ActivityContract contract) => new(
        "invocation", contract.SchemaFingerprint, "bindings",
        new Dictionary<string, ValueEnvelope>
        {
            ["message"] = ValueEnvelope.Inline(new ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement("hello"), ValueProtectionPolicy.InstanceInline),
            ["first"] = ValueEnvelope.Inline(new ValueTypeDescriptor("Boolean"), JsonSerializer.SerializeToElement(true), ValueProtectionPolicy.InstanceInline)
        }, DateTimeOffset.UnixEpoch);

    private class OrdinaryActivity : StructuralActivity
    {
        [ActivityInput(Key = "message")]
        public string? Message { get; set; }
    }

    private sealed class Consumer : OrdinaryActivity, IRuntimeStructureInputConsumer
    {
        public IReadOnlyDictionary<string, ValueEnvelope> Inputs { get; private set; } = new Dictionary<string, ValueEnvelope>();
        public void BindStructureInputs(IReadOnlyDictionary<string, ValueEnvelope> inputs) => Inputs = inputs;
    }
}
