using System.Text.Json;
using Elsa.Activities.ForEach;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Activities.ControlFlow;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;

namespace Elsa.Activities.ForEach.Tests;

public sealed class ForEachStructureHandlerTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureHandler _handler;

    public ForEachStructureHandlerTests()
    {
        var services = new ServiceCollection();
        new ActivitiesControlFlowRuntimeFeature().ConfigureServices(services);
        new Elsa.Activities.ControlFlow.Design.ActivitiesControlFlowDesignFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _handler = Assert.Single(_provider.GetServices<IActivityStructureHandler>(), h => h.Kind == ForEachActivity.StructureKind);
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void Feature_RegistersForEachStructureHandlerWithMatchingKindAndSchema()
    {
        Assert.Equal(ForEachActivity.StructureKind, _handler.Kind);
        Assert.Equal(ForEachActivity.StructureSchemaVersion, _handler.SchemaVersion);
    }

    [Fact]
    public void SupportsScopedVariables_IsFalse()
    {
        // ForEach is not a container scope; its per-iteration item/index live in a per-pass iteration scope.
        Assert.False(_handler.SupportsScopedVariables);
    }

    [Fact]
    public void ProjectChildren_ReturnsEmptyBodySlot_WhenNoStructure()
    {
        var projection = Assert.Single(_handler.ProjectChildren(NewNode()));
        Assert.Equal(ForEachActivity.BodySlotName, projection.Name);
        Assert.Empty(projection.Activities);
    }

    [Fact]
    public void ReplaceChildren_PlacesBody_AndRoundTripsThroughProjection()
    {
        var replaced = _handler.ReplaceChildren(NewNode(),
        [
            new ActivityChildProjection(ForEachActivity.BodySlotName, [NewBranchNode("body")])
        ]);

        var projected = _handler.ProjectChildren(replaced);
        Assert.Equal("body", Assert.Single(SlotActivities(projected, ForEachActivity.BodySlotName)).NodeId);
    }

    [Fact]
    public void ReplaceChildren_AllowsOmittingBody()
    {
        var replaced = _handler.ReplaceChildren(NewNode(), []);

        Assert.Empty(SlotActivities(_handler.ProjectChildren(replaced), ForEachActivity.BodySlotName));
    }

    [Fact]
    public void CompileExecutableStructure_RecordsBodyNodeId()
    {
        var node = _handler.ReplaceChildren(NewNode(),
        [
            new ActivityChildProjection(ForEachActivity.BodySlotName, [NewBranchNode("body")])
        ]);

        var compiled = _handler.CompileExecutableStructure(node);

        Assert.Equal(ForEachActivity.StructureKind, compiled.Kind);
        Assert.Equal(ForEachActivity.StructureSchemaVersion, compiled.SchemaVersion);
        using var document = JsonDocument.Parse(compiled.Payload.GetRawText());
        Assert.Equal("body", document.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public void CompileExecutableStructure_OmitsAbsentBody()
    {
        var compiled = _handler.CompileExecutableStructure(_handler.ReplaceChildren(NewNode(), []));

        using var document = JsonDocument.Parse(compiled.Payload.GetRawText());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("body").ValueKind);
    }

    private static IEnumerable<ActivityNode> SlotActivities(IReadOnlyCollection<ActivityChildProjection> projections, string slotName)
    {
        var slot = projections.FirstOrDefault(slot => slot.Name == slotName);
        return slot?.Activities ?? [];
    }

    private static ActivityNode NewNode() =>
        new("node-foreach", "activity-version-foreach", [], []);

    private static ActivityNode NewBranchNode(string nodeId) =>
        new(nodeId, $"activity-version-{nodeId}", [], []);
}
