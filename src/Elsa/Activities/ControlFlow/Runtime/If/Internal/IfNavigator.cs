using Elsa.Activities.If.Exceptions;
using Elsa.Activities.If.Models;
using Elsa.Workflows.Runtime.Core.Models;
using IfActivity = Elsa.Activities.If.Activities.If;

namespace Elsa.Activities.If.Internal;

/// <summary>
/// Resolves the executable <c>Then</c> / <c>Else</c> branch nodes for an <c>If</c> executable node by
/// reading its compiled structure and matching the recorded branch node ids against the named child
/// slots. Mirrors <c>SequenceNavigator</c>: structure is the ordering/identity source of truth and the
/// child slots carry the actual executable nodes.
/// </summary>
internal sealed class IfNavigator
{
    private IfNavigator(ExecutableNode? then, ExecutableNode? @else)
    {
        Then = then;
        Else = @else;
    }

    public ExecutableNode? Then { get; }
    public ExecutableNode? Else { get; }

    public static IfNavigator From(ExecutableNode executableNode)
    {
        ArgumentNullException.ThrowIfNull(executableNode);

        var thenChild = ExecutableStructureReader.ResolveSingleSlotChild(executableNode, IfActivity.ThenSlotName, "If", "branch", Fail);
        var elseChild = ExecutableStructureReader.ResolveSingleSlotChild(executableNode, IfActivity.ElseSlotName, "If", "branch", Fail);

        if (thenChild is null && elseChild is null && executableNode.Structure is null)
            return new IfNavigator(null, null);

        var structure = ExecutableStructureReader.ReadStructure<IfExecutableStructure>(
            executableNode, "If", IfActivity.StructureKind, IfActivity.StructureSchemaVersion, Fail);

        var then = ExecutableStructureReader.MatchSingleSlotChild(
            executableNode, "If", IfActivity.ThenSlotName, "branch", "'Then' branch", structure.Then, thenChild, Fail);
        var @else = ExecutableStructureReader.MatchSingleSlotChild(
            executableNode, "If", IfActivity.ElseSlotName, "branch", "'Else' branch", structure.Else, elseChild, Fail);

        return new IfNavigator(then, @else);
    }

    public ExecutableNode? Select(bool condition) => condition ? Then : Else;

    private static IfExecutionException Fail(string message, Exception? inner) =>
        inner is null ? new(message) : new(message, inner);
}

