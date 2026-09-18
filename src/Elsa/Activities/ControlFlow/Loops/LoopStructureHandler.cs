using System.Text.Json;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.ControlFlow.Loops;

/// <summary>
/// Design-side handler that projects and compiles a loop activity's single body slot, registered once per
/// <see cref="LoopKind"/>. Lives in the activity module (which references <c>Elsa.Workflows.Design.Core</c>); the
/// runtime loop activity classes reference no Design types, preserving the Elsa §E2.2 split.
/// </summary>
internal sealed class LoopStructureHandler(LoopKind loop) : IActivityStructureHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // Authored ArgumentState.Conversion enums (AuthoredValueConversionMode) arrive as camelCase
        // strings from the global web JSON options; nested structure payload reads must match.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Kind => loop.StructureKind;

    public string SchemaVersion => loop.StructureSchemaVersion;

    public Type AuthoredPayloadType => typeof(LoopAuthoredStructure);

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) =>
        [new ActivityChildProjection(loop.BodySlotName, ReadAuthoredStructure(activity).Body is { } body ? [body] : [])];

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        var slot = childProjections.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, loop.BodySlotName));
        return activity with { Structure = ToStructure(new LoopAuthoredStructure(slot?.Activities.FirstOrDefault())) };
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity) =>
        ToStructure(new LoopExecutableStructure(ReadAuthoredStructure(activity).Body?.NodeId));

    public ActivityNodeStructure RemapExecutableStructure(
        ActivityNodeStructure structure,
        IReadOnlyDictionary<string, string> authoredToExecutableNodeIds)
    {
        var executable = structure.Payload.Deserialize<LoopExecutableStructure>(SerializerOptions)
                         ?? throw new InvalidOperationException($"{loop.Name} executable structure payload is invalid.");
        var remapped = executable.Body is not null && authoredToExecutableNodeIds.TryGetValue(executable.Body, out var executableNodeId)
            ? executableNodeId
            : executable.Body;
        return ToStructure(new LoopExecutableStructure(remapped));
    }

    private ActivityNodeStructure ToStructure<TPayload>(TPayload payload) =>
        new(loop.StructureKind, loop.StructureSchemaVersion, JsonSerializer.SerializeToElement(payload, SerializerOptions));

    private static LoopAuthoredStructure ReadAuthoredStructure(ActivityNode activity) =>
        activity.Structure?.Payload.Deserialize<LoopAuthoredStructure>(SerializerOptions) ?? new LoopAuthoredStructure();
}
