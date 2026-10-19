using System.Text.Json;
using Elsa.Activities.Compensation.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using CompensationScopeActivity = Elsa.Activities.Compensation.Activities.CompensationScope;

namespace Elsa.Activities.Compensation.Internal;

/// <summary>
/// Design-side handler that projects and compiles a <c>CompensationScope</c>'s two slots per step: the forward
/// activity and the optional compensation handler. Lives in the activity module so the runtime activity class
/// references no Design types.
/// </summary>
internal sealed class CompensationScopeStructureHandler : IActivityStructureHandler
{
    public string Kind => CompensationScopeActivity.StructureKind;

    public string SchemaVersion => CompensationScopeActivity.StructureSchemaVersion;

    public Type AuthoredPayloadType => typeof(CompensationScopeAuthoredStructure);

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var structure = CompensationScopeAuthoredStructureReader.Read(activity);
        var projections = new List<ActivityChildProjection>();
        foreach (var step in structure.Steps)
        {
            projections.Add(new ActivityChildProjection(CompensationScopeActivity.StepSlotName(step.Name), ToSlot(step.Activity)));
            projections.Add(new ActivityChildProjection(CompensationScopeActivity.CompensationSlotName(step.Name), ToSlot(step.Compensation)));
        }

        return projections;
    }

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        var current = CompensationScopeAuthoredStructureReader.Read(activity);
        var steps = current.Steps
            .Select(step => new CompensationScopeAuthoredStep(
                step.Name,
                SingleChild(childProjections, CompensationScopeActivity.StepSlotName(step.Name)),
                SingleChild(childProjections, CompensationScopeActivity.CompensationSlotName(step.Name))))
            .ToArray();

        return activity with
        {
            Structure = new ActivityNodeStructure(
                Kind,
                SchemaVersion,
                JsonSerializer.SerializeToElement(new CompensationScopeAuthoredStructure(steps), CompensationScopeAuthoredStructureReader.SerializerOptions))
        };
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        var authored = CompensationScopeAuthoredStructureReader.Read(activity);
        var names = authored.Steps.Select(step => step.Name).ToArray();

        // Backstops for paths that bypass Draft validation (CompensationScopeValidator): the compiler turns these
        // ArgumentExceptions into compilation failures before an invalid structure reaches an executable.
        if (CompensationStepRules.HasBlankName(names))
            throw new ArgumentException($"Compensation scope '{activity.NodeId}' has a step without a name. Every step must be named.");

        var duplicate = CompensationStepRules.DuplicateNames(names).FirstOrDefault();
        if (duplicate is not null)
            throw new ArgumentException($"Compensation scope '{activity.NodeId}' declares step '{duplicate}' more than once. Step names must be unique.");

        var missingActivity = authored.Steps.FirstOrDefault(step => step.Activity is null);
        if (missingActivity is not null)
            throw new ArgumentException($"Compensation scope '{activity.NodeId}' step '{missingActivity.Name}' has no activity.");

        var executable = new CompensationScopeExecutableStructure(
            authored.Steps
                .Select(step => new CompensationScopeExecutableStep(CompensationStepRules.Normalize(step.Name), step.Activity?.NodeId, step.Compensation?.NodeId))
                .ToArray());

        return new ActivityNodeStructure(Kind, SchemaVersion, JsonSerializer.SerializeToElement(executable, CompensationScopeAuthoredStructureReader.SerializerOptions));
    }

    public ActivityNodeStructure RemapExecutableStructure(
        ActivityNodeStructure structure,
        IReadOnlyDictionary<string, string> authoredToExecutableNodeIds)
    {
        var executable = structure.Payload.Deserialize<CompensationScopeExecutableStructure>(CompensationScopeAuthoredStructureReader.SerializerOptions)
                         ?? throw new InvalidOperationException("Compensation scope executable structure payload is invalid.");
        var remapped = new CompensationScopeExecutableStructure(
            executable.Steps
                .Select(step => new CompensationScopeExecutableStep(
                    step.Name,
                    Remap(step.Activity, authoredToExecutableNodeIds),
                    Remap(step.Compensation, authoredToExecutableNodeIds)))
                .ToArray());
        return new ActivityNodeStructure(Kind, SchemaVersion, JsonSerializer.SerializeToElement(remapped, CompensationScopeAuthoredStructureReader.SerializerOptions));
    }

    private static IEnumerable<ActivityNode> ToSlot(ActivityNode? child) => child is null ? [] : [child];

    private static ActivityNode? SingleChild(IReadOnlyCollection<ActivityChildProjection> childProjections, string slotName) =>
        childProjections.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, slotName))?.Activities.FirstOrDefault();

    private static string? Remap(string? nodeId, IReadOnlyDictionary<string, string> nodeIds) =>
        nodeId is not null && nodeIds.TryGetValue(nodeId, out var executableNodeId) ? executableNodeId : nodeId;
}
