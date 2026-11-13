using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Content-derived execution findings leave unsupported complete-source drafts saveable.</summary>
internal sealed class BpmnDocumentAdmissionValidator(IEnumerable<IActivityStructureHandler> handlers,
    IEnumerable<IBpmnDocumentDirectAdmission>? directAdmissions = null) : IDraftValidator
{
    private readonly DefaultActivityStructureService _structures = new(handlers);
    private readonly IBpmnDocumentDirectAdmission[] _directAdmissions = (directAdmissions ?? []).ToArray();

    public ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Whole physical content can qualify for the separately composed direct publisher.
        // This content assessment leaves native compilation and other validators independent.
        if (draft.State.RootActivity?.Structure is { Kind: BpmnActivityIdentities.StructureKind, SchemaVersion: BpmnAuthoredDocument.SchemaVersion } &&
            _directAdmissions.Any(admission => admission.CanPromote(draft.State)))
            return ValueTask.FromResult<IEnumerable<ValidationError>>([]);
        var errors = new List<ValidationError>();
        var pending = new Stack<ActivityNode>();
        if (draft.State.RootActivity is { } root) pending.Push(root);
        while (pending.TryPop(out var activity))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (activity.Structure is { Kind: BpmnActivityIdentities.StructureKind, SchemaVersion: BpmnAuthoredDocument.SchemaVersion })
            {
                var document = BpmnDocumentStructureHandler.Read(activity);
                errors.AddRange(BpmnDocumentAdmission.Assess(activity).Select(diagnostic => new ValidationError(activity.NodeId,
                    "Bpmn/ExecutionCapability", $"{diagnostic.Code} at {diagnostic.SourcePath}: {diagnostic.Message}")));
                foreach (var child in document.Activities) pending.Push(child);
            }
            else foreach (var child in _structures.ProjectChildren(activity).SelectMany(slot => slot.Activities)) pending.Push(child);
        }
        return ValueTask.FromResult<IEnumerable<ValidationError>>(errors);
    }
}
