using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using BulkDispatchWorkflowsActivity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.BulkDispatchWorkflows;
using DispatchWorkflowActivity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow;

namespace Elsa.Activities.DispatchWorkflow.Design.Services;

/// <summary>
/// Draft validator for <c>BulkDispatchWorkflows</c> (spec 1242 FR-003): its <c>Dispatch</c> slot must hold exactly one
/// <c>DispatchWorkflow</c> node. A new bulk node starts with the slot empty, and this message tells the author what to
/// drop there. Errors do not block saving the Draft; the promotion gate refuses to publish it.
/// </summary>
internal sealed class BulkDispatchSlotValidator(
    IActivityStructureService activityStructureService,
    IActivityDefinitionLookup activityDefinitionLookup) : IDraftValidator
{
    public const string DispatchSlotType = "BulkDispatch/DispatchSlot";

    private static readonly string BulkDefinitionId = ActivityCatalogStableIds.DefinitionId(typeof(BulkDispatchWorkflowsActivity).FullName!)!;
    private static readonly string DispatchDefinitionId = ActivityCatalogStableIds.DefinitionId(typeof(DispatchWorkflowActivity).FullName!)!;

    public async ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();
        foreach (var node in Walk(draft.State.RootActivity))
        {
            if (!await IsAsync(node, BulkDefinitionId, cancellationToken) &&
                !StringComparer.Ordinal.Equals(node.Structure?.Kind, BulkDispatchWorkflowsConstants.StructureKind))
                continue;

            var dispatch = activityStructureService.ProjectChildren(node)
                .FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, BulkDispatchWorkflowsConstants.DispatchSlotName))?
                .Activities.ToArray() ?? [];

            if (dispatch.Length == 0)
                errors.Add(new ValidationError(node.NodeId, DispatchSlotType,
                    $"Bulk Dispatch Workflows '{node.NodeId}' has an empty Dispatch slot. Drop a Dispatch Workflow there: it runs once per item and says which workflow to start, with which inputs and correlation id."));
            else if (dispatch.Length > 1 || !await IsAsync(dispatch[0], DispatchDefinitionId, cancellationToken))
                errors.Add(new ValidationError(node.NodeId, DispatchSlotType,
                    $"Bulk Dispatch Workflows '{node.NodeId}' must hold one Dispatch Workflow in its Dispatch slot, and nothing else. Put other activities in the Child completed or Child faulted slot."));
        }

        return errors;
    }

    private async ValueTask<bool> IsAsync(ActivityNode node, string definitionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(node.ActivityVersionId))
            return false;
        var version = await activityDefinitionLookup.FindVersion(node.ActivityVersionId, cancellationToken);
        return StringComparer.Ordinal.Equals(version?.DefinitionId, definitionId);
    }

    // Iterative depth-first walk; a reference-identity visited set guards against cyclic data.
    private IEnumerable<ActivityNode> Walk(ActivityNode? root)
    {
        if (root is null)
            yield break;

        var stack = new Stack<ActivityNode>();
        stack.Push(root);
        var visited = new HashSet<ActivityNode>(ReferenceEqualityComparer.Instance);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node))
                continue;

            yield return node;
            foreach (var slot in activityStructureService.ProjectChildren(node))
            foreach (var child in slot.Activities)
                stack.Push(child);
        }
    }
}
