namespace Elsa.Mediator.Core.Contracts;

/// <summary>
/// Strategy plug-point that controls HOW a notification gets dispatched to its handlers.
/// Implementations include sequential (await each in order), parallel (await fan-out),
/// background (enqueue + return), and any custom dispatch policy the application wants
/// to compose.
/// </summary>
public interface IEventPublishingStrategy
{
    Task PublishAsync(INotificationStrategyContext context);
}
