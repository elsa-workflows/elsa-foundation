using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Events;

/// <summary>
/// Coarse domain event fired after every granular FR-018 Draft mutation. Validators subscribe
/// to this and contribute errors via <see cref="AddValidationError"/>. Per Unit C FR-025.
/// </summary>
/// <remarks>
/// <para>
/// Sealed class per framework §2.6.1's intent-revealing-methods sub-rule. The backing list
/// is private; the read accessor is the public <see cref="Errors"/> property typed as
/// <see cref="IReadOnlyList{T}"/> — non-mutating by type, so public visibility is safe
/// (per Joey 2026-05-28: "read-only means items inside can also not be replaced or mutated;
/// all good"). <c>InternalsVisibleTo</c> is deliberately NOT used.
/// </para>
/// <para>
/// <b>Pipeline behaviour:</b> publisher MUST NEVER be broken by subscribers (framework
/// §2.6.1 + Unit C FR-027c). The default dispatcher composes
/// <c>Iterator → ExceptionShielding → Invoker</c> in <c>Elsa.Mediator</c>; per-handler
/// exceptions are caught + logged + swallowed; dispatch always completes. The publishing
/// command reads <see cref="Errors"/> after the handler chain completes and flushes
/// wholesale to <c>WorkflowDefinitionDraftValidation</c> per FR-023 delete-and-re-add.
/// </para>
/// </remarks>
public sealed class OnDraftValidating(IWorkflowDefinitionDraft draft) : IDomainEvent
{
    private readonly List<ValidationError> _errors = new();

    /// <summary>The post-mutation Draft being validated. Cross-<c>.Core</c> reference per §2.1.</summary>
    public IWorkflowDefinitionDraft Draft { get; } = draft;

    /// <summary>Contribute a validation error. Validators call this for every error they detect.</summary>
    public void AddValidationError(ValidationError error) => _errors.Add(error);

    /// <summary>
    /// Read-only view of the accumulated errors. Consumed by the publishing command after the
    /// handler chain completes; flushed wholesale to <c>WorkflowDefinitionDraftValidation</c>
    /// per FR-023. Public read access is safe — <see cref="IReadOnlyList{T}"/> is non-mutating
    /// by type; only <see cref="AddValidationError"/> can append.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors => _errors;
}
