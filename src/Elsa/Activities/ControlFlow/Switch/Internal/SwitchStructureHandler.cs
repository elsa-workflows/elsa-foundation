using System.Text.Json;
using Elsa.Activities.Switch.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using SwitchActivity = Elsa.Activities.Switch.Activities.Switch;

namespace Elsa.Activities.Switch.Internal;

/// <summary>
/// Design-side handler that projects and compiles the <c>Switch</c> activity's per-case branch slots and
/// its <c>Default</c> branch slot. Lives in the activity module (which references
/// <c>Elsa.Workflows.Design.Core</c>); the runtime <c>Switch</c> activity class references no Design
/// types, preserving the Elsa §E2.2 split.
/// </summary>
internal sealed class SwitchStructureHandler : IActivityStructureHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string Kind => SwitchActivity.StructureKind;

    public string SchemaVersion => SwitchActivity.StructureSchemaVersion;

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
        var cases = current.Cases
            .Select(@case => new SwitchAuthoredCase(@case.Match, SingleBranch(childProjections, SwitchActivity.CaseSlotName(@case.Match))))
            .ToArray();
        var @default = SingleBranch(childProjections, SwitchActivity.DefaultSlotName);
        var structure = new SwitchAuthoredStructure(cases, @default);

        return activity with
        {
            Structure = new ActivityNodeStructure(
                SwitchActivity.StructureKind,
                SwitchActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structure, SerializerOptions))
        };
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        var authoredStructure = ReadAuthoredStructure(activity);

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
                .Select(@case => new SwitchExecutableCase(@case.Match, @case.Activity?.NodeId))
                .ToArray(),
            authoredStructure.Default?.NodeId);

        return new ActivityNodeStructure(
            SwitchActivity.StructureKind,
            SwitchActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(executableStructure, SerializerOptions));
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
}
