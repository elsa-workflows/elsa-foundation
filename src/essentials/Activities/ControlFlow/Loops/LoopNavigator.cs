using Elsa.Activities.ControlFlow.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.ControlFlow.Loops;

/// <summary>
/// Resolves the executable body node of a loop activity by reading its compiled structure and matching the
/// recorded body node id against the loop's body slot. Mirrors <c>IfNavigator</c>/<c>SwitchNavigator</c>: structure
/// is the body-identity source of truth and the child slot carries the actual executable node.
/// </summary>
internal sealed class LoopNavigator
{
    private LoopNavigator(ExecutableNode? body) => Body = body;

    /// <summary>The body activity to run each pass, or <c>null</c> when the loop has an empty body.</summary>
    public ExecutableNode? Body { get; }

    public static LoopNavigator From(ExecutableNode executableNode, LoopKind loop)
    {
        ArgumentNullException.ThrowIfNull(executableNode);

        // Only a node with no slots at all may omit its structure. A slot without a structure is malformed and
        // fails, rather than reading as an empty body that would end the loop as if it had succeeded.
        if (executableNode.ChildSlots.Count == 0 && executableNode.Structure is null)
            return new LoopNavigator(null);

        var structure = ExecutableStructureReader.ReadStructure<LoopExecutableStructure>(
            executableNode, loop.Name, loop.StructureKind, loop.StructureSchemaVersion, Fail);
        var bodyChild = ExecutableStructureReader.ResolveSingleSlotChild(
            executableNode, loop.BodySlotName, loop.Name, "body", Fail);
        var body = ExecutableStructureReader.MatchSingleSlotChild(
            executableNode, loop.Name, loop.BodySlotName, "body", "body", structure.Body, bodyChild, Fail);
        return new LoopNavigator(body);
    }

    /// <summary>True when <paramref name="executableNodeId"/> is this loop's body node.</summary>
    public bool IsBody(string executableNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        return Body is { } body && StringComparer.Ordinal.Equals(body.ExecutableNodeId, executableNodeId);
    }

    private static ControlFlowExecutionException Fail(string message, Exception? inner) =>
        inner is null ? new(message) : new(message, inner);
}
