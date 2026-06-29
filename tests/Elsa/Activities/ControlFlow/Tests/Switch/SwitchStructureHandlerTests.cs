using System.Text.Json;
using Elsa.Activities.Switch;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Activities.ControlFlow;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SwitchActivity = Elsa.Activities.Switch.Activities.Switch;

namespace Elsa.Activities.Switch.Tests;

public sealed class SwitchStructureHandlerTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureHandler _handler;

    public SwitchStructureHandlerTests()
    {
        var services = new ServiceCollection();
        new ActivitiesControlFlowFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _handler = Assert.Single(_provider.GetServices<IActivityStructureHandler>(), h => h.Kind == SwitchActivity.StructureKind);
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void Feature_RegistersSwitchStructureHandlerWithMatchingKindAndSchema()
    {
        Assert.Equal(SwitchActivity.StructureKind, _handler.Kind);
        Assert.Equal(SwitchActivity.StructureSchemaVersion, _handler.SchemaVersion);
    }

    [Fact]
    public void SupportsScopedVariables_IsFalse()
    {
        // Switch is not a container scope; it owns no container-scoped variables.
        Assert.False(_handler.SupportsScopedVariables);
    }

    [Fact]
    public void ProjectChildren_ReturnsOnlyDefaultSlot_WhenNoStructure()
    {
        var projections = _handler.ProjectChildren(NewNode());

        var projection = Assert.Single(projections);
        Assert.Equal(SwitchActivity.DefaultSlotName, projection.Name);
        Assert.Empty(projection.Activities);
    }

    [Fact]
    public void ReplaceChildren_PlacesCaseAndDefaultBranches_AndRoundTripsThroughProjection()
    {
        // Seed the authored cases (their match values) before placing branch children into the slots.
        var seeded = SeedCases("a", "b");

        var replaced = _handler.ReplaceChildren(seeded,
        [
            new ActivityChildProjection(SwitchActivity.CaseSlotName("a"), [NewBranchNode("branch-a")]),
            new ActivityChildProjection(SwitchActivity.CaseSlotName("b"), [NewBranchNode("branch-b")]),
            new ActivityChildProjection(SwitchActivity.DefaultSlotName, [NewBranchNode("branch-default")])
        ]);

        var projected = _handler.ProjectChildren(replaced);
        Assert.Equal("branch-a", Assert.Single(SlotActivities(projected, SwitchActivity.CaseSlotName("a"))).NodeId);
        Assert.Equal("branch-b", Assert.Single(SlotActivities(projected, SwitchActivity.CaseSlotName("b"))).NodeId);
        Assert.Equal("branch-default", Assert.Single(SlotActivities(projected, SwitchActivity.DefaultSlotName)).NodeId);
    }

    [Fact]
    public void ReplaceChildren_AllowsOmittingDefaultAndCaseBranches()
    {
        var seeded = SeedCases("a", "b");

        var replaced = _handler.ReplaceChildren(seeded,
        [
            new ActivityChildProjection(SwitchActivity.CaseSlotName("a"), [NewBranchNode("branch-a")])
        ]);

        var projected = _handler.ProjectChildren(replaced);
        Assert.Equal("branch-a", Assert.Single(SlotActivities(projected, SwitchActivity.CaseSlotName("a"))).NodeId);
        Assert.Empty(SlotActivities(projected, SwitchActivity.CaseSlotName("b")));
        Assert.Empty(SlotActivities(projected, SwitchActivity.DefaultSlotName));
    }

    [Fact]
    public void CompileExecutableStructure_RecordsCaseMatchesAndBranchNodeIds()
    {
        var node = _handler.ReplaceChildren(SeedCases("a", "b"),
        [
            new ActivityChildProjection(SwitchActivity.CaseSlotName("a"), [NewBranchNode("branch-a")]),
            new ActivityChildProjection(SwitchActivity.CaseSlotName("b"), [NewBranchNode("branch-b")]),
            new ActivityChildProjection(SwitchActivity.DefaultSlotName, [NewBranchNode("branch-default")])
        ]);

        var compiled = _handler.CompileExecutableStructure(node);

        Assert.Equal(SwitchActivity.StructureKind, compiled.Kind);
        Assert.Equal(SwitchActivity.StructureSchemaVersion, compiled.SchemaVersion);
        using var document = JsonDocument.Parse(compiled.Payload.GetRawText());
        var cases = document.RootElement.GetProperty("cases");
        Assert.Equal("a", cases[0].GetProperty("match").GetString());
        Assert.Equal("branch-a", cases[0].GetProperty("activity").GetString());
        Assert.Equal("b", cases[1].GetProperty("match").GetString());
        Assert.Equal("branch-b", cases[1].GetProperty("activity").GetString());
        Assert.Equal("branch-default", document.RootElement.GetProperty("default").GetString());
    }

    [Fact]
    public void CompileExecutableStructure_OmitsAbsentBranchAndDefault()
    {
        var node = _handler.ReplaceChildren(SeedCases("a"),
        [
            new ActivityChildProjection(SwitchActivity.CaseSlotName("a"), [NewBranchNode("branch-a")])
        ]);

        var compiled = _handler.CompileExecutableStructure(node);

        using var document = JsonDocument.Parse(compiled.Payload.GetRawText());
        Assert.Equal("branch-a", document.RootElement.GetProperty("cases")[0].GetProperty("activity").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("default").ValueKind);
    }

    // Seeds an authored structure with the given case match values (and no branches yet) by serializing
    // a cases array directly, since the handler only adds case slots for cases it already knows about.
    private static ActivityNode SeedCases(params string[] matches)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            cases = matches.Select(match => new { match }).ToArray()
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return NewNode() with
        {
            Structure = new ActivityNodeStructure(
                SwitchActivity.StructureKind,
                SwitchActivity.StructureSchemaVersion,
                payload)
        };
    }

    private static IEnumerable<ActivityNode> SlotActivities(IReadOnlyCollection<ActivityChildProjection> projections, string slotName)
    {
        var slot = projections.FirstOrDefault(slot => slot.Name == slotName);
        return slot?.Activities ?? [];
    }

    private static ActivityNode NewNode() =>
        new("node-switch", "activity-version-switch", [], []);

    private static ActivityNode NewBranchNode(string nodeId) =>
        new(nodeId, $"activity-version-{nodeId}", [], []);
}
