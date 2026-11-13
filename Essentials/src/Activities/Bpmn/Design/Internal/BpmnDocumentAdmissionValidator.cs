using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Retention is available before complete-source execution is qualified.</summary>
internal sealed class BpmnDocumentAdmissionValidator(IEnumerable<IActivityStructureHandler> handlers) : IDraftValidator
{
    private readonly DefaultActivityStructureService _structures = new(handlers);

    public ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<ValidationError>();
        var pending = new Stack<ActivityNode>();
        if (draft.State.RootActivity is { } root) pending.Push(root);
        while (pending.TryPop(out var activity))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (activity.Structure is { Kind: BpmnActivityIdentities.StructureKind, SchemaVersion: BpmnAuthoredDocument.SchemaVersion })
            {
                var document = BpmnDocumentStructureHandler.Read(activity);
                errors.Add(new ValidationError(activity.NodeId, "Bpmn/ExecutionCapability",
                    "Complete-document BPMN execution is not qualified. The complete authored source remains saveable; publication and Test Drive stay closed."));
                foreach (var child in document.Activities) pending.Push(child);
            }
            else foreach (var child in _structures.ProjectChildren(activity).SelectMany(slot => slot.Activities)) pending.Push(child);
        }
        return ValueTask.FromResult<IEnumerable<ValidationError>>(errors);
    }
}
