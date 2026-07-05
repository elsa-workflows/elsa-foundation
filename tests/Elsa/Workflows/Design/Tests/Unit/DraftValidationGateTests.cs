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

        var faulted = Assert.Single(errors, e => e.Type == "Validation/Faulted");
        Assert.Equal("$workflow", faulted.Path);
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
        Assert.Contains(errors, e => e.Type == "Validation/Faulted");
    }

    [Fact]
    public async Task Shielded_derive_lets_OperationCanceledException_propagate()
    {
        // Cancellation is not a validator fault — TryDerive must not swallow it into Validation/Faulted.
        var publisher = new ThrowingPublisher(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => publisher.TryDeriveValidationErrorsAsync(Draft(), CancellationToken.None));
    }

    private static IWorkflowDefinitionDraft Draft() => new StubDraft();

    /// <summary>
    /// Publisher whose <c>Publish(OnDraftValidating)</c> optionally contributes an error, then always
    /// throws — modelling a faulting validator (or a throwing OnPublish hook) on the gate's chain.
    /// </summary>
    private sealed class ThrowingPublisher(Exception toThrow, ValidationError? contributeFirst = null) : IEventPublisher
    {
        public Task Publish(IEvent @event, IEventPublishingStrategy? strategy = null, CancellationToken cancellationToken = default)
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
