using System.Text.Json;
using Elsa.Activities.For.Exceptions;
using Elsa.Activities.For.Models;
using Elsa.Workflows.Runtime.Core.Models;
using ForActivity = Elsa.Activities.For.Activities.For;

namespace Elsa.Activities.For.Internal;

/// <summary>
/// Resolves the executable body node for a <c>For</c> executable node by reading its compiled structure
/// and matching the recorded body node id against the single <c>For.Body</c> child slot. Mirrors
/// <c>IfNavigator</c>/<c>SwitchNavigator</c>: structure is the body-identity source of truth and the
/// child slot carries the actual executable node.
/// </summary>
internal sealed class ForNavigator
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private ForNavigator(ExecutableNode? body) => Body = body;

    /// <summary>The body activity to run each pass, or <c>null</c> when the loop has an empty body.</summary>
    public ExecutableNode? Body { get; }

    public static ForNavigator From(ExecutableNode executableNode)
    {
        ArgumentNullException.ThrowIfNull(executableNode);

        if (executableNode.ChildSlots.Count == 0 && executableNode.Structure is null)
            return new ForNavigator(null);

        var structure = ReadStructure(executableNode);
        var body = MatchBody(executableNode, structure.Body, ForActivity.BodySlotName);
        return new ForNavigator(body);
    }

    /// <summary>True when <paramref name="executableNodeId"/> is this loop's body node.</summary>
    public bool IsBody(string executableNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        return Body is { } body && StringComparer.Ordinal.Equals(body.ExecutableNodeId, executableNodeId);
    }

    private static ExecutableNode? MatchBody(ExecutableNode executableNode, string? structureNodeId, string slotName)
    {
        var slotChild = ResolveSingleSlotChild(executableNode, slotName);

        if (structureNodeId is null && slotChild is null)
            return null;

        if (structureNodeId is null)
            throw new ForExecutionException($"For executable node '{executableNode.ExecutableNodeId}' slot '{slotName}' carries a body activity but its structure declares no body.");

        if (slotChild is null)
            throw new ForExecutionException($"For executable node '{executableNode.ExecutableNodeId}' structure declares body '{structureNodeId}' but slot '{slotName}' carries no matching child.");

        if (!StringComparer.Ordinal.Equals(slotChild.ExecutableNodeId, structureNodeId))
            throw new ForExecutionException($"For executable node '{executableNode.ExecutableNodeId}' structure declares body '{structureNodeId}' but slot '{slotName}' carries child '{slotChild.ExecutableNodeId}'.");

        return slotChild;
    }

    private static ExecutableNode? ResolveSingleSlotChild(ExecutableNode executableNode, string slotName)
    {
        var slot = executableNode.ChildSlots.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, slotName));
        var children = slot?.Activities.ToArray() ?? [];

        if (children.Length > 1)
            throw new ForExecutionException($"For executable node '{executableNode.ExecutableNodeId}' slot '{slotName}' must contain at most one body activity but contains {children.Length}.");

        return children.Length == 0 ? null : children[0];
    }

    private static ForExecutableStructure ReadStructure(ExecutableNode executableNode)
    {
        if (executableNode.Structure is null)
            throw new ForExecutionException($"For executable node '{executableNode.ExecutableNodeId}' requires structure '{ForActivity.StructureKind}'.");

        if (!StringComparer.Ordinal.Equals(executableNode.Structure.Kind, ForActivity.StructureKind))
            throw new ForExecutionException($"For executable node '{executableNode.ExecutableNodeId}' has unsupported structure kind '{executableNode.Structure.Kind}'.");

        if (!StringComparer.Ordinal.Equals(executableNode.Structure.SchemaVersion, ForActivity.StructureSchemaVersion))
            throw new ForExecutionException($"For executable node '{executableNode.ExecutableNodeId}' has unsupported structure schema version '{executableNode.Structure.SchemaVersion}'.");

        try
        {
            return executableNode.Structure.Payload.Deserialize<ForExecutableStructure>(SerializerOptions)
                   ?? throw new ForExecutionException($"For executable node '{executableNode.ExecutableNodeId}' structure resolved to null.");
        }
        catch (ForExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new ForExecutionException($"For executable node '{executableNode.ExecutableNodeId}' structure is not a valid For structure payload.", exception);
        }
    }
}
