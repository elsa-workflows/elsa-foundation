using System.Text.Json;
using Elsa.Activities.If;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Activities.ControlFlow;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IfActivity = Elsa.Activities.If.Activities.If;

namespace Elsa.Activities.If.Tests;

public sealed class IfStructureHandlerTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureHandler _handler;

    public IfStructureHandlerTests()
    {
        var services = new ServiceCollection();
        new ActivitiesControlFlowFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _handler = Assert.Single(_provider.GetServices<IActivityStructureHandler>(), h => h.Kind == IfActivity.StructureKind);
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void Feature_RegistersIfStructureHandlerWithMatchingKindAndSchema()
    {
        Assert.Equal(IfActivity.StructureKind, _handler.Kind);
        Assert.Equal(IfActivity.StructureSchemaVersion, _handler.SchemaVersion);
    }

    [Fact]
    public void SupportsScopedVariables_IsFalse()
    {
        // If is not a container scope; it owns no container-scoped variables.
        Assert.False(_handler.SupportsScopedVariables);
    }

    [Fact]
    public void ProjectChildren_ReturnsBothNamedSlots_EvenWhenEmpty()
    {
        var node = NewNode();

        var projections = _handler.ProjectChildren(node);

        Assert.Equal(2, projections.Count);
        Assert.Empty(SlotActivities(projections, IfActivity.ThenSlotName));
        Assert.Empty(SlotActivities(projections, IfActivity.ElseSlotName));
    }

    [Fact]
    public void ReplaceChildren_PlacesBranchesIntoMatchingSlots_AndRoundTripsThroughProjection()
    {
        var then = NewBranchNode("then-branch");
        var @else = NewBranchNode("else-branch");

        var replaced = _handler.ReplaceChildren(NewNode(),
        [
            new ActivityChildProjection(IfActivity.ThenSlotName, [then]),
            new ActivityChildProjection(IfActivity.ElseSlotName, [@else])
        ]);

        var projected = _handler.ProjectChildren(replaced);
        Assert.Equal("then-branch", Assert.Single(SlotActivities(projected, IfActivity.ThenSlotName)).NodeId);
        Assert.Equal("else-branch", Assert.Single(SlotActivities(projected, IfActivity.ElseSlotName)).NodeId);
    }

    [Fact]
    public void ReplaceChildren_AllowsOmittingABranch()
    {
        var replaced = _handler.ReplaceChildren(NewNode(),
        [
            new ActivityChildProjection(IfActivity.ThenSlotName, [NewBranchNode("then-only")])
        ]);

        var projected = _handler.ProjectChildren(replaced);
        Assert.Equal("then-only", Assert.Single(SlotActivities(projected, IfActivity.ThenSlotName)).NodeId);
        Assert.Empty(SlotActivities(projected, IfActivity.ElseSlotName));
    }

    [Fact]
    public void CompileExecutableStructure_RecordsBranchNodeIds()
    {
        var node = _handler.ReplaceChildren(NewNode(),
        [
            new ActivityChildProjection(IfActivity.ThenSlotName, [NewBranchNode("then-branch")]),
            new ActivityChildProjection(IfActivity.ElseSlotName, [NewBranchNode("else-branch")])
        ]);

        var compiled = _handler.CompileExecutableStructure(node);

        Assert.Equal(IfActivity.StructureKind, compiled.Kind);
        Assert.Equal(IfActivity.StructureSchemaVersion, compiled.SchemaVersion);
        using var document = JsonDocument.Parse(compiled.Payload.GetRawText());
        Assert.Equal("then-branch", document.RootElement.GetProperty("then").GetString());
        Assert.Equal("else-branch", document.RootElement.GetProperty("else").GetString());
    }

    [Fact]
    public void CompileExecutableStructure_OmitsAbsentBranch()
    {
        var node = _handler.ReplaceChildren(NewNode(),
        [
            new ActivityChildProjection(IfActivity.ThenSlotName, [NewBranchNode("then-only")])
        ]);

        var compiled = _handler.CompileExecutableStructure(node);

        using var document = JsonDocument.Parse(compiled.Payload.GetRawText());
        Assert.Equal("then-only", document.RootElement.GetProperty("then").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("else").ValueKind);
    }

    [Fact]
    public void RemapExecutableStructure_RewritesBothSemanticBranchIds()
    {
        var node = _handler.ReplaceChildren(NewNode(),
        [
            new ActivityChildProjection(IfActivity.ThenSlotName, [NewBranchNode("then-branch")]),
            new ActivityChildProjection(IfActivity.ElseSlotName, [NewBranchNode("else-branch")])
        ]);
        var compiled = _handler.CompileExecutableStructure(node);

        var remapped = _handler.RemapExecutableStructure(compiled, new Dictionary<string, string>
        {
            ["then-branch"] = "node-then",
            ["else-branch"] = "node-else"
        });

        using var document = JsonDocument.Parse(remapped.Payload.GetRawText());
        Assert.Equal("node-then", document.RootElement.GetProperty("then").GetString());
        Assert.Equal("node-else", document.RootElement.GetProperty("else").GetString());
    }

    private static IEnumerable<ActivityNode> SlotActivities(IReadOnlyCollection<ActivityChildProjection> projections, string slotName) =>
        projections.Single(slot => slot.Name == slotName).Activities;

    private static ActivityNode NewNode() =>
        new("node-if", "activity-version-if", [], []);

    private static ActivityNode NewBranchNode(string nodeId) =>
        new(nodeId, $"activity-version-{nodeId}", [], []);
}
