using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Derived execution diagnostics do not prevent retaining an unsupported draft.</summary>
internal sealed class BpmnDocumentAdmissionValidator(IEnumerable<IActivityStructureHandler> handlers) : IDraftValidator
{
    private readonly DefaultActivityStructureService _structures = new(handlers);

    public ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();
        var pending = new Stack<ActivityNode>();
        if (draft.State.RootActivity is { } root) pending.Push(root);
        while (pending.TryPop(out var activity))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (activity.Structure is { Kind: BpmnActivityIdentities.StructureKind, SchemaVersion: BpmnAuthoredDocument.SchemaVersion })
            {
                errors.AddRange(BpmnDocumentAdmission.Assess(activity).Select(diagnostic => new ValidationError(activity.NodeId,
                    "Bpmn/ExecutionCapability", $"{diagnostic.Code} at {diagnostic.SourcePath}: {diagnostic.Message}")));
                foreach (var child in BpmnDocumentStructureHandler.Read(activity).Activities) pending.Push(child);
            }
            else foreach (var child in _structures.ProjectChildren(activity).SelectMany(slot => slot.Activities)) pending.Push(child);
        }
        return ValueTask.FromResult<IEnumerable<ValidationError>>(errors);
    }
}
