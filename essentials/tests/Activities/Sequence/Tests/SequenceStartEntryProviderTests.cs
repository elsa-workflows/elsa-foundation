using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence.Internal;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Sequence.Tests;

/// <summary>
/// Spec 238, the start-path rule for a Sequence: every run begins at the first child in structure order, whichever
/// trigger started it.
/// </summary>
public sealed class SequenceStartEntryProviderTests
{
    private readonly SequenceStartEntryProvider _provider = new();

    [Fact]
    public void AnyTrigger_EntersAtTheFirstChildInStructureOrder()
    {
        var sequence = NewSequenceNode([NewNode("node-a"), NewNode("node-b")], orderedChildIds: ["node-b", "node-a"]);

        Assert.Equal("node-b", _provider.SelectStartChild(sequence, "node-b")?.ExecutableNodeId);
        Assert.Equal("node-b", _provider.SelectStartChild(sequence, "node-a")?.ExecutableNodeId);
        Assert.Equal("node-b", _provider.SelectStartChild(sequence, "node-nested")?.ExecutableNodeId);
    }

    [Fact]
    public void EmptySequence_HasNoEntry()
    {
        Assert.Null(_provider.SelectStartChild(NewSequenceNode([]), "node-trigger"));
    }

    [Fact]
    public void MalformedStructure_IsReportedAsInvalidOperation()
    {
        var sequence = NewSequenceNode([NewNode("node-a")], orderedChildIds: ["node-missing"]);

        Assert.Throws<InvalidOperationException>(() => _provider.SelectStartChild(sequence, "node-a"));
    }

    [Fact]
    public void Feature_RegistersTheProviderForTheSequenceStructureKind()
    {
        var services = new ServiceCollection();
        new ActivitiesSequenceFeature().ConfigureServices(services);

        var provider = Assert.Single(services.BuildServiceProvider().GetServices<IStructuralStartEntryProvider>());
        Assert.Equal(SequenceActivity.StructureKind, provider.StructureKind);
    }

    private static ExecutableNode NewSequenceNode(IReadOnlyCollection<ExecutableNode> children, IReadOnlyCollection<string>? orderedChildIds = null) =>
        new(
            executableNodeId: "node-sequence",
            authoredActivityId: "authored-node-sequence",
            activityType: "sequence",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = orderedChildIds ?? children.Select(child => child.ExecutableNodeId).ToArray() })));

    private static ExecutableNode NewNode(string nodeId) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/probe",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());
}
