namespace Elsa.Mediator.Core.Contracts;

/// <summary>
/// Publishes <see cref="ILifecycleEvent"/>s. Implementations delegate to
/// <see cref="INotificationSender"/>; the type-narrowed surface keeps state-management
/// callers from accidentally publishing through the general-purpose notification sender
/// (and vice versa), and gives the framework a place to add lifecycle-only middleware
/// later without disturbing notification consumers.
/// </summary>
public interface ILifecycleEventSender
{
    /// <summary>Publishes using the configured default strategy.</summary>
    Task Send(ILifecycleEvent lifecycleEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes using the supplied strategy. Pass <c>null</c> to fall back to the default.
    /// </summary>
    Task SendAsync(
        ILifecycleEvent lifecycleEvent,
        IEventPublishingStrategy? strategy,
        CancellationToken cancellationToken = default
    );
}
