using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Switch.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using SwitchActivity = Elsa.Activities.Switch.Activities.Switch;

namespace Elsa.Activities.Switch.Internal;

/// <summary>
/// Design-side handler that projects and compiles the <c>Switch</c> activity's per-case branch slots and
/// its <c>Default</c> branch slot. Lives in the activity module (which references
/// <c>Elsa.Workflows.Design.Core</c>); the runtime <c>Switch</c> activity class references no Design
/// types, preserving the runtime/design split.
/// </summary>
internal sealed class SwitchStructureHandler(string schemaVersion = SwitchActivity.StructureSchemaVersion) : IActivityStructureHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // Authored ArgumentState.Conversion enums (AuthoredValueConversionMode) arrive as camelCase
        // strings from the global web JSON options; nested structure payload reads must match.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Kind => SwitchActivity.StructureKind;

    public string SchemaVersion => schemaVersion;

    public Type AuthoredPayloadType => typeof(SwitchAuthoredStructure);

    public ActivityStructureInputProjection ProjectInputs(ActivityNode activity)
    {
        if (SchemaVersion == SwitchActivity.LegacyStructureSchemaVersion)
            return ActivityStructureInputProjection.Empty;
        var structure = ReadAuthoredStructure(activity);
        var inputs = new List<ActivityStructureInput>();
        foreach (var item in structure.Cases.Where(item => item.Condition is not null && !string.IsNullOrWhiteSpace(item.CaseId)))
        {
            var key = SwitchActivity.ConditionInputKey(item.CaseId!);
            var definition = new InputDefinition(key, $"{item.Match} condition", new TypeReference("Boolean"), null,
                "Condition", "General", IsNullable: false, Description: "Run this case when its condition is true.",
                DefaultValue: JsonSerializer.SerializeToElement(false), DefaultSyntax: "Literal");
            // A present container remains a predicate when cleared. Lower its unbound/null value to
            // the Boolean default without mutating the authored payload or its storage policy.
            var state = item.Condition!;
            if (state.Value?.Value is null or JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined })
                state = state with { Value = new(false, "Literal") };
            inputs.Add(new(key, definition, state));
        }
        if (structure.Cases.Any(item => item.Condition is null))
            inputs.Add(new(nameof(SwitchActivity.Value), null,
                activity.Inputs.FirstOrDefault(input => input.ReferenceKey == nameof(SwitchActivity.Value))));
        return new(inputs, [nameof(SwitchActivity.Value)]);
    }

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var structure = ReadAuthoredStructure(activity);
        var projections = structure.Cases
            .Select(@case => new ActivityChildProjection(SwitchActivity.CaseSlotName(@case.Match), ToBranch(@case.Activity)))
            .ToList();
        projections.Add(new ActivityChildProjection(SwitchActivity.DefaultSlotName, ToBranch(structure.Default)));
        return projections;
    }

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        var current = ReadAuthoredStructure(activity);
        var payload = activity.Structure is null ? new JsonObject() : JsonNode.Parse(activity.Structure.Payload.GetRawText()) as JsonObject
            ?? throw new ArgumentException("Switch authored structure must be an object.");
        var cases = payload["cases"] as JsonArray ?? new JsonArray();
        foreach (var (item, index) in current.Cases.Select((item, index) => (item, index)))
        {
            var branch = SingleBranch(childProjections, SwitchActivity.CaseSlotName(item.Match));
            if (cases[index] is JsonObject record) record["activity"] = JsonSerializer.SerializeToNode(branch, SerializerOptions);
        }
        if (payload["cases"] is null) payload["cases"] = cases;
        payload["default"] = JsonSerializer.SerializeToNode(SingleBranch(childProjections, SwitchActivity.DefaultSlotName), SerializerOptions);

        return activity with
        {
            Structure = new ActivityNodeStructure(
                SwitchActivity.StructureKind,
                SchemaVersion,
                JsonSerializer.SerializeToElement(payload, SerializerOptions))
        };
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        var authoredStructure = ReadAuthoredStructure(activity);
        if (SwitchCaseRules.StructureErrors(activity, authoredStructure).FirstOrDefault() is { } structureError)
            throw new ArgumentException(structureError);

        // Backstop: reject duplicate case match values at compile time. The design-time
        // SwitchDuplicateCaseValidator already surfaces these as Draft validation errors (which the
        // promotion gate blocks on), so a sanctioned publish never reaches here with duplicates; this
        // guards paths that bypass Draft validation by turning a duplicate into a compile-time
        // WorkflowExecutableCompilationException (the compiler wraps ArgumentException) before it can reach
        // an executable structure.
        var duplicateMatch = SwitchCaseRules.DuplicateMatches(authoredStructure.Cases.Select(@case => @case.Match)).FirstOrDefault();
        if (duplicateMatch is not null)
            throw new ArgumentException($"Switch activity node '{activity.NodeId}' declares duplicate case match value '{duplicateMatch}'. Each Switch case must have a unique match value.");

        var executableStructure = new SwitchExecutableStructure(
            authoredStructure.Cases
                .Select(@case => new SwitchExecutableCase(@case.Match, @case.Activity?.NodeId)
                { ConditionInputKey = @case.Condition is null ? null : SwitchActivity.ConditionInputKey(@case.CaseId!) })
                .ToArray(),
            authoredStructure.Default?.NodeId);

        return new ActivityNodeStructure(
            SwitchActivity.StructureKind,
            SchemaVersion,
            JsonSerializer.SerializeToElement(executableStructure, SerializerOptions));
    }

    public ActivityNodeStructure RemapExecutableStructure(
        ActivityNodeStructure structure,
        IReadOnlyDictionary<string, string> authoredToExecutableNodeIds)
    {
        var executable = structure.Payload.Deserialize<SwitchExecutableStructure>(SerializerOptions)
                         ?? throw new InvalidOperationException("Switch executable structure payload is invalid.");
        var remapped = new SwitchExecutableStructure(
            executable.Cases
                .Select(@case => new SwitchExecutableCase(@case.Match, Remap(@case.Activity, authoredToExecutableNodeIds))
                { ConditionInputKey = @case.ConditionInputKey })
                .ToArray(),
            Remap(executable.Default, authoredToExecutableNodeIds));
        return new ActivityNodeStructure(Kind, SchemaVersion, JsonSerializer.SerializeToElement(remapped, SerializerOptions));
    }

    private static IEnumerable<ActivityNode> ToBranch(ActivityNode? branch) =>
        branch is null ? [] : [branch];

    private static ActivityNode? SingleBranch(IReadOnlyCollection<ActivityChildProjection> childProjections, string slotName)
    {
        var slot = childProjections.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, slotName));
        return slot?.Activities.FirstOrDefault();
    }

    private static SwitchAuthoredStructure ReadAuthoredStructure(ActivityNode activity) =>
        SwitchAuthoredStructureReader.Read(activity);

    private static string? Remap(string? nodeId, IReadOnlyDictionary<string, string> nodeIds) =>
        nodeId is not null && nodeIds.TryGetValue(nodeId, out var executableNodeId) ? executableNodeId : nodeId;
}
