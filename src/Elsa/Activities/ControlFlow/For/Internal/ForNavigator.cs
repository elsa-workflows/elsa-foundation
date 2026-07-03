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
    private ForNavigator(ExecutableNode? body) => Body = body;

    /// <summary>The body activity to run each pass, or <c>null</c> when the loop has an empty body.</summary>
    public ExecutableNode? Body { get; }

    public static ForNavigator From(ExecutableNode executableNode)
    {
        ArgumentNullException.ThrowIfNull(executableNode);

        if (executableNode.ChildSlots.Count == 0 && executableNode.Structure is null)
            return new ForNavigator(null);

        var structure = ExecutableStructureReader.ReadStructure<ForExecutableStructure>(
            executableNode, "For", ForActivity.StructureKind, ForActivity.StructureSchemaVersion, Fail);
        var bodyChild = ExecutableStructureReader.ResolveSingleSlotChild(
            executableNode, ForActivity.BodySlotName, "For", "body", Fail);
        var body = ExecutableStructureReader.MatchSingleSlotChild(
            executableNode, "For", ForActivity.BodySlotName, "body", "body", structure.Body, bodyChild, Fail);
        return new ForNavigator(body);
    }

    /// <summary>True when <paramref name="executableNodeId"/> is this loop's body node.</summary>
    public bool IsBody(string executableNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        return Body is { } body && StringComparer.Ordinal.Equals(body.ExecutableNodeId, executableNodeId);
    }

    private static ForExecutionException Fail(string message, Exception? inner) =>
        inner is null ? new(message) : new(message, inner);
}

