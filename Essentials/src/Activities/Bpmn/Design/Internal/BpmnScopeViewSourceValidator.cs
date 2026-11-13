using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>
/// Checks physical source before it is traversed. A JSON provenance marker cannot authorize storing
/// derived views; the ordinary write validation gate propagates this structural rejection.
/// </summary>
internal sealed class BpmnScopeViewSourceValidator(IEnumerable<IActivityStructureHandler> handlers) : IDraftValidator
{
    private readonly DefaultActivityStructureService _structures = new(handlers);

    public ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = new Stack<ActivityNode>();
        if (draft.State.RootActivity is { } root) pending.Push(root);
        while (pending.TryPop(out var activity))
        {
            cancellationToken.ThrowIfCancellationRequested();
            BpmnDocumentScopeProjection.RejectPhysicalView(activity);
            // Complete-source projection creates legitimate ephemeral scope nodes. Inspect its
            // physical native records instead; all other contributions own their native slots.
            var children = activity.Structure is { Kind: BpmnActivityIdentities.StructureKind, SchemaVersion: BpmnAuthoredDocument.SchemaVersion }
                ? BpmnDocumentStructureHandler.Read(activity).Activities
                : _structures.ProjectChildren(activity).SelectMany(slot => slot.Activities);
            foreach (var child in children) pending.Push(child);
        }
        return ValueTask.FromResult<IEnumerable<ValidationError>>([]);
    }
}
