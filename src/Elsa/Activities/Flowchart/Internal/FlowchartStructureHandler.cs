using System.Text.Json;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Internal;

internal sealed class FlowchartStructureHandler : IActivityStructureHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string Kind => FlowchartActivity.StructureKind;

    public string SchemaVersion => FlowchartActivity.StructureSchemaVersion;

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var structure = ReadAuthoredStructure(activity);
        return [new ActivityChildProjection(FlowchartActivity.ActivitiesSlotName, structure.Activities)];
    }

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        var current = ReadAuthoredStructure(activity);
        var slot = childProjections.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, FlowchartActivity.ActivitiesSlotName));
        var activities = slot?.Activities.ToArray() ?? [];
        var updated = new FlowchartAuthoredStructure(activities, current.Connections, current.StartNodeId);

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
        var executableStructure = new FlowchartStructure(authoredStructure.Connections, authoredStructure.StartNodeId);

        return new ActivityNodeStructure(
            FlowchartActivity.StructureKind,
            FlowchartActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(executableStructure, SerializerOptions));
    }

    private static FlowchartAuthoredStructure ReadAuthoredStructure(ActivityNode activity)
    {
        if (activity.Structure is null)
            return new FlowchartAuthoredStructure();

        return activity.Structure.Payload.Deserialize<FlowchartAuthoredStructure>(SerializerOptions)
               ?? new FlowchartAuthoredStructure();
    }
}
