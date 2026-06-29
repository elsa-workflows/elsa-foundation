using System.Text.Json;
using Elsa.Activities.For;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ForActivity = Elsa.Activities.For.Activities.For;

namespace Elsa.Activities.For.Tests;

public sealed class ForStructureHandlerTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureHandler _handler;

    public ForStructureHandlerTests()
    {
        var services = new ServiceCollection();
        new ActivitiesForFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _handler = Assert.Single(_provider.GetServices<IActivityStructureHandler>());
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void Feature_RegistersForStructureHandlerWithMatchingKindAndSchema()
    {
        Assert.Equal(ForActivity.StructureKind, _handler.Kind);
        Assert.Equal(ForActivity.StructureSchemaVersion, _handler.SchemaVersion);
    }

    [Fact]
    public void SupportsScopedVariables_IsFalse()
    {
        // For is not a container scope; the per-iteration index comes from the loop scope factory, not a
        // container-scoped variable declaration.
        Assert.False(_handler.SupportsScopedVariables);
    }

    [Fact]
    public void ProjectChildren_ReturnsEmptyBodySlot_WhenNoStructure()
    {
        var projection = Assert.Single(_handler.ProjectChildren(NewNode()));
        Assert.Equal(ForActivity.BodySlotName, projection.Name);
        Assert.Empty(projection.Activities);
    }

    [Fact]
    public void ReplaceChildren_PlacesBody_AndRoundTripsThroughProjection()
    {
        var replaced = _handler.ReplaceChildren(NewNode(),
        [
            new ActivityChildProjection(ForActivity.BodySlotName, [NewBodyNode("body")])
        ]);

        var projected = _handler.ProjectChildren(replaced);
        Assert.Equal("body", Assert.Single(SlotActivities(projected, ForActivity.BodySlotName)).NodeId);
    }

    [Fact]
    public void ReplaceChildren_AllowsOmittingBody()
    {
        var replaced = _handler.ReplaceChildren(NewNode(), []);

        var projected = _handler.ProjectChildren(replaced);
        Assert.Empty(SlotActivities(projected, ForActivity.BodySlotName));
    }

    [Fact]
    public void CompileExecutableStructure_RecordsBodyNodeId()
    {
        var node = _handler.ReplaceChildren(NewNode(),
        [
            new ActivityChildProjection(ForActivity.BodySlotName, [NewBodyNode("body")])
        ]);

        var compiled = _handler.CompileExecutableStructure(node);

        Assert.Equal(ForActivity.StructureKind, compiled.Kind);
        Assert.Equal(ForActivity.StructureSchemaVersion, compiled.SchemaVersion);
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
        new("node-for", "activity-version-for", [], []);

    private static ActivityNode NewBodyNode(string nodeId) =>
        new(nodeId, $"activity-version-{nodeId}", [], []);
}
