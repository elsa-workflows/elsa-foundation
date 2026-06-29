using System.Text.Json;
using Elsa.Activities.While;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Activities.ControlFlow;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.While.Tests;

public sealed class WhileStructureHandlerTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureHandler _handler;

    public WhileStructureHandlerTests()
    {
        var services = new ServiceCollection();
        new ActivitiesControlFlowFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _handler = Assert.Single(_provider.GetServices<IActivityStructureHandler>(), h => h.Kind == WhileActivity.StructureKind);
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void Feature_RegistersWhileStructureHandlerWithMatchingKindAndSchema()
    {
        Assert.Equal(WhileActivity.StructureKind, _handler.Kind);
        Assert.Equal(WhileActivity.StructureSchemaVersion, _handler.SchemaVersion);
    }

    [Fact]
    public void SupportsScopedVariables_IsFalse()
    {
        // While is a condition-only loop, not a container scope; it owns no container-scoped variables.
        Assert.False(_handler.SupportsScopedVariables);
    }

    [Fact]
    public void ProjectChildren_ReturnsEmptyBodySlot_WhenNoStructure()
    {
        var projections = _handler.ProjectChildren(NewNode());

        var projection = Assert.Single(projections);
        Assert.Equal(WhileActivity.BodySlotName, projection.Name);
        Assert.Empty(projection.Activities);
    }

    [Fact]
    public void ReplaceChildren_PlacesBodyBranch_AndRoundTripsThroughProjection()
    {
        var replaced = _handler.ReplaceChildren(NewNode(),
        [
            new ActivityChildProjection(WhileActivity.BodySlotName, [NewBranchNode("branch-body")])
        ]);

        var projected = _handler.ProjectChildren(replaced);
        Assert.Equal("branch-body", Assert.Single(SlotActivities(projected, WhileActivity.BodySlotName)).NodeId);
    }

    [Fact]
    public void ReplaceChildren_AllowsOmittingBody()
    {
        var replaced = _handler.ReplaceChildren(NewNode(), []);

        var projected = _handler.ProjectChildren(replaced);
        Assert.Empty(SlotActivities(projected, WhileActivity.BodySlotName));
    }

    [Fact]
    public void CompileExecutableStructure_RecordsBodyNodeId()
    {
        var node = _handler.ReplaceChildren(NewNode(),
        [
            new ActivityChildProjection(WhileActivity.BodySlotName, [NewBranchNode("branch-body")])
        ]);

        var compiled = _handler.CompileExecutableStructure(node);

        Assert.Equal(WhileActivity.StructureKind, compiled.Kind);
        Assert.Equal(WhileActivity.StructureSchemaVersion, compiled.SchemaVersion);
        using var document = JsonDocument.Parse(compiled.Payload.GetRawText());
        Assert.Equal("branch-body", document.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public void CompileExecutableStructure_OmitsAbsentBody()
    {
        var node = _handler.ReplaceChildren(NewNode(), []);

        var compiled = _handler.CompileExecutableStructure(node);

        using var document = JsonDocument.Parse(compiled.Payload.GetRawText());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("body").ValueKind);
    }

    private static IEnumerable<ActivityNode> SlotActivities(IReadOnlyCollection<ActivityChildProjection> projections, string slotName)
    {
        var slot = projections.FirstOrDefault(slot => slot.Name == slotName);
        return slot?.Activities ?? [];
    }

    private static ActivityNode NewNode() =>
        new("node-while", "activity-version-while", [], []);

    private static ActivityNode NewBranchNode(string nodeId) =>
        new(nodeId, $"activity-version-{nodeId}", [], []);
}
