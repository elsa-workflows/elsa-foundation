using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

/// <summary>
/// Minimal hook-only <see cref="IEventPublisher"/>: runs an optional <see cref="OnPublish"/> hook on
/// each published event and captures nothing else. Tests use the hook to simulate validator
/// contributions onto <c>DraftValidating</c> (errors are derived state — the command and promotion
/// gates re-run the validation gate through the inline face to derive the current error set).
/// Implementing both delivery faces lets one instance satisfy every command ctor (inline gate,
/// deferred notifications, or both). Shared across the Groundwork design-persistence test suites.
/// </summary>
/// <remarks>
/// Named for its behaviour (hook-only) to distinguish it from the richer capturing publisher in the
/// Design.Tests infrastructure project — these are separate test projects and each stays
/// self-contained (no cross-test-project reference).
/// </remarks>
public sealed class HookEventPublisher : IInlineEventPublisher, IDeferredEventPublisher
{
    public Action<IEvent>? OnPublish { get; set; }
    public List<IEvent> InlineEvents { get; } = [];
    public List<IEvent> DeferredEvents { get; } = [];

    /// <summary>
    /// Convenience: install an <see cref="OnPublish"/> hook that contributes <paramref name="error"/>
    /// onto every <c>DraftValidating</c> pass, simulating a validator that always emits it. Replaces
    /// the copy-pasted "if DraftValidating add ValidationError" lambda at every call site.
    /// </summary>
    public void ContributeError(ValidationError error) =>
        OnPublish = e =>
        {
            if (e is DraftValidating validating)
                validating.Errors.Add(error);
        };

    Task IInlineEventPublisher.Publish(IEvent @event, CancellationToken cancellationToken) =>
        Publish(@event, InlineEvents);

    Task IDeferredEventPublisher.Publish(IEvent @event, CancellationToken cancellationToken) =>
        Publish(@event, DeferredEvents);

    private Task Publish(IEvent @event, ICollection<IEvent> events)
    {
        OnPublish?.Invoke(@event);
        events.Add(@event);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Captures deferred enqueues while honoring cancellation, so post-commit command tests can prove
/// that a cancelled request token cannot suppress a notification for a durable mutation.
/// </summary>
public sealed class CancellationAwareDeferredEventPublisher : IDeferredEventPublisher
{
    public List<IEvent> Events { get; } = [];
    public List<CancellationToken> CancellationTokens { get; } = [];

    public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
    {
        CancellationTokens.Add(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Events.Add(@event);
        return Task.CompletedTask;
    }
}
