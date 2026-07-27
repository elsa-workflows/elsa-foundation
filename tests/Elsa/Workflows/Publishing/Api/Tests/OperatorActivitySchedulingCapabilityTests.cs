using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class OperatorActivitySchedulingCapabilityTests
{
    [Fact]
    public void Hash_changes_when_direct_child_operator_capability_changes()
    {
        var hasher = new WorkflowExecutableHasher();

        var withoutCapability = Parent(null);
        var withCapability = Parent(new OperatorActivitySchedulingCapability("example.policy", 1, JsonSerializer.SerializeToElement(new { mode = "exact" })));

        Assert.NotEqual(hasher.ComputeHash(withoutCapability), hasher.ComputeHash(withCapability));
    }

    [Fact]
    public void Capability_clones_configuration_and_requires_exact_identity()
    {
        using var document = JsonDocument.Parse("{\"mode\":\"exact\"}");
        var capability = new OperatorActivitySchedulingCapability("example.policy", 1, document.RootElement);

        Assert.Equal("example.policy", capability.PolicyKey);
        Assert.Equal(1, capability.SchemaVersion);
        Assert.Equal("{\"mode\":\"exact\"}", capability.Configuration.GetRawText());
        Assert.Throws<ArgumentOutOfRangeException>(() => new OperatorActivitySchedulingCapability("example.policy", 0, document.RootElement));
    }

    [Fact]
    public void ExecutableNode_json_round_trip_preserves_child_capability_identity_and_configuration()
    {
        var parent = Parent(new OperatorActivitySchedulingCapability("example.policy", 3, JsonSerializer.SerializeToElement(new { mode = "bounded", retry = false })));

        var roundTripped = JsonSerializer.Deserialize<ExecutableNode>(JsonSerializer.Serialize(parent));

        var capability = Assert.Single(roundTripped!.ChildSlots).OperatorSchedulingCapability;
        Assert.NotNull(capability);
        Assert.Equal("example.policy", capability.PolicyKey);
        Assert.Equal(3, capability.SchemaVersion);
        Assert.Equal("{\"mode\":\"bounded\",\"retry\":false}", capability.Configuration.GetRawText());
    }

    private static ExecutableNode Parent(OperatorActivitySchedulingCapability? capability)
    {
        var child = Node("child");
        return new ExecutableNode("parent", "parent", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), [new ExecutableChildSlot("children", [child], capability)]);
    }

    private static ExecutableNode Node(string id) => new(id, id, "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }),
        new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
}
