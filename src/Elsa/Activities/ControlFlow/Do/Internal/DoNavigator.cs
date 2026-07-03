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
    private DoNavigator(ExecutableNode? body) => Body = body;

    /// <summary>The body branch scheduled once per pass, or <c>null</c> when the loop has an empty body.</summary>
    public ExecutableNode? Body { get; }

    public static DoNavigator From(ExecutableNode executableNode)
    {
        ArgumentNullException.ThrowIfNull(executableNode);

        var bodyChild = ExecutableStructureReader.ResolveSingleSlotChild(
            executableNode, DoActivity.BodySlotName, "Do", "body", Fail);

        if (bodyChild is null && executableNode.Structure is null)
            return new DoNavigator(null);

        var structure = ExecutableStructureReader.ReadStructure<DoExecutableStructure>(
            executableNode, "Do", DoActivity.StructureKind, DoActivity.StructureSchemaVersion, Fail);
        var body = ExecutableStructureReader.MatchSingleSlotChild(
            executableNode, "Do", DoActivity.BodySlotName, "body", "body", structure.Body, bodyChild, Fail);

        return new DoNavigator(body);
    }

    /// <summary>Whether the given executable node id is this loop's body branch.</summary>
    public bool IsBody(string executableNodeId) =>
        Body is { } body && StringComparer.Ordinal.Equals(body.ExecutableNodeId, executableNodeId);

    private static DoExecutionException Fail(string message, Exception? inner) =>
        inner is null ? new(message) : new(message, inner);
}

