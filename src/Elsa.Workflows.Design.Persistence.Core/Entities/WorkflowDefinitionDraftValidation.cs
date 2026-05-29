using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Entities;

/// <summary>
/// Persistent sibling of <see cref="WorkflowDefinitionDraft"/> that carries the Draft's
/// current validation errors. FK to the Draft (1:0..1); cascade-deletes with the Draft per
/// Unit C FR-029. Mutable; rewritten wholesale on every Draft mutation per FR-023's
/// delete-and-re-add lifecycle.
/// </summary>
/// <remarks>
/// No Version-side counterpart — Versions cannot exist with validation errors (FR-024
/// promotion gate); a Version-side sibling would have no purpose.
/// </remarks>
public sealed class WorkflowDefinitionDraftValidation : TenantEntity, IWorkflowDefinitionDraftValidation
{
    public string WorkflowDefinitionDraftId { get; set; } = default!;

    public WorkflowDefinitionDraft? WorkflowDefinitionDraft { get; set; }

    public List<ValidationError> Errors { get; set; } = [];

    IReadOnlyList<ValidationError> IWorkflowDefinitionDraftValidation.Errors => Errors;
}
