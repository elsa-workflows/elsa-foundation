using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Tests.Infrastructure;

internal sealed class TestActivityStructureHandler : IActivityStructureHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public const string ActivitiesSlotName = "Activities";
    public const string StructureKind = "test.workflow-root.structure";
    public const string StructureSchemaVersion = "1.0.0";

    public string Kind => StructureKind;

    public string SchemaVersion => StructureSchemaVersion;

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var structure = ReadStructure(activity);
        return [new ActivityChildProjection(ActivitiesSlotName, structure.Activities)];
    }

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        var current = ReadStructure(activity);
        var slot = childProjections.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, ActivitiesSlotName));
        return activity with { Structure = CreateStructure(slot?.Activities ?? [], current.StartActivityNodeId, current.Variables) };
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        var structure = ReadStructure(activity);
        return CreateStructure([], structure.StartActivityNodeId, structure.Variables);
    }

    public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) =>
        ReadStructure(activity).Variables;

    public static ActivityNodeStructure CreateStructure(
        IEnumerable<ActivityNode> activities,
        string? startActivityNodeId = null,
        IReadOnlyCollection<VariableDefinition>? variables = null) =>
        new(
            StructureKind,
            StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new TestCompositeStructure(activities.ToArray(), startActivityNodeId, variables), SerializerOptions));

    private static TestCompositeStructure ReadStructure(ActivityNode activity)
    {
        if (activity.Structure is null)
            return new TestCompositeStructure();

        return activity.Structure.Payload.Deserialize<TestCompositeStructure>(SerializerOptions)
               ?? new TestCompositeStructure();
    }

    private sealed class TestCompositeStructure
    {
        [JsonConstructor]
        public TestCompositeStructure(
            IReadOnlyCollection<ActivityNode>? activities = null,
            string? startActivityNodeId = null,
            IReadOnlyCollection<VariableDefinition>? variables = null)
        {
            Activities = activities ?? [];
            StartActivityNodeId = startActivityNodeId;
            Variables = variables ?? [];
        }

        [JsonPropertyName("activities")]
        public IReadOnlyCollection<ActivityNode> Activities { get; }

        [JsonPropertyName("startActivityNodeId")]
        public string? StartActivityNodeId { get; }

        [JsonPropertyName("variables")]
        public IReadOnlyCollection<VariableDefinition> Variables { get; }
    }
}
