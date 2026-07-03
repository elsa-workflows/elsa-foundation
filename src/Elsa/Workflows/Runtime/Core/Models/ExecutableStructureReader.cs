using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Shared helpers for the control-flow activity navigators that resolve executable children from an
/// <see cref="ExecutableNode"/>'s compiled <see cref="ExecutableActivityStructure"/>. Each navigator
/// owns its own structure payload type and its own execution-exception type; this reader centralises the
/// otherwise line-for-line-identical structure validation and single-slot-child resolution so those
/// navigators do not each re-implement it. Callers pass an <paramref name="exceptionFactory"/> so the
/// exact exception type and message text of every navigator are preserved.
/// </summary>
public static class ExecutableStructureReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Validates the node's structure kind/schema version against the owning activity's declared values
    /// and deserialises the payload into <typeparamref name="TStructure"/>. The
    /// <paramref name="exceptionFactory"/> builds the navigator's own execution exception (message plus an
    /// optional inner exception) for every failure so behaviour is identical to a hand-rolled reader.
    /// </summary>
    public static TStructure ReadStructure<TStructure>(
        ExecutableNode executableNode,
        string label,
        string structureKind,
        string structureSchemaVersion,
        Func<string, Exception?, Exception> exceptionFactory)
    {
        var id = executableNode.ExecutableNodeId;

        if (executableNode.Structure is null)
            throw exceptionFactory($"{label} executable node '{id}' requires structure '{structureKind}'.", null);

        if (!StringComparer.Ordinal.Equals(executableNode.Structure.Kind, structureKind))
            throw exceptionFactory($"{label} executable node '{id}' has unsupported structure kind '{executableNode.Structure.Kind}'.", null);

        if (!StringComparer.Ordinal.Equals(executableNode.Structure.SchemaVersion, structureSchemaVersion))
            throw exceptionFactory($"{label} executable node '{id}' has unsupported structure schema version '{executableNode.Structure.SchemaVersion}'.", null);

        try
        {
            return executableNode.Structure.Payload.Deserialize<TStructure>(SerializerOptions)
                   ?? throw exceptionFactory($"{label} executable node '{id}' structure resolved to null.", null);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw exceptionFactory($"{label} executable node '{id}' structure is not a valid {label} structure payload.", exception);
        }
    }

    /// <summary>
    /// Resolves the single child activity carried by the named slot, throwing when the slot carries more
    /// than one. <paramref name="childActivityNoun"/> (e.g. <c>"branch"</c> or <c>"body"</c>) is woven into
    /// the message exactly as each navigator spells it.
    /// </summary>
    public static ExecutableNode? ResolveSingleSlotChild(
        ExecutableNode executableNode,
        string slotName,
        string label,
        string childActivityNoun,
        Func<string, Exception?, Exception> exceptionFactory)
    {
        var slot = executableNode.ChildSlots.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, slotName));
        var children = slot?.Activities.ToArray() ?? [];

        if (children.Length > 1)
            throw exceptionFactory($"{label} executable node '{executableNode.ExecutableNodeId}' slot '{slotName}' must contain at most one {childActivityNoun} activity but contains {children.Length}.", null);

        return children.Length == 0 ? null : children[0];
    }

    /// <summary>
    /// Matches an already-resolved <paramref name="slotChild"/> against the structure-declared node id for a
    /// single-child slot. <paramref name="childActivityNoun"/> is the "carries a … activity" noun and
    /// <paramref name="declaredPhrase"/> is the exact "declares … / declares no …" phrase each navigator
    /// uses (e.g. <c>"body"</c>, <c>"'Then' branch"</c>, <c>"case 'x' branch"</c>), so all three failure
    /// messages are reproduced byte-for-byte.
    /// </summary>
    public static ExecutableNode? MatchSingleSlotChild(
        ExecutableNode executableNode,
        string label,
        string slotName,
        string childActivityNoun,
        string declaredPhrase,
        string? structureNodeId,
        ExecutableNode? slotChild,
        Func<string, Exception?, Exception> exceptionFactory)
    {
        var id = executableNode.ExecutableNodeId;

        if (structureNodeId is null && slotChild is null)
            return null;

        if (structureNodeId is null)
            throw exceptionFactory($"{label} executable node '{id}' slot '{slotName}' carries a {childActivityNoun} activity but its structure declares no {declaredPhrase}.", null);

        if (slotChild is null)
            throw exceptionFactory($"{label} executable node '{id}' structure declares {declaredPhrase} '{structureNodeId}' but slot '{slotName}' carries no matching child.", null);

        if (!StringComparer.Ordinal.Equals(slotChild.ExecutableNodeId, structureNodeId))
            throw exceptionFactory($"{label} executable node '{id}' structure declares {declaredPhrase} '{structureNodeId}' but slot '{slotName}' carries child '{slotChild.ExecutableNodeId}'.", null);

        return slotChild;
    }
}
