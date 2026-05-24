namespace Elsa.Mediator.Core.Contracts;

/// <summary>
/// Represents a command middleware.
/// </summary>
public interface ICommandMiddleware : IMiddleware
{
    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">The command context.</param>
    ValueTask Invoke(ICommandContext context);
}
