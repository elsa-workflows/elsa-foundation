using DoActivity = Elsa.Activities.Do.Activities.Do;
using ForActivity = Elsa.Activities.For.Activities.For;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.ControlFlow.Loops;

/// <summary>
/// Identifies one loop activity to the shared <see cref="LoopNavigator"/> and <see cref="LoopStructureHandler"/>.
/// The loops differ only in when they run another pass, which their activity classes decide; their structure is
/// the same optional body in one named child slot, told apart by structure kind.
/// </summary>
internal sealed record LoopKind(string Name, string StructureKind, string StructureSchemaVersion, string BodySlotName)
{
    /// <summary>
    /// The iteration values each pass of the body reads (ADR 0028, spec 1222 FR-004a). Only ForEach and For build an
    /// iteration frame; While and Do have no iteration values.
    /// </summary>
    public IReadOnlyList<string> IterationVariableNames { get; init; } = [];

    /// <summary>The input that switches the index value off, when the loop has one (ForEach's <c>ExposeIndex</c>).</summary>
    public string? IndexSwitchInputKey { get; init; }

    /// <summary>The iteration value <see cref="IndexSwitchInputKey"/> switches off.</summary>
    public string? SwitchedIndexVariableName { get; init; }

    public static readonly LoopKind Do = new("Do", DoActivity.StructureKind, DoActivity.StructureSchemaVersion, DoActivity.BodySlotName);

    public static readonly LoopKind For = new("For", ForActivity.StructureKind, ForActivity.StructureSchemaVersion, ForActivity.BodySlotName)
    {
        IterationVariableNames = [ForActivity.IndexVariableName]
    };

    public static readonly LoopKind ForEach = new("ForEach", ForEachActivity.StructureKind, ForEachActivity.StructureSchemaVersion, ForEachActivity.BodySlotName)
    {
        IterationVariableNames = [ForEachActivity.CurrentItemVariableName, ForEachActivity.CurrentIndexVariableName],
        IndexSwitchInputKey = nameof(ForEachActivity.ExposeIndex),
        SwitchedIndexVariableName = ForEachActivity.CurrentIndexVariableName
    };

    public static readonly LoopKind While = new("While", WhileActivity.StructureKind, WhileActivity.StructureSchemaVersion, WhileActivity.BodySlotName);
}
