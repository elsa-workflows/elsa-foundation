namespace Elsa.Mediator.Notifications.Strategies;

/// <summary>
/// Pre-built singleton instances of the three baseline publishing strategies. Callers pass
/// these into <c>INotificationSender.SendAsync(...)</c> to override the per-call dispatch
/// behaviour without resolving a strategy from DI.
/// </summary>
public static class NotificationStrategy
{
    /// <summary>Awaited; handlers run in sequence.</summary>
    public static readonly SequentialProcessingStrategy Sequential = new();

    /// <summary>Awaited fan-out; handlers run in parallel.</summary>
    public static readonly ParallelProcessingStrategy Parallel = new();

    /// <summary>Fire-and-forget; the call returns before handlers run.</summary>
    public static readonly BackgroundProcessingStrategy Background = new();
}
