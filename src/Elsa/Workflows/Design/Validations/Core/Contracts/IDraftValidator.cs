using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Contracts;

/// <summary>
/// Contributor interface for Draft validation (Unit C FR-033/FR-034). A feature implements this
/// and registers it via DI; the single <c>ExecuteValidations</c> handler resolves
/// <see cref="IEnumerable{IDraftValidator}"/>, runs each, and aggregates the returned errors onto
/// the <c>DraftValidating</c> event. The interface — not the event — describes the intent.
/// </summary>
public interface IDraftValidator
{
    /// <summary>
    /// Inspect the post-mutation Draft and return any validation errors found. Return an empty
    /// sequence when the Draft is valid for this validator's concern. Implementations contribute
    /// errors by returning them; they never mutate the event.
    /// </summary>
    ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken);
}
