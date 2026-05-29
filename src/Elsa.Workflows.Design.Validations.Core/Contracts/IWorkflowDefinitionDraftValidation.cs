using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Contracts;

/// <summary>
/// Tier-1 read contract over a Draft's persisted validation result. Unit C FR-021 +
/// FR-024's promotion-gate read surface.
/// </summary>
/// <remarks>
/// <para>
/// Errors are rewritten wholesale on every Draft mutation (FR-023 — delete-and-re-add).
/// The promotion gate (<c>IPromoteDraftToVersionCommand</c>, Unit D's allocation) reads
/// <see cref="Errors"/>; a non-empty list MUST cause the gate to throw and block Version
/// creation.
/// </para>
/// <para>
/// There is no Version-side counterpart — Versions cannot exist with validation errors by
/// the promotion-gate rule (FR-024), so a Version-side sibling would have no purpose.
/// </para>
/// </remarks>
public interface IWorkflowDefinitionDraftValidation
{
    string Id { get; }

    string WorkflowDefinitionDraftId { get; }

    IReadOnlyList<ValidationError> Errors { get; }

    bool HasErrors => Errors.Count > 0;
}
