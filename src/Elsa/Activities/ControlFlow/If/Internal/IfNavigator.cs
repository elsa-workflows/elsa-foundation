using System.Text.Json;
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
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

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

        var thenChild = ResolveSingleSlotChild(executableNode, IfActivity.ThenSlotName);
        var elseChild = ResolveSingleSlotChild(executableNode, IfActivity.ElseSlotName);

        if (thenChild is null && elseChild is null && executableNode.Structure is null)
            return new IfNavigator(null, null);

        var structure = ReadStructure(executableNode);

        var then = MatchBranch(executableNode, "Then", structure.Then, thenChild, IfActivity.ThenSlotName);
        var @else = MatchBranch(executableNode, "Else", structure.Else, elseChild, IfActivity.ElseSlotName);

        return new IfNavigator(then, @else);
    }

    public ExecutableNode? Select(bool condition) => condition ? Then : Else;

    private static ExecutableNode? ResolveSingleSlotChild(ExecutableNode executableNode, string slotName)
    {
        var slot = executableNode.ChildSlots.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, slotName));
        var children = slot?.Activities.ToArray() ?? [];

        if (children.Length > 1)
            throw new IfExecutionException($"If executable node '{executableNode.ExecutableNodeId}' slot '{slotName}' must contain at most one branch activity but contains {children.Length}.");

        return children.Length == 0 ? null : children[0];
    }

    private static ExecutableNode? MatchBranch(
        ExecutableNode executableNode,
        string branchName,
        string? structureNodeId,
        ExecutableNode? slotChild,
        string slotName)
    {
        if (structureNodeId is null && slotChild is null)
            return null;

        if (structureNodeId is null)
            throw new IfExecutionException($"If executable node '{executableNode.ExecutableNodeId}' slot '{slotName}' carries a branch activity but its structure declares no '{branchName}' branch.");

        if (slotChild is null)
            throw new IfExecutionException($"If executable node '{executableNode.ExecutableNodeId}' structure declares '{branchName}' branch '{structureNodeId}' but slot '{slotName}' carries no matching child.");

        if (!StringComparer.Ordinal.Equals(slotChild.ExecutableNodeId, structureNodeId))
            throw new IfExecutionException($"If executable node '{executableNode.ExecutableNodeId}' structure declares '{branchName}' branch '{structureNodeId}' but slot '{slotName}' carries child '{slotChild.ExecutableNodeId}'.");

        return slotChild;
    }

    private static IfExecutableStructure ReadStructure(ExecutableNode executableNode)
    {
        if (executableNode.Structure is null)
            throw new IfExecutionException($"If executable node '{executableNode.ExecutableNodeId}' requires structure '{IfActivity.StructureKind}'.");

        if (!StringComparer.Ordinal.Equals(executableNode.Structure.Kind, IfActivity.StructureKind))
            throw new IfExecutionException($"If executable node '{executableNode.ExecutableNodeId}' has unsupported structure kind '{executableNode.Structure.Kind}'.");

        if (!StringComparer.Ordinal.Equals(executableNode.Structure.SchemaVersion, IfActivity.StructureSchemaVersion))
            throw new IfExecutionException($"If executable node '{executableNode.ExecutableNodeId}' has unsupported structure schema version '{executableNode.Structure.SchemaVersion}'.");

        try
        {
            return executableNode.Structure.Payload.Deserialize<IfExecutableStructure>(SerializerOptions)
                   ?? throw new IfExecutionException($"If executable node '{executableNode.ExecutableNodeId}' structure resolved to null.");
        }
        catch (IfExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new IfExecutionException($"If executable node '{executableNode.ExecutableNodeId}' structure is not a valid If structure payload.", exception);
        }
    }
}
