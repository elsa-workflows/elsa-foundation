using System.Text.Json;
using Elsa.Activities.Do.Exceptions;
using Elsa.Activities.Do.Models;
using Elsa.Workflows.Runtime.Core.Models;
using DoActivity = Elsa.Activities.Do.Activities.Do;

namespace Elsa.Activities.Do.Internal;

/// <summary>
/// Resolves the executable <c>Body</c> branch node for a <c>Do</c> executable node by reading its
/// compiled structure and matching the recorded body node id against the named child slot. Mirrors
/// <c>WhileNavigator</c>: structure is the identity source of truth and the child slot carries the actual
/// executable node.
/// </summary>
internal sealed class DoNavigator
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private DoNavigator(ExecutableNode? body) => Body = body;

    /// <summary>The body branch scheduled once per pass, or <c>null</c> when the loop has an empty body.</summary>
    public ExecutableNode? Body { get; }

    public static DoNavigator From(ExecutableNode executableNode)
    {
        ArgumentNullException.ThrowIfNull(executableNode);

        var bodyChild = ResolveSingleSlotChild(executableNode, DoActivity.BodySlotName);

        if (bodyChild is null && executableNode.Structure is null)
            return new DoNavigator(null);

        var structure = ReadStructure(executableNode);
        var body = MatchBranch(executableNode, structure.Body, bodyChild, DoActivity.BodySlotName);

        return new DoNavigator(body);
    }

    /// <summary>Whether the given executable node id is this loop's body branch.</summary>
    public bool IsBody(string executableNodeId) =>
        Body is { } body && StringComparer.Ordinal.Equals(body.ExecutableNodeId, executableNodeId);

    private static ExecutableNode? ResolveSingleSlotChild(ExecutableNode executableNode, string slotName)
    {
        var slot = executableNode.ChildSlots.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, slotName));
        var children = slot?.Activities.ToArray() ?? [];

        if (children.Length > 1)
            throw new DoExecutionException($"Do executable node '{executableNode.ExecutableNodeId}' slot '{slotName}' must contain at most one body activity but contains {children.Length}.");

        return children.Length == 0 ? null : children[0];
    }

    private static ExecutableNode? MatchBranch(
        ExecutableNode executableNode,
        string? structureNodeId,
        ExecutableNode? slotChild,
        string slotName)
    {
        if (structureNodeId is null && slotChild is null)
            return null;

        if (structureNodeId is null)
            throw new DoExecutionException($"Do executable node '{executableNode.ExecutableNodeId}' slot '{slotName}' carries a body activity but its structure declares no body.");

        if (slotChild is null)
            throw new DoExecutionException($"Do executable node '{executableNode.ExecutableNodeId}' structure declares body '{structureNodeId}' but slot '{slotName}' carries no matching child.");

        if (!StringComparer.Ordinal.Equals(slotChild.ExecutableNodeId, structureNodeId))
            throw new DoExecutionException($"Do executable node '{executableNode.ExecutableNodeId}' structure declares body '{structureNodeId}' but slot '{slotName}' carries child '{slotChild.ExecutableNodeId}'.");

        return slotChild;
    }

    private static DoExecutableStructure ReadStructure(ExecutableNode executableNode)
    {
        if (executableNode.Structure is null)
            throw new DoExecutionException($"Do executable node '{executableNode.ExecutableNodeId}' requires structure '{DoActivity.StructureKind}'.");

        if (!StringComparer.Ordinal.Equals(executableNode.Structure.Kind, DoActivity.StructureKind))
            throw new DoExecutionException($"Do executable node '{executableNode.ExecutableNodeId}' has unsupported structure kind '{executableNode.Structure.Kind}'.");

        if (!StringComparer.Ordinal.Equals(executableNode.Structure.SchemaVersion, DoActivity.StructureSchemaVersion))
            throw new DoExecutionException($"Do executable node '{executableNode.ExecutableNodeId}' has unsupported structure schema version '{executableNode.Structure.SchemaVersion}'.");

        try
        {
            return executableNode.Structure.Payload.Deserialize<DoExecutableStructure>(SerializerOptions)
                   ?? throw new DoExecutionException($"Do executable node '{executableNode.ExecutableNodeId}' structure resolved to null.");
        }
        catch (DoExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new DoExecutionException($"Do executable node '{executableNode.ExecutableNodeId}' structure is not a valid Do structure payload.", exception);
        }
    }
}
