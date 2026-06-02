using Elsa.Pipelines.Core.Contracts;

namespace Elsa.Mediator.Core.Contracts;

/// <summary>
/// Represents a request middleware component.
/// </summary>
public interface IRequestMiddleware : IMiddleware
{
    /// <summary>
    /// Invokes the request middleware.
    /// </summary>
    /// <param name="context">The request context.</param>
    ValueTask Invoke(IRequestContext context);
}