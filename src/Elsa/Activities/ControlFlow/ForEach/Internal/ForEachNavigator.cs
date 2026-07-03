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
    private ForEachNavigator(ExecutableNode? body) => Body = body;

    /// <summary>The body executable node, or <c>null</c> when the loop declares an empty body.</summary>
    public ExecutableNode? Body { get; }

    public static ForEachNavigator From(ExecutableNode executableNode)
    {
        ArgumentNullException.ThrowIfNull(executableNode);

        if (executableNode.ChildSlots.Count == 0 && executableNode.Structure is null)
            return new ForEachNavigator(null);

        var structure = ExecutableStructureReader.ReadStructure<ForEachExecutableStructure>(
            executableNode, "ForEach", ForEachActivity.StructureKind, ForEachActivity.StructureSchemaVersion, Fail);
        var bodyChild = ExecutableStructureReader.ResolveSingleSlotChild(
            executableNode, ForEachActivity.BodySlotName, "ForEach", "body", Fail);
        var body = ExecutableStructureReader.MatchSingleSlotChild(
            executableNode, "ForEach", ForEachActivity.BodySlotName, "body", "body", structure.Body, bodyChild, Fail);
        return new ForEachNavigator(body);
    }

    /// <summary>True when <paramref name="executableNodeId"/> is this loop's body node.</summary>
    public bool IsBody(string executableNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        return Body is { } body && StringComparer.Ordinal.Equals(body.ExecutableNodeId, executableNodeId);
    }

    private static ForEachExecutionException Fail(string message, Exception? inner) =>
        inner is null ? new(message) : new(message, inner);
}

