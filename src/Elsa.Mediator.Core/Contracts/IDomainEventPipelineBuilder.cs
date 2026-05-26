using Elsa.Mediator.Core.Middleware;


namespace Elsa.Mediator.Core.Contracts;

/// <summary>
/// Represents a builder for building a notification pipeline.
/// </summary>
public interface IDomainEventPipelineBuilder
{
    /// <summary>
    /// Gets the properties associated with the notification pipeline.
    /// </summary>
    IDictionary<string, object?> Properties { get; }

    /// <summary>
    /// Gets the service provider.
    /// </summary>
    IServiceProvider ApplicationServices { get; }

    /// <summary>
    /// Adds a middleware to the notification pipeline.
    /// </summary>
    /// <param name="middleware">The middleware to add.</param>
    IDomainEventPipelineBuilder Use(Func<DomainEventMiddlewareDelegate, DomainEventMiddlewareDelegate> middleware);

    /// <summary>
    /// Builds the notification pipeline.
    /// </summary>
    /// <returns></returns>
    DomainEventMiddlewareDelegate Build();
}
