using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core;

/// <summary>
/// The single validation gate. Deriving errors means publishing <see cref="OnDraftValidating"/>
/// on the <b>Sequential</b> strategy: the one <c>ExecuteValidations</c> handler resolves every
/// <see cref="Contracts.IDraftValidator"/>, runs each against the post-mutation Draft, and
/// aggregates their returned errors onto the event (§2.6.1 contribution). The publisher awaits the
/// full chain; we read the accumulated <see cref="OnDraftValidating.Errors"/> back afterwards.
/// </summary>
/// <remarks>
/// Errors are derived state — recomputed from scratch against the current Draft on every pass and
/// never persisted. Every create/update/promote/read site that needs the error set derives it
/// through this gate rather than hand-rolling the publish + read-back.
/// </remarks>
public static class DraftValidationGate
{
    /// <summary>
    /// Derives the validation error set for <paramref name="draft"/> by publishing
    /// <see cref="OnDraftValidating"/> on the <b>Sequential</b> strategy and reading the aggregated
    /// errors back. This IS the validation gate — a throwing validator propagates (the caller's
    /// write should fail). Use <see cref="TryDeriveValidationErrorsAsync"/> instead on read paths
    /// that must stay resilient to a faulting validator.
    /// </summary>
    public static async Task<IReadOnlyList<ValidationError>> DeriveValidationErrorsAsync(
        this IEventPublisher eventPublisher,
        IWorkflowDefinitionDraft draft,
        CancellationToken cancellationToken)
    {
        var validatingEvent = new OnDraftValidating(draft);
        // Default strategy is Sequential (synchronous, in-order, awaited, propagates handler faults) —
        // omitted here so Validations.Core need not reference the Strategies package.
        await eventPublisher.Publish(validatingEvent, cancellationToken: cancellationToken);
        return validatingEvent.Errors.ToArray();
    }

    /// <summary>
    /// Shielded variant of <see cref="DeriveValidationErrorsAsync"/> for read paths (e.g. GET-draft):
    /// derives the error set, but on a validator throwing returns the errors accumulated so far PLUS
    /// one synthetic <see cref="ValidationError"/> under the reserved <c>Validation/Faulted</c>
    /// category (R3) on the <c>$workflow</c> path (R2), carrying the exception type and message. The
    /// Draft stays openable and the fault is surfaced instead of turning every read into a 500.
    /// </summary>
    /// <remarks>
    /// <c>Validation/Faulted</c> is a reserved R3 category (see <see cref="ValidationError"/>): it is
    /// never emitted by a validator, only by this shield when a validator faults.
    /// </remarks>
    public static async Task<IReadOnlyList<ValidationError>> TryDeriveValidationErrorsAsync(
        this IEventPublisher eventPublisher,
        IWorkflowDefinitionDraft draft,
        CancellationToken cancellationToken)
    {
        var validatingEvent = new OnDraftValidating(draft);
        try
        {
            await eventPublisher.Publish(validatingEvent, cancellationToken: cancellationToken);
            return validatingEvent.Errors.ToArray();
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a validator fault — let the caller's abort propagate.
            throw;
        }
        catch (Exception exception)
        {
            // Return whatever validators accumulated before the fault, plus a synthetic marker so the
            // Draft remains readable and the fault is visible.
            var faulted = new ValidationError(
                Path: "$workflow",
                Type: "Validation/Faulted",
                Message: $"{exception.GetType().Name}: {exception.Message}");

            return [.. validatingEvent.Errors, faulted];
        }
    }
}
