using Elsa.Pipelines.Core.Contracts;

namespace Elsa.Mediator.Core.Contracts;

/// <summary>
/// A middleware component in the unified mediator pipeline. Replaces the separate
/// <c>ICommandMiddleware</c>/<c>IRequestMiddleware</c> pair — commands and requests share one
/// pipeline shape, so they share one middleware abstraction.
/// </summary>
public interface IMessageMiddleware : IMiddleware
{
    /// <summary>Invokes the middleware.</summary>
    /// <param name="context">The dispatch context.</param>
    ValueTask Invoke(IMessageContext context);
}
