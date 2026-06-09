using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Events;

/// <summary>
/// Coarse domain event fired after every granular FR-018 Draft mutation. The single
/// <c>ExecuteValidations</c> handler resolves all <see cref="Contracts.IDraftValidator"/>
/// implementations, runs each against <see cref="Draft"/>, and aggregates their returned errors
/// into <see cref="Errors"/>. Per Unit C FR-025.
/// </summary>
/// <remarks>
/// <see cref="Errors"/> is a directly-accessible <see cref="ICollection{T}"/>: the aggregating
/// handler writes into it; the publishing command reads it after dispatch and flushes wholesale
/// to <c>WorkflowDefinitionDraftValidation</c> per FR-023 delete-and-re-add. Individual validators
/// do not touch the event — they return their errors via <c>IDraftValidator.Validate</c>.
/// </remarks>
public sealed class OnDraftValidating(IWorkflowDefinitionDraft draft) : IEvent
{
    /// <summary>The post-mutation Draft being validated. Cross-<c>.Core</c> reference per §2.1.</summary>
    public IWorkflowDefinitionDraft Draft { get; } = draft;

    /// <summary>The accumulated validation errors. Populated by the aggregating handler.</summary>
    public ICollection<ValidationError> Errors { get; } = [];
}
