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
        _handler = Assert.Single(_provider.GetServices<IActivityStructureHandler>(), h =>
            h.Kind == SwitchActivity.StructureKind && h.SchemaVersion == SwitchActivity.StructureSchemaVersion);
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void Feature_RegistersSwitchStructureHandlerWithMatchingKindAndSchema()
    {
        Assert.Equal(SwitchActivity.StructureKind, _handler.Kind);
        Assert.Equal(SwitchActivity.StructureSchemaVersion, _handler.SchemaVersion);
    }

    [Fact]
    public void Projects_boolean_arguments_with_owner_derived_keys_and_retains_unbound_predicates()
    {
        var node = PredicateNode("a");
        var projection = _handler.ProjectInputs(node);
        Assert.Equal(["Value"], projection.ConsumedInputKeys);
        var input = Assert.Single(projection.Inputs);
        Assert.Equal(SwitchActivity.ConditionInputKey("a"), input.ReferenceKey);
        Assert.Equal("Boolean", input.Definition!.Type.Alias);
        Assert.NotNull(input.State);
        Assert.Equal(JsonValueKind.False, input.State.Value.Value!.Value.ValueKind);
        var compiled = _handler.CompileExecutableStructure(node);
        Assert.Equal(input.ReferenceKey, compiled.Payload.GetProperty("cases")[0].GetProperty("conditionInputKey").GetString());
    }

    [Theory]
    [InlineData("Literal", false)]
    [InlineData("JavaScript", false)]
    [InlineData("JavaScript", true)]
    [InlineData("Liquid", true)]
    public void Empty_editor_values_lower_to_false_without_reusing_a_previous_conversion(string syntax, bool portable)
    {
        var value = portable ? JsonSerializer.SerializeToElement(new { language = syntax, source = "", resultType = new { alias = "Boolean" }, parameters = new { }, options = new { }, capabilityProfile = "binding-readonly-v1" })
            : JsonSerializer.SerializeToElement("");
        var condition = new ArgumentState("foreign", new(value, syntax), null, null, null, null,
            new(AuthoredValueConversionMode.Profile, new("unused-profile", "1")));
        var node = NewNode() with { Structure = new(SwitchActivity.StructureKind, SwitchActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new { cases = new[] { new { caseId = "clear", match = "Approved", condition } } }, new JsonSerializerOptions(JsonSerializerDefaults.Web))) };
        var original = node.Structure!.Payload.GetRawText();
        var input = Assert.Single(_handler.ProjectInputs(node).Inputs);
        Assert.Equal(JsonValueKind.False, input.State!.Value.Value!.Value.ValueKind);
        Assert.Null(input.State.Conversion);
        Assert.Equal(original, node.Structure.Payload.GetRawText());
    }

    [Fact]
    public void Replacing_children_preserves_condition_identity_and_forward_compatible_members()
    {
        var node = PredicateNode("a");
        var replaced = _handler.ReplaceChildren(node, [new(SwitchActivity.CaseSlotName("Approved"), [NewBranchNode("child")])]);
        var payload = replaced.Structure!.Payload;
        Assert.Equal("a", payload.GetProperty("cases")[0].GetProperty("caseId").GetString());
        Assert.Equal("kept", payload.GetProperty("cases")[0].GetProperty("future").GetString());
        Assert.Equal("keep", payload.GetProperty("future").GetString());
        Assert.Equal(node.Structure!.Payload.GetProperty("cases")[0].GetProperty("condition").GetRawText(), payload.GetProperty("cases")[0].GetProperty("condition").GetRawText());
    }

    [Fact]
    public void Replacing_children_keeps_carried_condition_child_and_extension_tokens_exact()
    {
        const string raw = "{ \"OwnerKey\":\"Ren\\u00e9<+>\", \"N\":1.0, \"wide\":12345678901234567890123456789012345, \"x\":null }";
        using var authored = JsonDocument.Parse("""
            {"cases":[{"caseId":"a","match":"Approved","activity":null,
              "condition":{"referenceKey":"predicate","value":{"expressionType":"JavaScript","value":__RAW__}},
              "future":__RAW__}],"default":null,"future":__RAW__}
            """.Replace("__RAW__", raw, StringComparison.Ordinal));
        var node = NewNode() with { Structure = new(SwitchActivity.StructureKind, SwitchActivity.StructureSchemaVersion, authored.RootElement.Clone()) };
        var original = node.Structure.Payload.GetRawText();
        using var data = JsonDocument.Parse(raw);
        var child = NewBranchNode("child") with
        {
            Inputs = [new ArgumentState("data", new(data.RootElement.Clone(), "Literal"), null, null, null, null)]
        };
        var replaced = _handler.ReplaceChildren(node,
            [new(SwitchActivity.CaseSlotName("Approved"), [child]), new(SwitchActivity.DefaultSlotName, [child])]);
        var payload = replaced.Structure!.Payload;
        var item = payload.GetProperty("cases")[0];
        Assert.Equal(raw, item.GetProperty("condition").GetProperty("value").GetProperty("value").GetRawText());
        Assert.Equal(raw, item.GetProperty("future").GetRawText());
        Assert.Equal(raw, payload.GetProperty("future").GetRawText());
        Assert.Equal(raw, item.GetProperty("activity").GetProperty("inputs")[0].GetProperty("value").GetProperty("value").GetRawText());
        Assert.Equal(raw, payload.GetProperty("default").GetProperty("inputs")[0].GetProperty("value").GetProperty("value").GetRawText());
        Assert.Equal(original, node.Structure.Payload.GetRawText());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Refuses_empty_case_identity(string id) => Assert.Throws<ArgumentException>(() => _handler.CompileExecutableStructure(PredicateNode(id)));

    [Fact]
    public void Legacy_handler_keeps_literal_matching_and_refuses_predicates()
    {
        var legacy = Assert.Single(_provider.GetServices<IActivityStructureHandler>(), h => h.Kind == SwitchActivity.StructureKind && h.SchemaVersion == SwitchActivity.LegacyStructureSchemaVersion);
        var predicate = PredicateNode("a") with { Structure = new ActivityNodeStructure(SwitchActivity.StructureKind, SwitchActivity.LegacyStructureSchemaVersion, PredicateNode("a").Structure!.Payload) };
        Assert.Throws<ArgumentException>(() => legacy.CompileExecutableStructure(predicate));
        Assert.Empty(legacy.ProjectInputs(predicate).Inputs);
    }

    private static ActivityNode PredicateNode(string id) => NewNode() with
    {
        Structure = new(SwitchActivity.StructureKind, SwitchActivity.StructureSchemaVersion, JsonSerializer.SerializeToElement(new
        {
            cases = new[] { new { caseId = id, match = "Approved", activity = (object?)null, condition = new { referenceKey = "ignored" }, future = "kept" } },
            @default = (object?)null, future = "keep"
        }))
    };

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
    public void CompileExecutableStructure_RejectsDuplicateCaseMatchValues()
    {
        // Duplicate match values must be surfaced before execution (#291). The runtime SwitchNavigator
        // only rejects them when the workflow runs; compilation rejects them at the design/compile stage
        // so they never reach a published executable structure.
        var node = SeedCases("a", "a");

        var exception = Assert.Throws<ArgumentException>(() => _handler.CompileExecutableStructure(node));
        Assert.Contains("duplicate case match value 'a'", exception.Message);
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
            cases = matches.Select((match, index) => new { match, caseId = $"case-{index}" }).ToArray()
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
