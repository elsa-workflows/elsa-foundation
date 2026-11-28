using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>
/// Resolves the executable <c>Dispatch</c>, <c>ChildCompleted</c> and <c>ChildFaulted</c> nodes of a
/// <c>BulkDispatchWorkflows</c> executable node from its compiled structure and named child slots, as the
/// <c>If</c> navigator does for its branches.
/// </summary>
internal sealed class BulkDispatchWorkflowsNavigator
{
    private const string Label = "BulkDispatchWorkflows";

    private BulkDispatchWorkflowsNavigator(ExecutableNode? dispatch, ExecutableNode? childCompleted, ExecutableNode? childFaulted)
    {
        Dispatch = dispatch;
        ChildCompleted = childCompleted;
        ChildFaulted = childFaulted;
    }

    public ExecutableNode? Dispatch { get; }
    public ExecutableNode? ChildCompleted { get; }
    public ExecutableNode? ChildFaulted { get; }

    public static BulkDispatchWorkflowsNavigator From(ExecutableNode executableNode)
    {
        ArgumentNullException.ThrowIfNull(executableNode);

        if (executableNode.ChildSlots.Count == 0 && executableNode.Structure is null)
            return new BulkDispatchWorkflowsNavigator(null, null, null);

        var structure = ExecutableStructureReader.ReadStructure<BulkDispatchWorkflowsExecutableStructure>(
            executableNode, Label, BulkDispatchWorkflowsConstants.StructureKind, BulkDispatchWorkflowsConstants.StructureSchemaVersion, Fail);

        return new BulkDispatchWorkflowsNavigator(
            Match(executableNode, BulkDispatchWorkflowsConstants.DispatchSlotName, "'Dispatch' node", structure.Dispatch),
            Match(executableNode, BulkDispatchWorkflowsConstants.ChildCompletedSlotName, "'ChildCompleted' node", structure.ChildCompleted),
            Match(executableNode, BulkDispatchWorkflowsConstants.ChildFaultedSlotName, "'ChildFaulted' node", structure.ChildFaulted));
    }

    public bool IsDispatch(string executableNodeId) => Is(Dispatch, executableNodeId);

    public bool IsSlot(string executableNodeId) => Is(ChildCompleted, executableNodeId) || Is(ChildFaulted, executableNodeId);

    private static bool Is(ExecutableNode? node, string executableNodeId) =>
        node is not null && StringComparer.Ordinal.Equals(node.ExecutableNodeId, executableNodeId);

    private static ExecutableNode? Match(ExecutableNode executableNode, string slotName, string declaredPhrase, string? structureNodeId)
    {
        var slotChild = ExecutableStructureReader.ResolveSingleSlotChild(executableNode, slotName, Label, "slot", Fail);
        return ExecutableStructureReader.MatchSingleSlotChild(
            executableNode, Label, slotName, "slot", declaredPhrase, structureNodeId, slotChild, Fail);
    }

    private static InvalidOperationException Fail(string message, Exception? inner) =>
        inner is null ? new(message) : new(message, inner);
}
