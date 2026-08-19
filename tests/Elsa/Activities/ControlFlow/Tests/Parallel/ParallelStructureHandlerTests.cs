using System.Text.Json;
using Elsa.Activities.Parallel;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Activities.ControlFlow;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ParallelActivity = Elsa.Activities.Parallel.Activities.Parallel;

namespace Elsa.Activities.Parallel.Tests;

public sealed class ParallelStructureHandlerTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureHandler _handler;

    public ParallelStructureHandlerTests()
    {
        var services = new ServiceCollection();
        new ActivitiesControlFlowRuntimeFeature().ConfigureServices(services);
        new Elsa.Activities.ControlFlow.Design.ActivitiesControlFlowDesignFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _handler = Assert.Single(_provider.GetServices<IActivityStructureHandler>(), h => h.Kind == ParallelActivity.StructureKind);
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void Feature_RegistersParallelStructureHandlerWithMatchingKindAndSchema()
    {
        Assert.Equal(ParallelActivity.StructureKind, _handler.Kind);
        Assert.Equal(ParallelActivity.StructureSchemaVersion, _handler.SchemaVersion);
    }

    [Fact]
    public void SupportsScopedVariables_IsFalse()
    {
        // Parallel is not a container scope; it owns no container-scoped variables.
        Assert.False(_handler.SupportsScopedVariables);
    }

    [Fact]
    public void ProjectChildren_ReturnsNoSlots_WhenNoStructure()
    {
        var projections = _handler.ProjectChildren(NewNode());

        Assert.Empty(projections);
    }

    [Fact]
    public void ReplaceChildren_PlacesBranches_AndRoundTripsThroughProjection()
    {
        var seeded = SeedBranches("a", "b");

        var replaced = _handler.ReplaceChildren(seeded,
        [
            new ActivityChildProjection(ParallelActivity.BranchSlotName("a"), [NewBranchNode("branch-a")]),
            new ActivityChildProjection(ParallelActivity.BranchSlotName("b"), [NewBranchNode("branch-b")])
        ]);

        var projected = _handler.ProjectChildren(replaced);
        Assert.Equal("branch-a", Assert.Single(SlotActivities(projected, ParallelActivity.BranchSlotName("a"))).NodeId);
        Assert.Equal("branch-b", Assert.Single(SlotActivities(projected, ParallelActivity.BranchSlotName("b"))).NodeId);
    }

    [Fact]
    public void ReplaceChildren_AllowsOmittingABranchActivity()
    {
        var seeded = SeedBranches("a", "b");

        var replaced = _handler.ReplaceChildren(seeded,
        [
            new ActivityChildProjection(ParallelActivity.BranchSlotName("a"), [NewBranchNode("branch-a")])
        ]);

        var projected = _handler.ProjectChildren(replaced);
        Assert.Equal("branch-a", Assert.Single(SlotActivities(projected, ParallelActivity.BranchSlotName("a"))).NodeId);
        Assert.Empty(SlotActivities(projected, ParallelActivity.BranchSlotName("b")));
    }

    [Fact]
    public void CompileExecutableStructure_RecordsBranchNamesAndNodeIds_AndThreshold()
    {
        var node = _handler.ReplaceChildren(SeedBranches(["a", "b"], threshold: 1),
        [
            new ActivityChildProjection(ParallelActivity.BranchSlotName("a"), [NewBranchNode("branch-a")]),
            new ActivityChildProjection(ParallelActivity.BranchSlotName("b"), [NewBranchNode("branch-b")])
        ]);

        var compiled = _handler.CompileExecutableStructure(node);

        Assert.Equal(ParallelActivity.StructureKind, compiled.Kind);
        Assert.Equal(ParallelActivity.StructureSchemaVersion, compiled.SchemaVersion);
        using var document = JsonDocument.Parse(compiled.Payload.GetRawText());
        var branches = document.RootElement.GetProperty("branches");
        Assert.Equal("a", branches[0].GetProperty("name").GetString());
        Assert.Equal("branch-a", branches[0].GetProperty("activity").GetString());
        Assert.Equal("b", branches[1].GetProperty("name").GetString());
        Assert.Equal("branch-b", branches[1].GetProperty("activity").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("threshold").GetInt32());
    }

    [Fact]
    public void CompileExecutableStructure_OmitsAbsentBranchActivity()
    {
        var node = _handler.ReplaceChildren(SeedBranches("a"),
        [
            new ActivityChildProjection(ParallelActivity.BranchSlotName("a"), [])
        ]);

        var compiled = _handler.CompileExecutableStructure(node);

        using var document = JsonDocument.Parse(compiled.Payload.GetRawText());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("branches")[0].GetProperty("activity").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("threshold").ValueKind);
    }

    private static ActivityNode SeedBranches(params string[] names) => SeedBranches(names, threshold: null);

    private static ActivityNode SeedBranches(string[] names, int? threshold)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            branches = names.Select(name => new { name }).ToArray(),
            threshold
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return NewNode() with
        {
            Structure = new ActivityNodeStructure(
                ParallelActivity.StructureKind,
                ParallelActivity.StructureSchemaVersion,
                payload)
        };
    }

    private static IEnumerable<ActivityNode> SlotActivities(IReadOnlyCollection<ActivityChildProjection> projections, string slotName)
    {
        var slot = projections.FirstOrDefault(slot => slot.Name == slotName);
        return slot?.Activities ?? [];
    }

    private static ActivityNode NewNode() =>
        new("node-parallel", "activity-version-parallel", [], []);

    private static ActivityNode NewBranchNode(string nodeId) =>
        new(nodeId, $"activity-version-{nodeId}", [], []);
}
