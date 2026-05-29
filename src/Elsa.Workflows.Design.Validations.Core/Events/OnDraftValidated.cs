using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Events;

/// <summary>
/// Lifecycle counterpart to <see cref="OnDraftValidating"/>. Fires AFTER the validation pass
/// completed AND the errors have been persisted alongside the Draft's state. Subscribers
/// (UI push, audit, telemetry) react to "validation just landed" — they don't feed back.
/// </summary>
/// <remarks>
/// <para>
/// Per framework §2.6.6, lifecycle events default to the <c>Background</c> publishing strategy.
/// The mutation pipeline returns to the caller before subscribers process this event; an
/// in-process channel + a hosted-task worker (<c>BackgroundEventPublisher</c>) deliver each
/// subscriber sequentially.
/// </para>
/// <para>
/// <see cref="Draft"/> is the same post-mutation snapshot validators saw on
/// <see cref="OnDraftValidating"/>. <see cref="Errors"/> is the persisted error set —
/// empty if validation found no issues.
/// </para>
/// </remarks>
public sealed class OnDraftValidated(IWorkflowDefinitionDraft draft, IReadOnlyList<ValidationError> errors) : ILifecycleEvent
{
    public IWorkflowDefinitionDraft Draft { get; } = draft;

    public IReadOnlyList<ValidationError> Errors { get; } = errors;

    public bool HasErrors => Errors.Count > 0;
}
