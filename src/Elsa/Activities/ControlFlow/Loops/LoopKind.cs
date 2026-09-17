using DoActivity = Elsa.Activities.Do.Activities.Do;
using ForActivity = Elsa.Activities.For.Activities.For;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.ControlFlow.Loops;

/// <summary>
/// Identifies one loop activity to the shared <see cref="LoopNavigator"/> and <see cref="LoopStructureHandler"/>.
/// The loops differ only in when they run another pass, which their activity classes decide; their structure is
/// the same optional body in one named child slot (ADR 0028), told apart by structure kind.
/// </summary>
internal sealed record LoopKind(string Name, string StructureKind, string StructureSchemaVersion, string BodySlotName)
{
    public static readonly LoopKind Do = new("Do", DoActivity.StructureKind, DoActivity.StructureSchemaVersion, DoActivity.BodySlotName);

    public static readonly LoopKind For = new("For", ForActivity.StructureKind, ForActivity.StructureSchemaVersion, ForActivity.BodySlotName);

    public static readonly LoopKind ForEach = new("ForEach", ForEachActivity.StructureKind, ForEachActivity.StructureSchemaVersion, ForEachActivity.BodySlotName);

    public static readonly LoopKind While = new("While", WhileActivity.StructureKind, WhileActivity.StructureSchemaVersion, WhileActivity.BodySlotName);
}
