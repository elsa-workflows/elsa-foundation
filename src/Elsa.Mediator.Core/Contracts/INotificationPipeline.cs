namespace Elsa.Mediator.Core.Contracts;

/// <summary>The composed notification dispatch pipeline. One instance per process (singleton).</summary>
public interface INotificationPipeline
{
    /// <summary>Replaces the pipeline composition with the supplied setup.</summary>
    NotificationMiddlewareDelegate Setup(Action<INotificationPipelineBuilder> setup);

    /// <summary>The composed delegate. Lazy-initialised to the default pipeline on first read.</summary>
    NotificationMiddlewareDelegate Pipeline { get; }

    /// <summary>Dispatches the given context through the composed pipeline.</summary>
    Task ExecuteAsync(INotificationContext context);
}
