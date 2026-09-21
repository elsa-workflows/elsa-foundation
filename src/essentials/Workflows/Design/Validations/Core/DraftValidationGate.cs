using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core;

/// <summary>
/// The single validation gate. Deriving errors means publishing <see cref="DraftValidating"/>:
/// the one <c>ExecuteValidations</c> handler resolves every <see cref="Contracts.IDraftValidator"/>,
/// runs each against the post-mutation Draft, and aggregates their returned errors onto the event
/// (§2.6.1 contribution). The publisher awaits the full chain; we read the accumulated
/// <see cref="DraftValidating.Errors"/> back afterwards.
/// </summary>
/// <remarks>
/// <para>
/// Errors are derived state — recomputed from scratch against the current Draft on every pass and
/// never persisted. Every create/update/promote/read site that needs the error set derives it
/// through this gate rather than hand-rolling the publish + read-back.
/// </para>
/// <para>
/// The gate depends on <see cref="IInlineEventPublisher"/> — inline delivery awaits every handler,
/// so the errors are fully aggregated by the time we read them back. Depending on the inline face
/// (rather than <see cref="IEventPublisher"/> plus a caller-supplied strategy) makes the wrong
/// semantics unrepresentable AT THE GATE: nobody can hand it a fire-and-forget publisher that would
/// return before validators run and leave the error set silently empty. <c>.Core</c> depends only
/// on <see cref="IInlineEventPublisher"/> (an <c>Elsa.Events.Core</c> abstraction), preserving
/// framework §2.1 purity.
/// </para>
/// </remarks>
public static class DraftValidationGate
{
    /// <summary>
    /// Derives the validation error set for <paramref name="draft"/> by publishing
    /// <see cref="DraftValidating"/> inline (every handler awaited) and reading the aggregated
    /// errors back. This IS the validation gate — a throwing validator propagates (the caller's
    /// write should fail). Use <see cref="TryDeriveValidationErrorsAsync"/> instead on read paths
    /// that must stay resilient to a faulting validator.
    /// </summary>
    public static async Task<IReadOnlyList<ValidationError>> DeriveValidationErrorsAsync(
        this IInlineEventPublisher eventPublisher,
        IWorkflowDefinitionDraft draft,
        CancellationToken cancellationToken)
    {
        var validatingEvent = new DraftValidating(draft);
        await eventPublisher.Publish(validatingEvent, cancellationToken);
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
        this IInlineEventPublisher eventPublisher,
        IWorkflowDefinitionDraft draft,
        CancellationToken cancellationToken)
    {
        var validatingEvent = new DraftValidating(draft);
        try
        {
            // Inline delivery awaits every handler — see DeriveValidationErrorsAsync.
            await eventPublisher.Publish(validatingEvent, cancellationToken);
            return validatingEvent.Errors.ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller's own abort — let it propagate. A validator's INTERNAL timeout
            // (TaskCanceledException whose token is not our cancellationToken) is NOT a caller abort,
            // so it falls through to the shield below and folds into the Validation/Faulted synthetic.
            throw;
        }
        catch (Exception exception)
        {
            // Return whatever validators accumulated before the fault, plus a synthetic marker so the
            // Draft remains readable and the fault is visible.
            var faulted = new ValidationError(
                Path: ValidationPaths.Workflow,
                Type: ValidationCategories.Faulted,
                Message: $"{exception.GetType().Name}: {exception.Message}");

            return [.. validatingEvent.Errors, faulted];
        }
    }
}
