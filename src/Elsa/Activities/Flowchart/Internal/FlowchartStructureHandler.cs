using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Flowchart.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Internal;

internal sealed class FlowchartStructureHandler : IActivityStructureHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // Authored ArgumentState.Conversion enums (AuthoredValueConversionMode) arrive as camelCase
        // strings from the global FastEndpoints options; nested structure payload reads must match.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Kind => FlowchartActivity.StructureKind;

    public string SchemaVersion => FlowchartActivity.StructureSchemaVersion;

    public bool SupportsScopedVariables => true;

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var structure = ReadAuthoredStructure(activity);
        return [new ActivityChildProjection(FlowchartActivity.ActivitiesSlotName, structure.Activities)];
    }

    public IReadOnlyCollection<ActivityChildContractMemberUsage> ProjectChildContractMemberUsage(ActivityNode activity) =>
        ReadAuthoredStructure(activity).Connections
            .GroupBy(connection => connection.Source.NodeId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ActivityChildContractMemberUsage(
                group.Key,
                group.Select(connection => new ActivityContractMemberUsage(
                        "Outcome",
                        connection.Source.Port,
                        "Connected"))
                    .Distinct()
                    .OrderBy(usage => usage.ReferenceKey, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        var current = ReadAuthoredStructure(activity);
        var slot = childProjections.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, FlowchartActivity.ActivitiesSlotName));
        var activities = slot?.Activities.ToArray() ?? [];
        var updated = new FlowchartAuthoredStructure(
            activities,
            current.Connections,
            current.StartNodeId,
            current.NodeMetadata,
            current.ConnectionMetadata,
            current.Variables);

        return activity with
        {
            Structure = new ActivityNodeStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(updated, SerializerOptions))
        };
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        var authoredStructure = ReadAuthoredStructure(activity);
        var executableStructure = new FlowchartStructure(
            authoredStructure.Connections,
            authoredStructure.StartNodeId,
            authoredStructure.NodeMetadata,
            authoredStructure.ConnectionMetadata,
            authoredStructure.Variables);

        return new ActivityNodeStructure(
            FlowchartActivity.StructureKind,
            FlowchartActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(executableStructure, SerializerOptions));
    }

    public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) =>
        ReadAuthoredStructure(activity).Variables;

    private static FlowchartAuthoredStructure ReadAuthoredStructure(ActivityNode activity)
    {
        if (activity.Structure is null)
            return new FlowchartAuthoredStructure();

        return activity.Structure.Payload.Deserialize<FlowchartAuthoredStructure>(SerializerOptions)
               ?? new FlowchartAuthoredStructure();
    }
}
