using System.Text.Json;
using Elsa.Activities.ForEach.Exceptions;
using Elsa.Activities.ForEach.Models;
using Elsa.Workflows.Runtime.Core.Models;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;

namespace Elsa.Activities.ForEach.Internal;

/// <summary>
/// Resolves the executable body node for a <c>ForEach</c> executable node by reading its compiled
/// structure and matching the recorded body node id against the named body child slot. Mirrors
/// <c>IfNavigator</c>/<c>SwitchNavigator</c>: structure is the body-identity source of truth and the
/// child slot carries the actual executable node. The same resolved body runs once per collection item.
/// </summary>
internal sealed class ForEachNavigator
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private ForEachNavigator(ExecutableNode? body) => Body = body;

    /// <summary>The body executable node, or <c>null</c> when the loop declares an empty body.</summary>
    public ExecutableNode? Body { get; }

    public static ForEachNavigator From(ExecutableNode executableNode)
    {
        ArgumentNullException.ThrowIfNull(executableNode);

        if (executableNode.ChildSlots.Count == 0 && executableNode.Structure is null)
            return new ForEachNavigator(null);

        var structure = ReadStructure(executableNode);
        var body = MatchBody(executableNode, structure.Body);
        return new ForEachNavigator(body);
    }

    /// <summary>True when <paramref name="executableNodeId"/> is this loop's body node.</summary>
    public bool IsBody(string executableNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        return Body is { } body && StringComparer.Ordinal.Equals(body.ExecutableNodeId, executableNodeId);
    }

    private static ExecutableNode? MatchBody(ExecutableNode executableNode, string? structureNodeId)
    {
        var slotChild = ResolveSingleSlotChild(executableNode);

        if (structureNodeId is null && slotChild is null)
            return null;

        if (structureNodeId is null)
            throw new ForEachExecutionException($"ForEach executable node '{executableNode.ExecutableNodeId}' slot '{ForEachActivity.BodySlotName}' carries a body activity but its structure declares no body.");

        if (slotChild is null)
            throw new ForEachExecutionException($"ForEach executable node '{executableNode.ExecutableNodeId}' structure declares body '{structureNodeId}' but slot '{ForEachActivity.BodySlotName}' carries no matching child.");

        if (!StringComparer.Ordinal.Equals(slotChild.ExecutableNodeId, structureNodeId))
            throw new ForEachExecutionException($"ForEach executable node '{executableNode.ExecutableNodeId}' structure declares body '{structureNodeId}' but slot '{ForEachActivity.BodySlotName}' carries child '{slotChild.ExecutableNodeId}'.");

        return slotChild;
    }

    private static ExecutableNode? ResolveSingleSlotChild(ExecutableNode executableNode)
    {
        var slot = executableNode.ChildSlots.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, ForEachActivity.BodySlotName));
        var children = slot?.Activities.ToArray() ?? [];

        if (children.Length > 1)
            throw new ForEachExecutionException($"ForEach executable node '{executableNode.ExecutableNodeId}' slot '{ForEachActivity.BodySlotName}' must contain at most one body activity but contains {children.Length}.");

        return children.Length == 0 ? null : children[0];
    }

    private static ForEachExecutableStructure ReadStructure(ExecutableNode executableNode)
    {
        if (executableNode.Structure is null)
            throw new ForEachExecutionException($"ForEach executable node '{executableNode.ExecutableNodeId}' requires structure '{ForEachActivity.StructureKind}'.");

        if (!StringComparer.Ordinal.Equals(executableNode.Structure.Kind, ForEachActivity.StructureKind))
            throw new ForEachExecutionException($"ForEach executable node '{executableNode.ExecutableNodeId}' has unsupported structure kind '{executableNode.Structure.Kind}'.");

        if (!StringComparer.Ordinal.Equals(executableNode.Structure.SchemaVersion, ForEachActivity.StructureSchemaVersion))
            throw new ForEachExecutionException($"ForEach executable node '{executableNode.ExecutableNodeId}' has unsupported structure schema version '{executableNode.Structure.SchemaVersion}'.");

        try
        {
            return executableNode.Structure.Payload.Deserialize<ForEachExecutableStructure>(SerializerOptions)
                   ?? throw new ForEachExecutionException($"ForEach executable node '{executableNode.ExecutableNodeId}' structure resolved to null.");
        }
        catch (ForEachExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new ForEachExecutionException($"ForEach executable node '{executableNode.ExecutableNodeId}' structure is not a valid ForEach structure payload.", exception);
        }
    }
}
