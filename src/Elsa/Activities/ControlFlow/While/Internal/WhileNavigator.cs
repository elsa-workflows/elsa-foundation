using Elsa.Activities.While.Exceptions;
using Elsa.Activities.While.Models;
using Elsa.Workflows.Runtime.Core.Models;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.While.Internal;

/// <summary>
/// Resolves the executable <c>Body</c> branch node for a <c>While</c> executable node by reading its
/// compiled structure and matching the recorded body node id against the named child slot. Mirrors
/// <c>IfNavigator</c>: structure is the identity source of truth and the child slot carries the actual
/// executable node.
/// </summary>
internal sealed class WhileNavigator
{
    private WhileNavigator(ExecutableNode? body) => Body = body;

    /// <summary>The body branch scheduled once per pass, or <c>null</c> when the loop has an empty body.</summary>
    public ExecutableNode? Body { get; }

    public static WhileNavigator From(ExecutableNode executableNode)
    {
        ArgumentNullException.ThrowIfNull(executableNode);

        var bodyChild = ExecutableStructureReader.ResolveSingleSlotChild(
            executableNode, WhileActivity.BodySlotName, "While", "body", Fail);

        if (bodyChild is null && executableNode.Structure is null)
            return new WhileNavigator(null);

        var structure = ExecutableStructureReader.ReadStructure<WhileExecutableStructure>(
            executableNode, "While", WhileActivity.StructureKind, WhileActivity.StructureSchemaVersion, Fail);
        var body = ExecutableStructureReader.MatchSingleSlotChild(
            executableNode, "While", WhileActivity.BodySlotName, "body", "body", structure.Body, bodyChild, Fail);

        return new WhileNavigator(body);
    }

    /// <summary>Whether the given executable node id is this loop's body branch.</summary>
    public bool IsBody(string executableNodeId) =>
        Body is { } body && StringComparer.Ordinal.Equals(body.ExecutableNodeId, executableNodeId);

    private static WhileExecutionException Fail(string message, Exception? inner) =>
        inner is null ? new(message) : new(message, inner);
}

