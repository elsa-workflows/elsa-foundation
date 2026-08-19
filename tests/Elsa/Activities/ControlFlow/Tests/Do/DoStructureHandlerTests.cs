using System.Text.Json;
using Elsa.Activities.Do;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Activities.ControlFlow;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DoActivity = Elsa.Activities.Do.Activities.Do;

namespace Elsa.Activities.Do.Tests;

public sealed class DoStructureHandlerTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureHandler _handler;

    public DoStructureHandlerTests()
    {
        var services = new ServiceCollection();
        new ActivitiesControlFlowRuntimeFeature().ConfigureServices(services);
        new Elsa.Activities.ControlFlow.Design.ActivitiesControlFlowDesignFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _handler = Assert.Single(_provider.GetServices<IActivityStructureHandler>(), h => h.Kind == DoActivity.StructureKind);
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void Feature_RegistersDoStructureHandlerWithMatchingKindAndSchema()
    {
        Assert.Equal(DoActivity.StructureKind, _handler.Kind);
        Assert.Equal(DoActivity.StructureSchemaVersion, _handler.SchemaVersion);
    }

    [Fact]
    public void SupportsScopedVariables_IsFalse()
    {
        // Do is a condition-only loop, not a container scope; it owns no container-scoped variables.
        Assert.False(_handler.SupportsScopedVariables);
    }

    [Fact]
    public void ProjectChildren_ReturnsEmptyBodySlot_WhenNoStructure()
    {
        var projections = _handler.ProjectChildren(NewNode());

        var projection = Assert.Single(projections);
        Assert.Equal(DoActivity.BodySlotName, projection.Name);
        Assert.Empty(projection.Activities);
    }

    [Fact]
    public void ReplaceChildren_PlacesBodyBranch_AndRoundTripsThroughProjection()
    {
        var replaced = _handler.ReplaceChildren(NewNode(),
        [
            new ActivityChildProjection(DoActivity.BodySlotName, [NewBranchNode("branch-body")])
        ]);

        var projected = _handler.ProjectChildren(replaced);
        Assert.Equal("branch-body", Assert.Single(SlotActivities(projected, DoActivity.BodySlotName)).NodeId);
    }

    [Fact]
    public void ReplaceChildren_AllowsOmittingBody()
    {
        var replaced = _handler.ReplaceChildren(NewNode(), []);

        var projected = _handler.ProjectChildren(replaced);
        Assert.Empty(SlotActivities(projected, DoActivity.BodySlotName));
    }

    [Fact]
    public void CompileExecutableStructure_RecordsBodyNodeId()
    {
        var node = _handler.ReplaceChildren(NewNode(),
        [
            new ActivityChildProjection(DoActivity.BodySlotName, [NewBranchNode("branch-body")])
        ]);

        var compiled = _handler.CompileExecutableStructure(node);

        Assert.Equal(DoActivity.StructureKind, compiled.Kind);
        Assert.Equal(DoActivity.StructureSchemaVersion, compiled.SchemaVersion);
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
        new("node-do", "activity-version-do", [], []);

    private static ActivityNode NewBranchNode(string nodeId) =>
        new(nodeId, $"activity-version-{nodeId}", [], []);
}
