namespace Elsa.Events.Core.Contracts;

/// <summary>
/// Strategy plug-point that controls HOW an event gets dispatched to its handlers.
/// Implementations include sequential (await each in order), parallel (await fan-out),
/// background (enqueue + return), and any custom dispatch policy the application wants
/// to compose. A strategy that decouples from the caller (e.g. Background) owns its own
/// resilience — catching and logging handler failures so they never reach the publisher.
/// </summary>
public interface IEventPublishingStrategy
{
    Task PublishAsync(IEventStrategyContext context);
}
