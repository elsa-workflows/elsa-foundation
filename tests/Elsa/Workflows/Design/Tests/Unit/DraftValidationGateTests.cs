using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// Pins the two <see cref="DraftValidationGate"/> variants' faulting semantics — the "unpinned
/// semantics" the code review flagged:
/// <list type="bullet">
/// <item><see cref="DraftValidationGate.DeriveValidationErrorsAsync"/> (write path) PROPAGATES a
///       throwing validator so the caller's mutation fails.</item>
/// <item><see cref="DraftValidationGate.TryDeriveValidationErrorsAsync"/> (read path) SHIELDS the
///       throw: it returns the errors accumulated so far plus one synthetic
///       <c>("$workflow", "Validation/Faulted")</c> marker, and never throws — except an
///       <see cref="OperationCanceledException"/>, which is a cancellation signal, not a validator
///       fault, and must propagate.</item>
/// </list>
/// </summary>
public sealed class DraftValidationGateTests
{
    private static readonly ValidationError PriorError =
        new("n1", "Test/Prior", "accumulated before the fault");

    [Fact]
    public async Task Unshielded_derive_propagates_a_throwing_validator()
    {
        var publisher = new ThrowingPublisher(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.DeriveValidationErrorsAsync(Draft(), CancellationToken.None));
    }

    [Fact]
    public async Task Shielded_derive_converts_a_throwing_validator_to_a_synthetic_faulted_error()
    {
        var publisher = new ThrowingPublisher(new InvalidOperationException("boom"));

        var errors = await publisher.TryDeriveValidationErrorsAsync(Draft(), CancellationToken.None);

        var faulted = Assert.Single(errors, e => e.Type == ValidationCategories.Faulted);
        Assert.Equal(ValidationPaths.Workflow, faulted.Path);
        Assert.Contains("InvalidOperationException", faulted.Message);
        Assert.Contains("boom", faulted.Message);
    }

    [Fact]
    public async Task Shielded_derive_keeps_errors_accumulated_before_the_fault()
    {
        // A validator contributes an error, then a later one throws: the shielded read returns both
        // the accumulated error and the synthetic Validation/Faulted marker.
        var publisher = new ThrowingPublisher(new InvalidOperationException("boom"), contributeFirst: PriorError);

        var errors = await publisher.TryDeriveValidationErrorsAsync(Draft(), CancellationToken.None);

        Assert.Contains(errors, e => e == PriorError);
        Assert.Contains(errors, e => e.Type == ValidationCategories.Faulted);
    }

    [Fact]
    public async Task Shielded_derive_lets_the_callers_own_cancellation_propagate()
    {
        // The CALLER's abort (its token is signalled) is not a validator fault — TryDerive must let it
        // propagate rather than swallow it into Validation/Faulted.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var publisher = new ThrowingPublisher(new OperationCanceledException(cts.Token));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => publisher.TryDeriveValidationErrorsAsync(Draft(), cts.Token));
    }

    [Fact]
    public async Task Shielded_derive_folds_a_validators_internal_cancellation_into_Validation_Faulted()
    {
        // A validator's INTERNAL timeout surfaces as an OperationCanceledException whose token is NOT
        // the caller's (the caller's token is unsignalled). That is a validator fault, not a caller
        // abort, so the shield folds it into the synthetic instead of turning the read into a 500.
        var publisher = new ThrowingPublisher(new TaskCanceledException("validator timed out"));

        var errors = await publisher.TryDeriveValidationErrorsAsync(Draft(), CancellationToken.None);

        Assert.Single(errors, e => e.Type == ValidationCategories.Faulted);
    }

    [Fact]
    public async Task Derive_reads_back_a_handler_contribution_made_inline()
    {
        // BITE (compile-pin + runtime): the gate takes IInlineEventPublisher and awaits the handler,
        // so a contribution made synchronously during Publish is visible when Errors is read back.
        // Reverting the fix — re-adding the strategy parameter or widening the gate back to
        // IEventPublisher — makes this stub no longer satisfy the gate signature and fails to compile,
        // which is the primary bite: a fire-and-forget publisher can no longer reach the gate at all.
        var publisher = new InlineSpyPublisher(PriorError);

        var errors = await publisher.DeriveValidationErrorsAsync(Draft(), CancellationToken.None);

        Assert.Contains(errors, e => e == PriorError);
    }

    private static IWorkflowDefinitionDraft Draft() => new StubDraft();

    /// <summary>
    /// Inline publisher whose <c>Publish(OnDraftValidating)</c> synchronously contributes an error
    /// before returning — modelling a validator whose effect the gate must observe on read-back.
    /// </summary>
    private sealed class InlineSpyPublisher(ValidationError contribution) : IInlineEventPublisher
    {
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
        {
            if (@event is OnDraftValidating validating)
                validating.Errors.Add(contribution);

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Inline publisher whose <c>Publish(OnDraftValidating)</c> optionally contributes an error, then
    /// always throws — modelling a faulting validator (or a throwing OnPublish hook) on the gate's chain.
    /// </summary>
    private sealed class ThrowingPublisher(Exception toThrow, ValidationError? contributeFirst = null) : IInlineEventPublisher
    {
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
        {
            if (@event is OnDraftValidating validating && contributeFirst is not null)
                validating.Errors.Add(contributeFirst);

            throw toThrow;
        }
    }

    private sealed class StubDraft : IWorkflowDefinitionDraft
    {
        public string Id => "draft-1";
        public string WorkflowDefinitionId => "wf-1";
        public WorkflowDefinitionState State { get; } = new([], null, [], [], null, null);
        public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UtcNow;
    }
}
